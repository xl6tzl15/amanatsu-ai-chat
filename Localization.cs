namespace Amanatsu.AiChat;

// One DLL serves both editions. "auto" follows AmanatsuAiChat/language.txt, which the
// English package ships with "en"; anything else falls back to Japanese.
internal static class L
{
    public static bool En { get; private set; }
    public static string Code => En ? "en" : "ja";

    public static void Init(string setting, string pluginDirectory)
    {
        var value = (setting ?? "auto").Trim().ToLowerInvariant();
        if (value == "auto")
        {
            try
            {
                var marker = Path.Combine(pluginDirectory ?? "", "AmanatsuAiChat", "language.txt");
                value = File.Exists(marker) ? File.ReadAllText(marker).Trim().ToLowerInvariant() : "ja";
            }
            catch (IOException) { value = "ja"; }
        }
        En = value == "en";
    }

    public static string T(string ja, string en) => En ? en : ja;
}
