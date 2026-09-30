using HarmonyLib;

namespace Amanatsu.AiChat.GameAdapter;

// A character without save data starts with default parameters, which unlock only the first
// postures. During an H scene started from the chat every posture is available; the main
// game's own H scenes are not affected.
[HarmonyPatch(typeof(AL.H.List.AnimationListInfo), nameof(AL.H.List.AnimationListInfo.CheckReleasePhase))]
internal static class HPostureUnlock
{
    private static bool Prefix(ref bool __result)
    {
        if (!HTransition.UnlockPostures) return true;
        __result = true;
        return false;
    }
}
