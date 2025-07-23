using HarmonyLib;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch]
public static class UndoExceptionsPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(ChooseCardToPutInHand), nameof(ChooseCardToPutInHand.Begin))]
	private static void ChooseCardToPutInHand_Begin_Postfix(ChooseCardToPutInHand __instance, Combat c) {
		if (__instance.selectedCard != null) ModEntry.Instance.Api.MarkCardAsOkayToBeGone(c, __instance.selectedCard.uuid);
	}
}