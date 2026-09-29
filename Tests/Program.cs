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

// Dialogue pages: short text stays whole; long text splits at sentence ends and every page fits.
if (DialoguePages.Split("こんにちは。").Count != 1) throw new Exception("short text was split");
var longText = string.Concat(Enumerable.Repeat("ゼロ知識証明は、秘密を明かさずに知っていることを示す方法です。", 6));
var pages = DialoguePages.Split(longText);
if (pages.Count < 2) throw new Exception("long text was not split");
if (pages.Any(p => DialoguePages.Width(p) > DialoguePages.PageWidth)) throw new Exception("a page is too wide");
if (!pages.All(p => p.EndsWith("。"))) throw new Exception("pages should end at sentence boundaries");
if (string.Concat(pages) != longText) throw new Exception("paging lost text");
var runOn = new string('あ', 250);
var runOnPages = DialoguePages.Split(runOn);
if (string.Concat(runOnPages) != runOn || runOnPages.Any(p => DialoguePages.Width(p) > DialoguePages.PageWidth)) throw new Exception("run-on text paging failed");
var english = string.Concat(Enumerable.Repeat("The observatory is open tonight, so come and look at the stars with me. ", 5));
if (DialoguePages.Split(english).Any(p => DialoguePages.Width(p) > DialoguePages.PageWidth)) throw new Exception("english paging failed");
var quoted = "聞こえたよ。「" + new string('い', 80) + "」だね。";
var quotedPages = DialoguePages.Split(quoted);
if (string.Concat(quotedPages) != quoted || DialoguePages.Width(quotedPages[0]) < DialoguePages.PageWidth / 2)
    throw new Exception("a short first sentence should not leave the first page nearly empty");
Console.WriteLine($"Dialogue pages: {pages.Count} pages for {longText.Length} characters passed.");
