using Amanatsu.AiChat.Sequence;

foreach (var valid in new[] { "undress", "half_undress", "dress", "top:undress", "bra:half_undress", "socks:dress", "preset:nude" })
    if (!OutfitIntent.IsValidAction(valid)) throw new Exception($"Rejected valid action: {valid}");
foreach (var invalid in new[] { "top:unknown", "other:undress", "top:undress:now", "preset:unknown" })
    if (OutfitIntent.IsValidAction(invalid)) throw new Exception($"Accepted invalid action: {invalid}");

var expectedPresets = new Dictionary<string, string>
{
    ["dressed"] = "dress,dress,dress,dress",
    ["bra_show"] = "undress,dress,dress,dress",
    ["panties_show"] = "dress,dress,undress,dress",
    ["underwear"] = "undress,dress,undress,dress",
    ["topless"] = "undress,undress,dress,dress",
    ["bottomless"] = "dress,dress,undress,undress",
    ["nude"] = "undress,undress,undress,undress"
};
foreach (var (name, expected) in expectedPresets)
{
    if (!OutfitPresets.TryGet(name, out var targets)) throw new Exception($"Missing preset: {name}");
    var actual = string.Join(',', new[] { "top", "bra", "bottom", "shorts" }.Select(part => targets[part]));
    if (actual != expected) throw new Exception($"Preset {name}: expected {expected}, got {actual}");
    if (!OutfitIntent.IsValidAction($"preset:{name}")) throw new Exception($"Preset action invalid: {name}");
}
if (OutfitPresets.TryGet("unknown", out _)) throw new Exception("Unknown preset accepted");
Console.WriteLine($"Outfit vocabulary: 7 valid, 4 invalid actions and {expectedPresets.Count} presets passed.");
