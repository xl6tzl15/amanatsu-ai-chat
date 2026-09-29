using System.Text;

namespace Amanatsu.AiChat.Sequence;

// Splits a long line into pages that fit the native dialogue window (about three lines).
// Width is estimated: a full-width character counts 1, a half-width one about half of that.
internal static class DialoguePages
{
    public const float PageWidth = 90f;
    private static readonly char[] SentenceEnds = { '。', '！', '？', '!', '?', '…', '.', '\n' };
    private static readonly char[] ClauseEnds = { '、', '，', ',', '；', ';', '：', ':', ' ' };

    public static float Width(string text)
    {
        var width = 0f;
        foreach (var ch in text) width += ch < 0x2E80 ? 0.55f : 1f;
        return width;
    }

    public static List<string> Split(string text, float pageWidth = PageWidth)
    {
        var pages = new List<string>();
        if (string.IsNullOrEmpty(text) || Width(text) <= pageWidth)
        {
            pages.Add(text ?? "");
            return pages;
        }
        var page = new StringBuilder();
        foreach (var piece in Pieces(text, pageWidth))
        {
            if (page.Length > 0 && Width(page.ToString()) + Width(piece) > pageWidth)
            {
                // A page that is still mostly empty takes the next sentence clause by clause.
                if (Width(page.ToString()) < pageWidth / 2)
                {
                    foreach (var clause in Pieces(piece, pageWidth / 4))
                    {
                        if (Width(page.ToString()) + Width(clause) > pageWidth)
                        {
                            pages.Add(page.ToString().Trim());
                            page.Clear();
                        }
                        page.Append(clause);
                    }
                    continue;
                }
                pages.Add(page.ToString().Trim());
                page.Clear();
            }
            page.Append(piece);
        }
        if (page.ToString().Trim().Length > 0) pages.Add(page.ToString().Trim());
        return pages;
    }

    // Sentences first; a sentence wider than a page is cut at clauses, then by width.
    private static IEnumerable<string> Pieces(string text, float pageWidth)
    {
        foreach (var sentence in Cut(text, SentenceEnds))
        {
            if (Width(sentence) <= pageWidth) { yield return sentence; continue; }
            foreach (var clause in Cut(sentence, ClauseEnds))
            {
                if (Width(clause) <= pageWidth) { yield return clause; continue; }
                var chunk = new StringBuilder();
                foreach (var ch in clause)
                {
                    if (Width(chunk.ToString()) + Width(ch.ToString()) > pageWidth)
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
