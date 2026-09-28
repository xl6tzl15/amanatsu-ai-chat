using System.Text.Json;
using Amanatsu.AiChat.GameAdapter;

namespace Amanatsu.AiChat.Config;

// This Mod-owned file is shared by every character. It never writes card PNGs
// or the per-card personality/configuration sidecars.
internal static class GlobalExpressionPresets
{
    public static string PathOnDisk => Path.GetFullPath(L.En
        ? "BepInEx/config/amanatsu.ai-chat/expression-presets-en.json"
        : "BepInEx/config/amanatsu.ai-chat/expression-presets.json");

    public static Dictionary<string, ExpressionPreset> LoadOrCreate(
        string path, IReadOnlyDictionary<string, ExpressionPreset> seed)
    {
        if (!File.Exists(path))
        {
            var created = new Dictionary<string, ExpressionPreset>(seed, StringComparer.OrdinalIgnoreCase);
            ExpressionCatalog.FillMissingDescriptions(created);
            foreach (var (key, preset) in created)
                if (string.IsNullOrWhiteSpace(preset.Description)) preset.Description = key;
            Save(path, created);
            return created;
        }
        var data = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path), JsonOptions());
        if (data?.Version != 1 || data.Expressions == null || data.Expressions.Count == 0)
            throw new InvalidDataException(L.T("グローバル表情プリセットの形式が正しくありません", "The shared expression preset file is not valid"));
        var result = new Dictionary<string, ExpressionPreset>(data.Expressions, StringComparer.OrdinalIgnoreCase);
        ExpressionCatalog.FillMissingDescriptions(result);
        foreach (var (key, preset) in result)
            if (!ExpressionPresetRules.Validate(key, preset, 11, 25, 29, out var error))
                throw new InvalidDataException(L.T($"表情プリセット {key}: {error}", $"Expression preset {key}: {error}"));
        return result;
    }

    public static void Save(string path, IReadOnlyDictionary<string, ExpressionPreset> expressions)
    {
        if (expressions == null || expressions.Count == 0)
            throw new InvalidDataException(L.T("表情プリセットを空にはできません", "The expression preset list cannot be empty"));
        foreach (var (key, preset) in expressions)
            if (!ExpressionPresetRules.Validate(key, preset, 11, 25, 29, out var error))
                throw new InvalidDataException(L.T($"表情プリセット {key}: {error}", $"Expression preset {key}: {error}"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new FileModel
        {
            Version = 1,
            Expressions = new Dictionary<string, ExpressionPreset>(expressions, StringComparer.OrdinalIgnoreCase)
        }, JsonOptions()));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporaryPath, path, true);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class FileModel
    {
        public int Version { get; set; }
        public Dictionary<string, ExpressionPreset> Expressions { get; set; } = new();
    }
}
