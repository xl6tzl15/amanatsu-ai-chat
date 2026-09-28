using BepInEx.Configuration;
using UnityEngine;

namespace Amanatsu.AiChat.GameAdapter;

// The native options window owns its UI and validation. These entries persist
// the chat's graphics preferences while the game's previous state is restored on exit.
internal sealed class GraphicPreferences
{
    private readonly ConfigEntry<bool> _saved;
    private readonly ConfigEntry<bool> _followGame;
    private readonly Dictionary<string, ConfigEntry<bool>> _flags = new();
    private readonly ConfigEntry<int> _aa;
    private readonly ConfigEntry<int> _quality;
    private readonly ConfigEntry<string> _background;
    public readonly ConfigEntry<LightShadows> ShadowType;
    private Dictionary<string, bool> _baseline;
    private int _baselineAa;
    private int _baselineQuality;
    private Color32 _baselineBackground;
    private static readonly string[] Names = { "Bloom", "DepthOfField", "Vignette", "SSAO", "Fog", "Map", "Shield" };

    public GraphicPreferences(ConfigFile config)
    {
        _saved = config.Bind("Graphics", "UseSavedSettings", false, "Import the game's graphics on first entry; then use the saved chat settings. Native options changes update these entries.");
        _followGame = config.Bind("Graphics", "FollowGameSettings", true, "Use the game's current graphics settings when entering chat. Set false only to apply this Mod's saved Graphics overrides.");
        foreach (var name in Names) _flags[name] = config.Bind("Graphics", name, true, "Native game graphics setting, synchronized when the options window closes.");
        _aa = config.Bind("Graphics", "Antialiasing", 0, "Native antialiasing selection. Use the game options screen to select a supported mode.");
        _quality = config.Bind("Graphics", "Quality", 0, "Native graphics preset metadata; individual effects are stored separately.");
        _background = config.Bind("Graphics", "BackgroundColor", "#101010FF", "Conversation background in HTML RGBA format.");
        ShadowType = config.Bind("Graphics", "ShadowType", LightShadows.Soft, "Conversation light shadows: None, Hard, or Soft.");
    }

    private static Dictionary<string, bool> Read()
    {
        var g = Manager.Config.GraphicData;
        return new() { ["Bloom"] = g.Bloom, ["DepthOfField"] = g.DepthOfField,
            ["Vignette"] = g.Vignette, ["SSAO"] = g.SSAO, ["Fog"] = g.Fog,
            ["Map"] = g.Map, ["Shield"] = g.Shield };
    }

    private static void Write(Dictionary<string, bool> v, int aa)
    {
        var g = Manager.Config.GraphicData;
        g.Bloom = v["Bloom"]; g.DepthOfField = v["DepthOfField"]; g.Vignette = v["Vignette"];
        g.SSAO = v["SSAO"]; g.Fog = v["Fog"]; g.Map = v["Map"]; g.Shield = v["Shield"];
        g.Antialiasing = aa;
        Refresh();
    }

    public static void Refresh()
    {
        foreach (var effect in UnityEngine.Object.FindObjectsOfType<AL.Config.ConfigEffectorVolume>(true))
            if (effect != null && effect.isActiveAndEnabled) effect.Refresh();
    }

    public void Enter()
    {
        if (_baseline != null || Manager.Config.GraphicData == null) return;
        _baseline = Read(); _baselineAa = Manager.Config.GraphicData.Antialiasing;
        _baselineQuality = Manager.Config.GraphicData.Quality; _baselineBackground = Manager.Config.GraphicData.BackColor;
        if (_followGame.Value)
        {
            Capture();
            Refresh();
            return;
        }
        if (!_saved.Value) Capture();
        Write(_flags.ToDictionary(p => p.Key, p => p.Value.Value), _aa.Value);
        Manager.Config.GraphicData.Quality = _quality.Value;
        if (ColorUtility.TryParseHtmlString(_background.Value, out var color)) Manager.Config.GraphicData.BackColor = color;
    }

    public void Capture()
    {
        if (Manager.Config.GraphicData == null) return;
        foreach (var p in Read()) _flags[p.Key].Value = p.Value;
        _aa.Value = Manager.Config.GraphicData.Antialiasing;
        _quality.Value = Manager.Config.GraphicData.Quality;
        _background.Value = "#" + ColorUtility.ToHtmlStringRGBA(Manager.Config.GraphicData.BackColor);
        _saved.Value = true;
    }

    public void OptionsClosed()
    {
        // Native options intentionally save the user's game preferences too.
        Capture(); _baseline = Read(); _baselineAa = Manager.Config.GraphicData.Antialiasing;
        _baselineQuality = Manager.Config.GraphicData.Quality; _baselineBackground = Manager.Config.GraphicData.BackColor;
        Refresh();
    }

    public void Exit()
    {
        if (_baseline == null) return;
        Write(_baseline, _baselineAa); _baseline = null;
        Manager.Config.GraphicData.Quality = _baselineQuality; Manager.Config.GraphicData.BackColor = _baselineBackground;
    }

    public object Diagnostics() => new { active = _baseline != null,
        followGame = _followGame.Value,
        flags = Manager.Config.GraphicData == null ? null : Read(),
        antialiasing = Manager.Config.GraphicData?.Antialiasing, shadowType = ShadowType.Value.ToString() };
}
