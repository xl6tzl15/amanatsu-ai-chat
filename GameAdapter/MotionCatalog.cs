#nullable enable
namespace Amanatsu.AiChat.GameAdapter;

// Logical IDs belong to this adapter, not the main game's Heroine state IDs.
// Labels and states were read from anim_f_000[000_00] and f_00 respectively.
internal static class MotionCatalog
{
    public static IReadOnlyDictionary<string, string> Descriptions => L.En ? EnglishDescriptions : JapaneseDescriptions;

    private static readonly IReadOnlyDictionary<string, string> JapaneseDescriptions = new Dictionary<string, string>
    {
        ["idle"] = "待機", ["stretch"] = "伸び", ["look_up"] = "遠くの景色を見る",
        ["fan_self"] = "手で仰ぐ", ["bend_stretch"] = "屈伸",
        ["leg_stretch"] = "脚のストレッチ", ["arm_stretch"] = "腕のストレッチ",
        ["side_stretch"] = "脇を伸ばす", ["twist_stretch"] = "腰をひねる",
        ["look_down"] = "下を眺める"
    };

    private static readonly IReadOnlyDictionary<string, string> EnglishDescriptions = new Dictionary<string, string>
    {
        ["idle"] = "stand idle", ["stretch"] = "stretch up", ["look_up"] = "gaze at the distant view",
        ["fan_self"] = "fan herself with a hand", ["bend_stretch"] = "knee bends",
        ["leg_stretch"] = "leg stretch", ["arm_stretch"] = "arm stretch",
        ["side_stretch"] = "side stretch", ["twist_stretch"] = "waist twist",
        ["look_down"] = "look down at something"
    };

    // Standing idle poses (Pose_D_XX). The AI switches between them like a persistent motion.
    public readonly record struct Pose(string Key, int Index, string Japanese, string English)
    {
        public string Description => L.T(Japanese, English);
    }

    public static readonly Pose[] Poses =
    {
        new("pose_wave", 0, "片手を軽く上げて気さくに立つ", "stands casually with one hand lightly raised"),
        new("pose_hand_chest", 1, "片手を胸元に添えて控えめに立つ", "stands modestly with a hand on her chest"),
        new("pose_hands_hips", 2, "両手を腰に当てて堂々と立つ", "stands proudly with both hands on her hips"),
        new("pose_hand_hip", 3, "片手を腰に当てて気楽に立つ", "stands at ease with one hand on her hip"),
        new("pose_arms_front", 4, "両手を体の前で重ねてもじもじ立つ", "fidgets with both hands clasped in front"),
        new("pose_open_arms", 5, "両腕を少し広げて明るく立つ", "stands cheerfully with arms slightly open"),
        new("pose_relaxed", 6, "片手を腰に添え、もう片手を下ろして自然体", "relaxed, one hand on her hip and the other down"),
        new("pose_arms_crossed", 7, "腕を組んで立つ", "stands with her arms crossed"),
        new("pose_shy", 8, "片腕を抱えて恥ずかしそうに立つ", "holds one arm and stands shyly"),
        new("pose_hands_belly", 9, "両手をお腹の前で組んで上品に立つ", "stands gracefully with hands folded at her waist"),
        new("pose_finger_lips", 10, "指を口元に当てて考え込む・甘える", "finger to her lips, thinking or being coy"),
        new("pose_neutral", 11, "両手を下ろしてまっすぐ立つ", "stands straight with her arms down"),
    };

    public static int? PoseIndex(string key)
    {
        foreach (var p in Poses) if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) return p.Index;
        return null;
    }

    public static void FillMissingDescriptions(IDictionary<string, string> descriptions,
        IEnumerable<string> motionKeys)
    {
        foreach (var key in motionKeys)
            if ((!descriptions.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                && Descriptions.TryGetValue(key, out var description))
                descriptions[key] = description;
    }

    public static readonly IReadOnlyDictionary<int, string> States = new Dictionary<int, string>
    {
        [0] = "Base Layer.Idle.chara.Pose_D_00_Loop",
        [100] = "Base Layer.tachi.al_f_act_00_17", // 伸び
        [101] = "Base Layer.tachi.al_f_act_00_26", // 滝を眺める（遠方へ視線）
        [102] = "Base Layer.tachi.al_f_act_00_00", // 立ち振り向く：左
        [103] = "Base Layer.tachi.al_f_act_00_01", // 立ち振り向く：右
        [104] = "Base Layer.tachi.al_f_act_00_08.al_f_act_00_08", // 仰ぐ
        [105] = "Base Layer.tachi.al_f_act_00_12", // ストレッチ：屈伸
        [106] = "Base Layer.tachi.al_f_act_00_13", // ストレッチ：伸脚
        [107] = "Base Layer.tachi.al_f_act_00_14", // ストレッチ：腕クロス
        [108] = "Base Layer.tachi.al_f_act_00_15", // ストレッチ：脇
        [109] = "Base Layer.tachi.al_f_act_00_16", // ストレッチ：腰ひねり
        [110] = "Base Layer.tachi.al_f_act_00_24.al_f_act_00_24", // 眺める：植物観察にも使用
    };

    public static string? StateFor(int id, int personality)
    {
        if (id == 0 && personality is >= 0 and <= 11)
            return $"Base Layer.Idle.chara.Pose_D_{personality:00}_Loop";
        return States.TryGetValue(id, out var state) ? state : null;
    }

    // 102/103 turn a character that starts with her back to the player; on the face-to-face
    // stage they spin her 180 degrees away and she stays that way, so they are not offered.
    public static readonly IReadOnlyDictionary<string, int> Retired = new Dictionary<string, int>
    {
        ["turn_left"] = 102, ["turn_right"] = 103
    };

    public static Dictionary<string, int> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = 0, ["stretch"] = 100, ["look_up"] = 101,
        ["fan_self"] = 104, ["bend_stretch"] = 105, ["leg_stretch"] = 106,
        ["arm_stretch"] = 107, ["side_stretch"] = 108, ["twist_stretch"] = 109,
        ["look_down"] = 110
    };
}
