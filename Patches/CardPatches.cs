using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Nickel;
using static TheJazMaster.CombatQoL.ICombatQolApi;
using static TheJazMaster.CombatQoL.SaveStates;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch(typeof(Card))]
public static class CardPatches
{
	public enum PrognosticationWarningType {
		INVALID_BEFORE,
		INVALID_AFTER,
	}

	private static IModData ModData => ModEntry.Instance.Helper.ModData;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Card.GetAllTooltips))]
    public static void Card_GetAllTooltips_Postfix(Card __instance, ref IEnumerable<Tooltip> __result, G g, State s, bool showCardTraits = true) {
		bool validityAfter = SimulationPatches.TryGetPrognosticationValidityAfter(__instance.UIKey(), out var resultAfter);
		bool shouldSalvage = SimulationPatches.ShouldDrawSalvagedPreview(resultAfter);

		if (shouldSalvage) {
			__result = new List<Tooltip> { new TTText(SalvagedFutureText()) }.Concat(__result);
		}
		if (SimulationPatches.TryGetPrognosticationValidity(__instance.UIKey(), out var result) && result != InvalidationReason.NONE) {
			__result = new List<Tooltip> { new TTText(UndoPrognosticationText(result, PrognosticationWarningType.INVALID_BEFORE)) }.Concat(__result);
		}
		else if (!shouldSalvage && validityAfter && (resultAfter & ~InvalidationReasonObvious) != InvalidationReason.NONE) {
			__result = new List<Tooltip> { new TTText(UndoPrognosticationText(resultAfter, PrognosticationWarningType.INVALID_AFTER)) }.Concat(__result);
		}
    }

	public static string UndoPrognosticationText(InvalidationReason mask, PrognosticationWarningType warningType)
	{
		StringBuilder sb = new();
		// bool first = true;
		// if (mask.HasFlag(InvalidationReason.PILE_CONTENTS)) mask &= ~InvalidationReason.UNKNOWN_ORDER;
		foreach (InvalidationReason reason in Enum.GetValues(typeof(InvalidationReason))) {
			if (reason != InvalidationReason.NONE && mask.HasFlag(reason)) {
				// if (first) first = false;
				// else sb.Append(ModEntry.Instance.Localizations.Localize(["glossary", "undo", "prognostication", "delimiter"]));
				sb.Append(ModEntry.Instance.Localizations.Localize(["glossary", "undo", "prognostication", reason.Key()]));
				break;
			}
		}
		return ModEntry.Instance.Localizations.Localize(["glossary", "undo", "prognostication", warningType switch {
			PrognosticationWarningType.INVALID_BEFORE => "start",
			PrognosticationWarningType.INVALID_AFTER => "startBadSimulation",
			// PrognosticationWarningType.SALVAGED => "salvaged",
			_ => "",
		}], new { Reasons = sb.ToString() });
	}

	public static string SalvagedFutureText()
	{
		return ModEntry.Instance.Localizations.Localize(["glossary", "undo", "prognostication", "salvaged"]);
	}
}
