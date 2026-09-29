using System.Text;
using System.Text.RegularExpressions;

namespace Amanatsu.AiChat.Sequence;

// Splits a long line into pages that fit the native dialogue window: at most three wrapped lines.
// Width is estimated: a full-width character counts 1, a half-width one about half of that.
// The window holds about 36 full-width characters per line; 34 leaves a margin.
internal static class DialoguePages
{
    public const float LineWidth = 34f;
    public const int MaxLines = 3;
    public const string MoreMarker = " ▼";
    private static readonly char[] SentenceEnds = { '。', '！', '？', '!', '?', '…', '.', '\n' };
    private static readonly char[] ClauseEnds = { '、', '，', ',', '；', ';', '：', ':', ' ' };

    public static float Width(string text)
    {
        var width = 0f;
        foreach (var ch in text) width += ch < 0x2E80 ? 0.55f : 1f;
        return width;
    }

    // Wrapped line count; an explicit newline always starts a new line.
    public static int Lines(string text)
    {
        var lines = 0;
        foreach (var line in text.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(Width(line) / LineWidth));
        return lines;
    }

    // Whether the text fits one page; a page followed by more pages also carries the marker.
    public static bool Fits(string text, bool withMarker = true) =>
        Lines(withMarker ? text.TrimEnd() + MoreMarker : text.TrimEnd()) <= MaxLines;

    public static List<string> Split(string text)
    {
        var pages = new List<string>();
        // The window has only three lines, so a run of blank lines becomes a single line break.
        text = Regex.Replace(text ?? "", @"\n[ \t　]*(?:\n[ \t　]*)+", "\n");
        if (text.Length == 0 || Fits(text, false))
        {
            pages.Add(text);
            return pages;
        }
        var page = new StringBuilder();
        // Pieces never exceed one line, so a page can always take at least one of them.
        foreach (var piece in Pieces(text, LineWidth - Width(MoreMarker)))
        {
            if (page.Length > 0 && !Fits(page + piece))
            {
                AddPage(pages, page.ToString());
                page.Clear();
            }
            page.Append(piece);
        }
        AddPage(pages, page.ToString());
        if (pages.Count == 0) pages.Add("");
        return pages;
    }

    private static void AddPage(List<string> pages, string page)
    {
        page = page.Trim();
        if (page.Length > 0) pages.Add(page);
    }

    // Sentences first; a sentence wider than the limit is cut at clauses, then by width.
    private static IEnumerable<string> Pieces(string text, float limit)
    {
        foreach (var sentence in Cut(text, SentenceEnds))
        {
            if (Width(sentence.TrimEnd('\n')) <= limit) { yield return sentence; continue; }
            foreach (var clause in Cut(sentence, ClauseEnds))
            {
                if (Width(clause.TrimEnd('\n')) <= limit) { yield return clause; continue; }
                var chunk = new StringBuilder();
                foreach (var ch in clause)
                {
                    if (chunk.Length > 0 && Width(chunk.ToString()) + Width(ch.ToString()) > limit)
                    {
                        yield return chunk.ToString();
                        chunk.Clear();
                    }
                    chunk.Append(ch);
                }
                if (chunk.Length > 0) yield return chunk.ToString();
            }
        }
    }

    // Keeps each delimiter (and closing quotes right after it) with the text before it.
    private static IEnumerable<string> Cut(string text, char[] delimiters)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (Array.IndexOf(delimiters, text[i]) < 0) continue;
            var end = i + 1;
            while (end < text.Length && (Array.IndexOf(delimiters, text[end]) >= 0 || "」』）)\"'".IndexOf(text[end]) >= 0)) end++;
            yield return text.Substring(start, end - start);
            start = end;
            i = end - 1;
        }
        if (start < text.Length) yield return text.Substring(start);
    }
}
