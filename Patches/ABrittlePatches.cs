using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nickel;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch(typeof(ABrittle))]
public static class ABrittlePatches
{
	private static IModData ModData => ModEntry.Instance.Helper.ModData;


    [HarmonyPrefix]
    [HarmonyPatch(nameof(ABrittle.Begin))]
    public static void ABrittle_Begin_Prefix(G g, State s, Combat c, ABrittle __instance, ref List<Part> __state) {
        __state = __instance.targetPlayer ? Mutil.DeepCopy(s.ship.parts) : Mutil.DeepCopy(c.otherShip.parts);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ABrittle.Begin))]
    public static void ABrittle_Begin_Postfix(G g, State s, Combat c, ABrittle __instance, List<Part> __state) {
        if (__instance.makeTheBrittlenessInvisible) {
			CombatPatches.ResetBrittlenessKnowledge(s, c, __instance.targetPlayer ? s.ship : c.otherShip, __state);
		}
    }
}