using System.Text.Json.Serialization;

namespace Amanatsu.AiChat.Sequence;

public sealed class SequenceEnvelope
{
    [JsonPropertyName("sequence")]
    public List<SequenceCommand> Commands { get; set; } = new();

    [JsonPropertyName("thinking")]
    public string Thinking { get; set; }
}

public sealed class SequenceCommand
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; }

    [JsonPropertyName("duration")]
    public float? Duration { get; set; }
}

public static class SequenceValidator
{
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
        { "expression", "motion", "text", "wait", "eyebrow", "eyes", "mouth", "outfit", "pose" };
    // Conservative ranges checked against the shipped face tables and live controllers.
    // CharacterAdapter checks the active card's actual limits again before applying.
    public static readonly IReadOnlyDictionary<string, int> FacePartMaxExclusive = new Dictionary<string, int>
        { ["eyebrow"] = 11, ["eyes"] = 25, ["mouth"] = 29 };

    public static IReadOnlyList<SequenceCommand> Validate(
        SequenceEnvelope envelope,
        IReadOnlySet<string> expressions,
        IReadOnlySet<string> motions,
        Action<string> warn)
    {
        var valid = new List<SequenceCommand>();
        if (envelope?.Commands == null)
            return valid;

        foreach (var command in envelope.Commands.Take(40))
        {
            if (command == null)
            {
                warn("Null command ignored.");
                continue;
            }
            var type = command.Type?.Trim().ToLowerInvariant() ?? "";
            if (!Types.Contains(type))
            {
                warn($"Unknown command ignored: {command.Type}");
                continue;
            }

            if (type == "wait")
            {
                if (command.Duration is not { } seconds || float.IsNaN(seconds) || seconds < 0 || seconds > 15)
                {
                    warn("Invalid wait ignored (allowed: 0..15 seconds).");
                    continue;
                }
            }
            else if (string.IsNullOrWhiteSpace(command.Value))
            {
                warn($"Empty {type} ignored.");
                continue;
            }
            else if (type == "expression" && !expressions.Contains(command.Value))
            {
                warn($"Unknown expression ignored: {command.Value}");
                continue;
            }
            else if (type == "motion" && !motions.Contains(command.Value))
            {
                warn($"Unknown motion ignored: {command.Value}");
                continue;
            }
            else if (type == "pose" && GameAdapter.MotionCatalog.PoseIndex(command.Value) == null)
            {
                warn($"Unknown pose ignored: {command.Value}");
                continue;
            }
            else if (type == "outfit" && !OutfitIntent.IsValidAction(command.Value))
            {
                warn($"Unknown outfit action ignored: {command.Value}");
                continue;
            }
            else if (FacePartMaxExclusive.TryGetValue(type, out var maximum))
            {
                if (!int.TryParse(command.Value, out var index) || index < 0 || index >= maximum)
                {
                    warn($"Invalid {type} index ignored: {command.Value} (allowed: 0..{maximum - 1}).");
                    continue;
                }
                command.Value = index.ToString();
            }
            else if (type == "text" && command.Value!.Length > 1000)
            {
                command.Value = command.Value[..1000];
            }

            command.Type = type;
            valid.Add(command);
        }
        return valid;
    }
}
