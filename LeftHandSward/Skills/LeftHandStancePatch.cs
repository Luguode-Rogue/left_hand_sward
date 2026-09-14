using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace LeftHandSward.Skills
{
    [HarmonyPatch(typeof(Agent), nameof(Agent.GetIsLeftStance))]
    internal static class LeftHandStancePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Agent __instance,
            ref bool __result)
        {
            if (LeftHandAttackRuntime.ShouldForceLeftStance(__instance))
                __result = true;
        }
    }
}
