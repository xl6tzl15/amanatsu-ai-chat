using System.Text.RegularExpressions;

namespace Amanatsu.AiChat.Config;

internal static class ExpressionPresetRules
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);

    public static bool Validate(string key, ExpressionPreset preset,
        int eyebrowCount, int eyesCount, int mouthCount, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(key) || !KeyPattern.IsMatch(key))
            error = L.T("IDは小文字英字で始まる英数字・_の1～40文字にしてください", "The ID must be 1-40 lowercase letters, digits or _, starting with a letter");
        else if (preset == null || string.IsNullOrWhiteSpace(preset.Description)
            || preset.Description.Length > 160 || preset.Description.Contains('\n') || preset.Description.Contains('\r'))
            error = L.T("説明は改行なしの1～160文字で入力してください", "The description must be 1-160 characters on one line");
        else if (preset.Eyebrow < 0 || preset.Eyebrow >= eyebrowCount
            || preset.Eyes < 0 || preset.Eyes >= eyesCount
            || preset.Mouth < 0 || preset.Mouth >= mouthCount)
            error = L.T("眉・目・口の番号が現在のキャラの範囲外です", "A brow, eye or mouth number is out of range for this character");
        else if (float.IsNaN(preset.Blush) || float.IsInfinity(preset.Blush)
            || preset.Blush < 0f || preset.Blush > 1f)
            error = L.T("赤面の強さは0～1で入力してください", "Blush must be between 0 and 1");
        else if (!ValidOptional(preset.EyesOpen) || !ValidOptional(preset.MouthOpen))
            error = L.T("開閉度は自動または0～1にしてください", "Openness must be auto or between 0 and 1");
        return error.Length == 0;
    }

    private static bool ValidOptional(float? value) => !value.HasValue
        || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
            && value >= 0f && value <= 1f);
}
