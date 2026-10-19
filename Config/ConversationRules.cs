using System.Text.Json;
using System.Text.Json.Nodes;

namespace Amanatsu.AiChat.Config;

// Wording around the conversation that users may edit: the opening instruction, the line used
// when a clothing change does nothing, the dialogue length, the clothing vocabulary, the
// sex_timing/arousal criteria and the standing-pose descriptions. The JSON reply format, the
// field order and the "the player decides with the button" rule stay in code (bridge.py).
// Edits take effect from the next message. Each entry is read on its own: a blank or malformed
// entry falls back to the shipped wording, and a file that cannot be parsed keeps the last rules.
public sealed class ConversationRules
{
    public string GreetingInstruction { get; private set; }
    public string OutfitUnavailableLine { get; private set; }
    public string DialogueStyle { get; private set; }
    public int DialogueMaxLength { get; private set; }
    public string OutfitVocabulary { get; private set; }
    public string SexTimingRule { get; private set; }
    public string ArousalRule { get; private set; }
    public IReadOnlyDictionary<string, string> PoseDescriptions { get; private set; }

    // (message, isWarning); set by the plugin so tests can run without BepInEx.
    public static Action<string, bool> Log { get; set; } = (_, _) => { };
    // Tests point this at a temporary file.
    internal static string PathOverride { get; set; }

    public static string PathOnDisk => PathOverride ?? Path.GetFullPath(L.En
        ? "BepInEx/config/amanatsu.ai-chat/conversation-rules-en.json"
        : "BepInEx/config/amanatsu.ai-chat/conversation-rules.json");

    private static readonly object Gate = new();
    private static ConversationRules _current;
    private static DateTime _loadedStamp;
    private static DateTime _failedStamp;

    // Called from the main thread and from the reply task; one caller reloads at a time.
    public static ConversationRules Current
    {
        get { lock (Gate) { Reload(); return _current; } }
    }

    // The part the bridge places into the fixed protocol rules, ahead of "Current state".
    public object BridgeRules() => new
    {
        dialogue_style = DialogueStyle,
        dialogue_max_length = DialogueMaxLength,
        outfit_vocabulary = OutfitVocabulary,
        sex_timing_rule = SexTimingRule,
        arousal_rule = ArousalRule
    };

    private static void Reload()
    {
        var path = PathOnDisk;
        DateTime stamp;
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, DefaultJson());
            }
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            _current ??= Defaults();
            Log($"Conversation rules file is not accessible right now: {ex.Message}", true);
            return;
        }
        if (_current != null && (stamp == _loadedStamp || stamp == _failedStamp)) return;

        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException)
        {
            // Usually an editor still writing the file; the next message tries again.
            _current ??= Defaults();
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Permissions do not change by themselves, so warn once per file version.
            _current ??= Defaults();
            _failedStamp = stamp;
            Log($"Conversation rules file cannot be read: {ex.Message}", true);
            return;
        }

        var defaults = Defaults();
        try
        {
            var root = JsonNode.Parse(text, null, new JsonDocumentOptions
                { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidDataException("the file must contain one JSON object");
            var bad = new List<string>();
            var rules = FromJson(root, defaults, bad);
            _current = rules;
            _loadedStamp = stamp;
            _failedStamp = default;
            Log($"Conversation rules loaded: {path}", false);
            if (bad.Count > 0)
                Log(L.T($"会話ルールファイルの次の項目は読めなかったため、初期の文章を使います: {string.Join(", ", bad)}",
                    $"These conversation rules entries could not be read, so the original wording is used: {string.Join(", ", bad)}"), true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            _current ??= defaults;
            _failedStamp = stamp;
            Log(L.T($"会話ルールファイルを読み込めませんでした。直前の内容を使います: {ex.Message}",
                $"The conversation rules file could not be read; the previous rules stay in use: {ex.Message}"), true);
        }
    }

    private static ConversationRules FromJson(JsonObject root, ConversationRules defaults, List<string> bad)
    {
        string Text(string name, string fallback)
        {
            var node = Find(root, name);
            if (node == null) return fallback;
            if (node is JsonValue value && value.TryGetValue<string>(out var s))
                return string.IsNullOrWhiteSpace(s) ? fallback : s;
            bad.Add(name);
            return fallback;
        }
        int Number(string name, int fallback)
        {
            var node = Find(root, name);
            if (node == null) return fallback;
            if (node is JsonValue value && value.TryGetValue<int>(out var n) && n >= 1) return n;
            bad.Add(name);
            return fallback;
        }
        var version = Find(root, "Version");
        if (version is not JsonValue v || !v.TryGetValue<int>(out var number) || number != 1)
            throw new InvalidDataException("Version must be 1");

        var poses = new Dictionary<string, string>(defaults.PoseDescriptions, StringComparer.OrdinalIgnoreCase);
        var poseNode = Find(root, "PoseDescriptions");
        if (poseNode is JsonObject poseObject)
        {
            foreach (var (key, node) in poseObject)
            {
                if (!defaults.PoseDescriptions.ContainsKey(key))
                    bad.Add(L.T($"PoseDescriptions.{key}（存在しないポーズ）", $"PoseDescriptions.{key} (no such pose)"));
                else if (node is JsonValue pose && pose.TryGetValue<string>(out var description))
                { if (!string.IsNullOrWhiteSpace(description)) poses[key] = description; }
                else bad.Add($"PoseDescriptions.{key}");
            }
        }
        else if (poseNode != null) bad.Add("PoseDescriptions");

        return new ConversationRules
        {
            GreetingInstruction = Text("GreetingInstruction", defaults.GreetingInstruction),
            OutfitUnavailableLine = Text("OutfitUnavailableLine", defaults.OutfitUnavailableLine),
            DialogueStyle = Text("DialogueStyle", defaults.DialogueStyle),
            DialogueMaxLength = Number("DialogueMaxLength", defaults.DialogueMaxLength),
            OutfitVocabulary = Text("OutfitVocabulary", defaults.OutfitVocabulary),
            SexTimingRule = Text("SexTimingRule", defaults.SexTimingRule),
            ArousalRule = Text("ArousalRule", defaults.ArousalRule),
            PoseDescriptions = poses
        };
    }

    // Property names are matched without regard to case, like the other Mod JSON files.
    private static JsonNode Find(JsonObject root, string name)
    {
        foreach (var (key, node) in root)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return node;
        return null;
    }

    private static ConversationRules Defaults()
    {
        var root = (JsonObject)JsonNode.Parse(DefaultJson())!;
        var poses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, node) in (JsonObject)root["PoseDescriptions"]!) poses[key] = node!.GetValue<string>();
        return new ConversationRules
        {
            GreetingInstruction = root["GreetingInstruction"]!.GetValue<string>(),
            OutfitUnavailableLine = root["OutfitUnavailableLine"]!.GetValue<string>(),
            DialogueStyle = root["DialogueStyle"]!.GetValue<string>(),
            DialogueMaxLength = root["DialogueMaxLength"]!.GetValue<int>(),
            OutfitVocabulary = root["OutfitVocabulary"]!.GetValue<string>(),
            SexTimingRule = root["SexTimingRule"]!.GetValue<string>(),
            ArousalRule = root["ArousalRule"]!.GetValue<string>(),
            PoseDescriptions = poses
        };
    }

    private static string DefaultJson()
    {
        var name = L.En ? "conversation-rules-en.json" : "conversation-rules.json";
        using var stream = typeof(ConversationRules).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded default {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
