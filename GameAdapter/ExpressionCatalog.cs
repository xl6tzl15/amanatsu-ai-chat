using Amanatsu.AiChat.Config;

namespace Amanatsu.AiChat.GameAdapter;

// From the game's com/lis/exp/000_00.unity3d, c00..c11.
// All twelve personality tables use the same face indices for these presets.
internal static class ExpressionCatalog
{
    public static IReadOnlyDictionary<string, string> Descriptions => L.En ? EnglishDescriptions : JapaneseDescriptions;

    private static readonly IReadOnlyDictionary<string, string> EnglishDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = "neutral face", ["blushing"] = "neutral face with blushing cheeks",
            ["eyes_closed"] = "eyes closed", ["soft_smile"] = "gentle smile",
            ["smile"] = "bright smile", ["smile_blushing"] = "smile with blushing cheeks",
            ["happy_eyes_closed"] = "happy smile with eyes closed", ["curious"] = "puzzled, questioning look",
            ["worried"] = "worried look", ["surprised"] = "surprised look",
            ["embarrassed"] = "embarrassed look", ["troubled"] = "troubled look",
            ["wry_smile"] = "wry smile", ["exasperated"] = "exasperated look",
            ["angry"] = "angry look", ["reluctant"] = "reluctant, unwilling look",
            ["serious"] = "serious look", ["sad"] = "sad look",
            ["sleepy"] = "sleepy, half-closed eyes", ["awkward_smile"] = "awkward, troubled smile",
            ["wink_left"] = "wink with the left eye closed", ["wink_right"] = "wink with the right eye closed",
            ["displeased"] = "displeased look", ["sulky"] = "sulky pout",
            ["flustered"] = "flustered and bright red with embarrassment", ["dreamy"] = "dreamy, entranced look with flushed cheeks",
            ["melting"] = "face melting with pleasure during a sexual act; use only in the middle of the act",
            ["heart_eyes"] = "heart-eyed, completely smitten",
            ["default"] = "the default, expressionless face"
        };

    private static readonly IReadOnlyDictionary<string, string> JapaneseDescriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = "普通の表情", ["blushing"] = "頬を赤らめた普通の表情",
            ["eyes_closed"] = "目を閉じる", ["soft_smile"] = "穏やかな微笑み",
            ["smile"] = "明るい笑顔", ["smile_blushing"] = "頬を赤らめた笑顔",
            ["happy_eyes_closed"] = "目を閉じた笑顔", ["curious"] = "疑問を抱く表情",
            ["worried"] = "不安な表情", ["surprised"] = "驚いた表情",
            ["embarrassed"] = "照れた表情", ["troubled"] = "困った表情",
            ["wry_smile"] = "苦笑い", ["exasperated"] = "呆れた表情",
            ["angry"] = "怒った表情", ["reluctant"] = "嫌がる表情",
            ["serious"] = "真面目な表情", ["sad"] = "悲しい表情",
            ["sleepy"] = "眠そうな半目の表情", ["awkward_smile"] = "困り笑顔",
            ["wink_left"] = "左目を閉じたウインク", ["wink_right"] = "右目を閉じたウインク",
            ["displeased"] = "不満げな表情", ["sulky"] = "拗ねた表情",
            ["flustered"] = "顔を真っ赤にして恥じらう表情", ["dreamy"] = "頬を染めてうっとりした表情",
            ["melting"] = "性的な行為の最中に快感でとろけきった表情。行為の最中以外では使わない",
            ["heart_eyes"] = "目がハートになるほど夢中な表情",
            ["default"] = "何も表情を作っていないデフォルトの顔（無表情）"
        };

    public static void FillMissingDescriptions(IDictionary<string, ExpressionPreset> expressions)
    {
        foreach (var (key, preset) in expressions)
            if (preset != null && string.IsNullOrWhiteSpace(preset.Description)
                && Descriptions.TryGetValue(key, out var description))
                preset.Description = description;
    }

    public static Dictionary<string, ExpressionPreset> Defaults()
    {
        var expressions = new Dictionary<string, ExpressionPreset>(StringComparer.OrdinalIgnoreCase)
        {
        ["neutral"] = new(0, 0, 0, 0f),             // 標準
        ["blushing"] = new(0, 0, 0, .5f),           // 標準（頬赤）
        ["eyes_closed"] = new(0, 1, 0, 0f),         // 目閉じ
        ["soft_smile"] = new(1, 0, 19, 0f),         // 微笑
        ["smile"] = new(2, 3, 2, 0f),               // 笑顔
        ["smile_blushing"] = new(2, 3, 2, .5f),     // 笑顔（頬赤）
        ["happy_eyes_closed"] = new(2, 4, 2, 0f),  // 笑顔（目閉じ）
        ["curious"] = new(4, 0, 23, 0f),            // 疑問
        ["worried"] = new(5, 0, 8, 0f),             // 不安
        ["surprised"] = new(2, 0, 8, 0f),           // 驚き
        ["embarrassed"] = new(5, 2, 23, .5f),      // 照れ
        ["troubled"] = new(8, 6, 23, 0f),           // 困り
        ["wry_smile"] = new(8, 3, 19, 0f),          // 苦笑
        ["exasperated"] = new(0, 7, 8, 0f),        // 呆れ
        ["angry"] = new(3, 5, 6, 0f),               // 怒り
        ["reluctant"] = new(6, 7, 8, 0f),           // 嫌がり
        ["serious"] = new(3, 0, 19, 0f),            // 真面目
        ["sad"] = new(8, 2, 8, 0f),                 // 悲しい
        ["sleepy"] = new(0, 0, 0, 0f) { EyesOpen = .4f }, // 半目
        ["awkward_smile"] = new(8, 6, 19, 0f),      // 困り笑顔
        ["wink_left"] = new(9, 10, 2, 0f),         // ウインク（左閉じ）
        ["wink_right"] = new(10, 11, 2, 0f),       // ウインク（右閉じ）
        ["displeased"] = new(6, 0, 8, 0f),          // 不満
        ["sulky"] = new(6, 2, 23, 0f),              // 拗ねる
        ["flustered"] = new(5, 2, 23, .6f),        // 赤面の照れ
        ["dreamy"] = new(8, 0, 26, .6f) { EyesOpen = .6f },   // うっとり
        ["melting"] = new(8, 0, 27, .6f) { EyesOpen = .4f },  // とろけ
        ["heart_eyes"] = new(8, 21, 27, .6f),      // ハート目
        ["default"] = new(0, 0, 0, 0f),            // デフォルトの顔（無表情）
        };
        FillMissingDescriptions(expressions);
        return expressions;
    }

    public static bool IsLegacyDefault(IReadOnlyDictionary<string, ExpressionPreset> expressions) =>
        expressions.Count == 6 && expressions.TryGetValue("smile", out var smile)
        && smile.Eyebrow == 0 && smile.Eyes == 3 && smile.Mouth == 1;
}
