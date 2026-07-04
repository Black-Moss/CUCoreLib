using HarmonyLib;
using UnityEngine;

namespace CUCoreLib.Patches
{
    [HarmonyPatch(typeof(GlobalDark), "Awake")]
    internal static class GlobalDarkPatches
    {
        [HarmonyPostfix]
        private static void UpdateBetaBuildText(GlobalDark __instance)
        {
            if (__instance?.betaBuild == null || string.IsNullOrEmpty(__instance.betaBuild.text))
                return;

            __instance.betaBuild.text = __instance.betaBuild.text.Replace(
                "This is a beta build",
                "This is a modded beta build");

            __instance.betaBuild.color = new Color(1f, 0.45f, 0.2f, __instance.betaBuild.color.a);
        }
    }
}
