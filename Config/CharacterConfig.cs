using System.Text.Json;

namespace Amanatsu.AiChat.Config;

public sealed class CharacterConfig
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Character";
    public string SystemPrompt { get; set; } = L.T("あなたは画面内のキャラクターです。日本語で自然かつ簡潔に会話してください。", "You are the character on the screen. Talk naturally and briefly in English.");
    public Dictionary<string, ExpressionPreset> Expressions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Motions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> MotionDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Idle standing pose (Pose_D_XX); -1 keeps the card personality's own pose.
    public int IdlePose { get; set; } = -1;

    public static CharacterConfig LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            return Normalize(JsonSerializer.Deserialize<CharacterConfig>(File.ReadAllText(path), JsonOptions())
                   ?? throw new InvalidDataException($"Invalid character config: {path}"));

        var config = CreateDefault();
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions()));
        return config;
    }

    public static CharacterConfig LoadOrCreateForCard(string basePath, CharacterConfig defaults, string cardPath, string name)
    {
        var directory = Path.Combine(Path.GetDirectoryName(basePath)!, L.En ? "characters-en" : "characters");
        Directory.CreateDirectory(directory);
        var id = Path.GetFileNameWithoutExtension(cardPath);
        var path = Path.Combine(directory, id + ".json");
        if (File.Exists(path))
        {
            var loaded = Normalize(JsonSerializer.Deserialize<CharacterConfig>(File.ReadAllText(path), JsonOptions())
                ?? throw new InvalidDataException($"Invalid character config: {path}"));
            // Card files keep their own overrides, but motions added to the Mod later must still reach them.
            foreach (var (key, motionId) in defaults.Motions) loaded.Motions.TryAdd(key, motionId);
            return loaded;
        }
        var config = new CharacterConfig
        {
            Id = id,
            Name = name,
            SystemPrompt = L.T($"あなたは{name}です。", $"You are {name}. ") + defaults.SystemPrompt,
            Expressions = new(defaults.Expressions, StringComparer.OrdinalIgnoreCase),
            Motions = new(defaults.Motions, StringComparer.OrdinalIgnoreCase),
            MotionDescriptions = new(defaults.MotionDescriptions, StringComparer.OrdinalIgnoreCase)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions()));
        return config;
    }

    public static void SaveForCard(string basePath, string cardPath, CharacterConfig config)
    {
        var directory = Path.Combine(Path.GetDirectoryName(basePath)!, L.En ? "characters-en" : "characters");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileNameWithoutExtension(cardPath) + ".json");
        Save(path, config);
    }

    public static void Save(string path, CharacterConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, JsonOptions()));
        File.Move(temporaryPath, path, true);
    }

    // System.Text.Json replaces initialized dictionaries with case-sensitive ones.
    private static CharacterConfig Normalize(CharacterConfig config)
    {
        config.Expressions = new(config.Expressions ?? new(), StringComparer.OrdinalIgnoreCase);
        config.Motions = new(config.Motions ?? new(), StringComparer.OrdinalIgnoreCase);
        config.MotionDescriptions = new(config.MotionDescriptions ?? new(), StringComparer.OrdinalIgnoreCase);
        return config;
    }

    private static CharacterConfig CreateDefault() => new()
    {
        Expressions = GameAdapter.ExpressionCatalog.Defaults(),
        Motions = GameAdapter.MotionCatalog.Defaults(),
        MotionDescriptions = new(GameAdapter.MotionCatalog.Descriptions, StringComparer.OrdinalIgnoreCase)
    };

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

public sealed class ExpressionPreset
{
    public string Description { get; set; } = "";
    public int Eyebrow { get; set; }
    public int Eyes { get; set; }
    public int Mouth { get; set; }
    public float Blush { get; set; }
    // Fixed openness 0 (closed) .. 1 (open); null leaves eyes to blinking and mouth to the animation.
    public float? EyesOpen { get; set; }
    public float? MouthOpen { get; set; }

    public ExpressionPreset() { }

    public ExpressionPreset(int eyebrow, int eyes, int mouth, float blush, string description = "")
    {
        Eyebrow = eyebrow;
        Eyes = eyes;
        Mouth = mouth;
        Blush = blush;
        Description = description;
    }
}
