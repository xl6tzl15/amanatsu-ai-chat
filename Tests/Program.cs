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

// Dialogue pages: every page fits three wrapped lines (with the ▼ marker when more follow) and no text is lost.
void CheckPages(string name, string text)
{
    var result = DialoguePages.Split(text);
    if (result.Count == 0) throw new Exception($"{name}: no pages");
    for (var i = 0; i < result.Count; i++)
        if (!DialoguePages.Fits(result[i], i < result.Count - 1)) throw new Exception($"{name}: page {i} is longer than three lines");
    string Strip(string x) => new string(x.Where(c => !char.IsWhiteSpace(c)).ToArray());
    if (Strip(string.Concat(result)) != Strip(text)) throw new Exception($"{name}: text was lost");
}
if (DialoguePages.Split("こんにちは。").Count != 1) throw new Exception("short text was split");
var longText = string.Concat(Enumerable.Repeat("ゼロ知識証明は、秘密を明かさずに知っていることを示す方法です。", 6));
var pages = DialoguePages.Split(longText);
if (pages.Count < 2) throw new Exception("long text was not split");
if (!pages.All(p => p.EndsWith("。"))) throw new Exception("pages should end at sentence boundaries");
CheckPages("long", longText);
CheckPages("run-on", new string('あ', 250));
CheckPages("english", string.Concat(Enumerable.Repeat("The observatory is open tonight, so come and look at the stars with me. ", 5)));
CheckPages("quoted", "聞こえたよ。「" + new string('い', 80) + "」だね。");
var manyLines = "うん。\nそうだね。\nでも。\nほんとに？\nまたね。";
if (DialoguePages.Split(manyLines).Count < 2) throw new Exception("five short lines should not fit one page");
CheckPages("many lines", manyLines);
CheckPages("blank lines", "はい。\n\n\n\nそれで？\n\n\nうん。");
var spaced = "今日ね、\n\nちょっと\n\n面白いことがあったんだ。\n\n新しいカフェに\n\n寄ったら、\n\nすごく素敵な\n\n香りがしてたの。";
if (DialoguePages.Split(spaced).Count != 3) throw new Exception($"blank-line reply: expected 3 pages, got {DialoguePages.Split(spaced).Count}");
CheckPages("blank-line reply", spaced);
CheckPages("spaces only", new string('　', 150));
CheckPages("empty", "");
if (DialoguePages.Split(new string('　', 150)).Count != 1) throw new Exception("blank text should give one page");
Console.WriteLine($"Dialogue pages: {pages.Count} pages for {longText.Length} characters, line and blank cases passed.");
