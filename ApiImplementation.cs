using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Nickel;
using TheJazMaster.CombatQoL.Patches;
using static TheJazMaster.CombatQoL.ICombatQolApi;
using static TheJazMaster.CombatQoL.SaveStates;

namespace TheJazMaster.CombatQoL;

public class ApiImplementation : ICombatQolApi {
	private static IModData ModData => ModEntry.Instance.Helper.ModData;

    public bool IsSimulating() => SimulationPatches.IsSimulating();

    public bool IsDrawingFuture() => FutureRenderingPatches.isRenderingFuture;

    public void MarkSafeRngAdvance(Combat c, RngTypes type, int count = 1) {
		ModData.SetModData(c, SafeRngAdvanceKey + type, ModData.GetModDataOrDefault(c, SafeRngAdvanceKey + type, 0) + count);
	}

	public void ClearKnownCards(Combat c, CardDestination destination = CardDestination.Deck) {
		SetLastKnownCards(c, [], destination);
	}

	public void InvalidateUndos(Combat c, InvalidationReason reason) {
		ModData.SetModData(c, UndoInvalidatedCustomKey, ModData.GetModDataOrDefault(c, UndoInvalidatedCustomKey, InvalidationReason.NONE) | reason);
	}

	public void MarkCardAsOkayToBeGone(Combat c, Card card) {
		MarkCardAsOkayToBeGone(c, card.uuid);
	}

	public void MarkCardAsOkayToBeGone(Combat c, int uuid) {
		if (!ModData.ContainsModData(c, CardsOkayToBeGoneKey)) {
			ModData.SetModData<List<int>>(c, CardsOkayToBeGoneKey, [uuid]);
			return;
		}
		var list = ModData.GetModData<List<int>>(c, CardsOkayToBeGoneKey);
		list.Add(uuid);
		ModData.SetModData(c, CardsOkayToBeGoneKey, list);
	}
}