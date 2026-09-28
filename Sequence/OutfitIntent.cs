#nullable enable
namespace Amanatsu.AiChat.Sequence;

// Outfit action vocabulary shared by the AI protocol and the game adapter. The player's
// wording is interpreted by the AI only; this class just validates the structured values.
internal static class OutfitIntent
{
    public static readonly string[] PartKeys =
        { "top", "bottom", "bra", "shorts", "gloves", "pantyhose", "socks", "shoes", "arm_add", "leg_add", "other_add" };

    public static bool IsValidAction(string value)
    {
        if (value is "undress" or "half_undress" or "dress") return true;
        if (value.StartsWith("preset:", StringComparison.Ordinal))
            return OutfitPresets.Keys.Contains(value[7..], StringComparer.Ordinal);
        var parts = value.Split(':');
        return parts.Length == 2 && PartKeys.Contains(parts[0])
            && parts[1] is ("undress" or "half_undress" or "dress");
    }
}
