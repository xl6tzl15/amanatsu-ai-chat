using System.Text.Json.Nodes;
using Amanatsu.AiChat.Config;

// Exercises Config/ConversationRules.cs without the game: creation from the embedded
// defaults, edits, one malformed entry, an unparsable file, and blank entries.
internal static class ConversationRulesChecks
{
    public static void Run()
    {
        var warnings = new List<string>();
        ConversationRules.Log = (message, warning) => { if (warning) warnings.Add(message); };
        var path = Path.Combine(Path.GetTempPath(), $"amanatsu-rules-{Guid.NewGuid():N}.json");
        ConversationRules.PathOverride = path;
        try
        {
            var defaults = ConversationRules.Current;
            if (!File.Exists(path)) throw new Exception("Rules file was not created");
            if (defaults.DialogueMaxLength != 300 || defaults.PoseDescriptions.Count != 12
                || string.IsNullOrWhiteSpace(defaults.GreetingInstruction))
                throw new Exception("Embedded defaults are incomplete");

            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            root["GreetingInstruction"] = "edited greeting";
            root["DialogueMaxLength"] = "600";                 // malformed: a string
            root["ArousalRule"] = "";                          // blank: back to the default
            root["PoseDescriptions"]!["pose_wave"] = "waves";
            root["PoseDescriptions"]!["pose_waves"] = "typo";   // not one of the 12 poses
            Write(path, root.ToJsonString());
            var edited = ConversationRules.Current;
            if (edited.GreetingInstruction != "edited greeting") throw new Exception("Edit was not applied");
            if (edited.DialogueMaxLength != 300) throw new Exception("Malformed entry did not fall back");
            if (edited.ArousalRule != defaults.ArousalRule) throw new Exception("Blank entry did not fall back");
            if (edited.PoseDescriptions["POSE_WAVE"] != "waves") throw new Exception("Pose edit was not applied");
            if (!warnings.Any(w => w.Contains("DialogueMaxLength"))) throw new Exception("Malformed entry was not reported");
            if (!warnings.Any(w => w.Contains("pose_waves"))) throw new Exception("Unknown pose was not reported");
            if (edited.PoseDescriptions.ContainsKey("pose_waves")) throw new Exception("Unknown pose was added");

            warnings.Clear();
            Write(path, "{ not json");
            var kept = ConversationRules.Current;
            if (kept.GreetingInstruction != "edited greeting") throw new Exception("Broken file did not keep the last rules");
            if (warnings.Count != 1) throw new Exception("Broken file was not reported once");
            _ = ConversationRules.Current;
            if (warnings.Count != 1) throw new Exception("Broken file was reported again without a change");

            Console.WriteLine("Conversation rules: defaults, edits, malformed and broken files passed.");
        }
        finally
        {
            ConversationRules.PathOverride = null;
            File.Delete(path);
        }
    }

    // Each write must look like a new edit even within the file system's timestamp resolution.
    private static void Write(string path, string text)
    {
        var before = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, text);
        if (File.GetLastWriteTimeUtc(path) <= before) File.SetLastWriteTimeUtc(path, before.AddSeconds(1));
    }
}
