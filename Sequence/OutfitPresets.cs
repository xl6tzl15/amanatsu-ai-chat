namespace Amanatsu.AiChat.Sequence;

// These are complete silhouettes for the four principal garments. Accessories
// are changed only where they would conceal underwear, or for fully dressed/nude.
internal static class OutfitPresets
{
    public static readonly string[] Keys =
        { "dressed", "bra_show", "panties_show", "underwear", "topless", "bottomless", "nude" };

    public static bool TryGet(string key, out IReadOnlyDictionary<string, string> targets)
    {
        var selected = key switch
        {
            "dressed" => All("dress"),
            "bra_show" => Core("undress", "dress", "dress", "dress"),
            "panties_show" => RevealBottom("dress", "dress", "undress", "dress"),
            "underwear" => RevealBottom("undress", "dress", "undress", "dress"),
            "topless" => Core("undress", "undress", "dress", "dress"),
            "bottomless" => RevealBottom("dress", "dress", "undress", "undress"),
            "nude" => All("undress"),
            _ => null
        };
        targets = selected ?? new Dictionary<string, string>();
        return selected != null;
    }

    private static Dictionary<string, string> Core(string top, string bra, string bottom, string shorts) =>
        new Dictionary<string, string>
        {
            ["top"] = top, ["bra"] = bra, ["bottom"] = bottom, ["shorts"] = shorts
        };

    private static IReadOnlyDictionary<string, string> RevealBottom(string top, string bra, string bottom, string shorts)
    {
        var targets = new Dictionary<string, string>(Core(top, bra, bottom, shorts))
        {
            ["pantyhose"] = "undress"
        };
        return targets;
    }

    private static IReadOnlyDictionary<string, string> All(string state) =>
        OutfitIntent.PartKeys.ToDictionary(key => key, _ => state);
}
