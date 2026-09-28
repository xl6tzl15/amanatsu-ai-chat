using System.Text.Json;

namespace Amanatsu.AiChat.Config;

// Portable personality-only preset. Character cards and motion/expression mappings are never written here.
internal static class PersonalityFile
{
    // Each language keeps its own presets so the file dialog never mixes the two.
    public static string Directory => Path.GetFullPath(L.En
        ? "BepInEx/config/amanatsu.ai-chat/personalities-en"
        : "BepInEx/config/amanatsu.ai-chat/personalities");

    public static string Load(string path)
    {
        RequireJsonPath(path);
        var preset = JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), JsonOptions());
        if (preset?.Version != 1 || string.IsNullOrWhiteSpace(preset.PersonalityPrompt)
            || preset.PersonalityPrompt.Length > 4000)
            throw new InvalidDataException(L.T("性格ファイルの形式または文字数が正しくありません", "The personality file format or length is not valid"));
        return preset.PersonalityPrompt;
    }

    public static void Save(string path, string prompt)
    {
        RequireJsonPath(path);
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 4000)
            throw new InvalidDataException(L.T("性格・口調の指示は1～4000文字で入力してください", "The personality text must be 1 to 4000 characters"));
        // Never overwrite a character profile or another unrelated JSON file.
        if (File.Exists(path)) Load(path);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Preset
        {
            Version = 1,
            PersonalityPrompt = prompt.Trim()
        }, JsonOptions()));
        File.Move(temporaryPath, path, true);
    }

    private static void RequireJsonPath(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("性格ファイルは .json を指定してください", "Choose a .json personality file"));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class Preset
    {
        public int Version { get; set; }
        public string PersonalityPrompt { get; set; }
    }
}
