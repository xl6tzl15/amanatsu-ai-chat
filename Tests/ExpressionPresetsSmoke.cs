using Amanatsu.AiChat.Config;
using Amanatsu.AiChat.GameAdapter;

var defaults = ExpressionCatalog.Defaults();
if (defaults.Count < 20 || defaults.Values.Any(value => string.IsNullOrWhiteSpace(value.Description)))
    throw new Exception("Default expression descriptions are missing");
var temp = Path.Combine(Path.GetTempPath(), $"amanatsu-expression-{Guid.NewGuid():N}.json");
try
{
    var created = GlobalExpressionPresets.LoadOrCreate(temp, defaults);
    if (created.Count != defaults.Count) throw new Exception("Global seed mismatch");
    var custom = new ExpressionPreset(2, 3, 19, .6f, "赤面しながら優しく微笑む")
    {
        EyesOpen = .8f, MouthOpen = .1f
    };
    if (!ExpressionPresetRules.Validate("gentle_blush", custom, 11, 25, 29, out var error))
        throw new Exception(error);
    created["gentle_blush"] = custom;
    GlobalExpressionPresets.Save(temp, created);
    if (!File.Exists(temp + ".bak")) throw new Exception("Previous preset file was not backed up");
    var loaded = GlobalExpressionPresets.LoadOrCreate(temp, defaults);
    var roundtrip = loaded["gentle_blush"];
    if (roundtrip.Description != custom.Description || roundtrip.Blush != .6f
        || roundtrip.EyesOpen != .8f || roundtrip.MouthOpen != .1f || roundtrip.EyesOpen == null)
        throw new Exception("Preset values did not survive save/load");
    var invalid = new ExpressionPreset(2, 3, 19, 0, "bad") { MouthOpen = 1.5f };
    if (ExpressionPresetRules.Validate("bad", invalid, 11, 25, 29, out _))
        throw new Exception("Invalid open range accepted");
    Console.WriteLine($"Expression presets: {loaded.Count} global entries, descriptions, blush and open ranges passed.");
}
finally
{
    if (File.Exists(temp)) File.Delete(temp);
    if (File.Exists(temp + ".bak")) File.Delete(temp + ".bak");
}
