using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nickel;
using TheJazMaster.CombatQoL.Artifacts;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch(typeof(State))]
public static class StatePatches
{
    private static IModData ModData => ModEntry.Instance.Helper.ModData;
    internal static Type displayClass = null!;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(State.Save))]
    public static void State_Save_Prefix(State __instance) {
        if (__instance.route is Combat c)
            ModData.SetModData(c, SaveStates.SavesKey, SaveStates.saves);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(State.Load))]
    public static void State_Load_Postfix(int slot, State.SaveSlot __result) {
        if (__result.state == null || __result.state.route is not Combat c) return;
        if (ModData.ContainsModData(c, SaveStates.SavesKey) && ModData.GetModData<object>(c, SaveStates.SavesKey) is not List<State>) {
            SimulationPatches.ClearPrognostication();
            return;
        }
        if (ModData.TryGetModData(c, SaveStates.SavesKey, out List<State>? saves) && saves != null) {
            SaveStates.saves = saves;
        }
        SimulationPatches.ClearPrognostication();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(State.SendCardToDeck))]
    public static void State_SendCardToDeck_Postfix(State __instance, Card card, bool doAnimation = false, bool insertRandomly = false) {
        if (__instance.route is Combat c && insertRandomly) {
            ModEntry.Instance.Api.MarkSafeRngAdvance(c, ICombatQolApi.RngTypes.SHUFFLE);
            ModEntry.Instance.Api.ClearKnownCards(c, CardDestination.Deck);
        }
    }

    // Patched in ModEntry
    public static void State_PopulateRun_Delegate_Postfix(object __instance) {
        FieldInfo stateField = displayClass.GetFields().Where((FieldInfo f) => f.FieldType == typeof(State)).First();
		State state = (State) stateField!.GetValue(__instance)!;
        state.SendArtifactToChar(new TurnCounter());
    }
}

