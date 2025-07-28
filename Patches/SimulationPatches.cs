using System;
using System.Collections.Generic;
using System.Linq;
using FMOD;
using FMOD.Studio;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nickel;
using static TheJazMaster.CombatQoL.ICombatQolApi;
using static TheJazMaster.CombatQoL.SaveStates;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch]
public static class SimulationPatches
{
	private static IModData ModData => ModEntry.Instance.Helper.ModData;
	internal static readonly string PreventShuffleKey = "PreventShuffle";
    private static bool isSimulating = false;
    public static bool disableDraw = false;

	public static bool IsSimulating() => isSimulating;

    private static readonly Dictionary<UIKey, (State state, InvalidationReason reason, InvalidationReason reasonAfter)> prognosticationCache = [];
	private static readonly Dictionary<UIKey, bool> prognosticationMattersCache = [];

	public static void ClearPrognostication() {
		prognosticationCache.Clear();
		prognosticationMattersCache.Clear();
		FutureRenderingPatches.RenderFutureThisFrame = null;
	}
	public static bool HasPrognostication(UIKey key) {
		return prognosticationCache.ContainsKey(key);
	}

	public static bool TryGetPrognostication(UIKey key, out (State state, InvalidationReason reason, InvalidationReason reasonAfter) res) {
		return prognosticationCache.TryGetValue(key, out res);
	}

	public static bool TryGetPrognosticationState(UIKey key, out State state) {
		bool has = prognosticationCache.TryGetValue(key, out var value);
		state = value.state;
		return has;
	}

	public static bool TryGetPrognosticationValidity(UIKey key, out InvalidationReason reason) {
		bool has = prognosticationCache.TryGetValue(key, out var value);
		reason = value.reason;
		return has;
	}

	public static bool TryGetPrognosticationValidityAfter(UIKey key, out InvalidationReason reasonAfter) {
		bool has = prognosticationCache.TryGetValue(key, out var value);
		reasonAfter = value.reasonAfter;
		return has;
	}

	// public static bool TryGetPrognosticationImperfection(UIKey key, out bool imperfect) {
	// 	bool has = prognosticationCache.TryGetValue(key, out var value);
	// 	imperfect = value.isImperfect;
	// 	return has;
	// }

	public static bool TryGetPrognosticationActionPresence(UIKey key, out bool anyActions) {
		return prognosticationMattersCache.TryGetValue(key, out anyActions);
	}

	private static bool CanPreviewBeSalvaged(InvalidationReason reason) {
		if ((reason & ~InvalidationReasonObvious) == InvalidationReason.NONE) return false;
		// RNG can't be "disabled" for a preview, the rest can
		return !reason.HasFlag(InvalidationReason.RNG_SEED);
	}

	[HarmonyPatch(typeof(AEndTurn), nameof(AEndTurn.Begin)), HarmonyPostfix]
	public static void MarkTurnEnd(G g, State s, Combat c) {
		ModData.SetModData(c, UndoInvalidatedDueToTurnEndKey, true);
	} 

	private static bool stopSimulatingCombat = false;
	[HarmonyPatch(typeof(AEnemyTurnAfter), nameof(AEnemyTurnAfter.Begin)), HarmonyPrefix]
	public static bool AEnemyTurnAfter_Begin_Prefix(G g, State s, Combat c) {
		if (isSimulating) {
			c.cardActions.Clear();
			stopSimulatingCombat = true;
			return false;
		}
		return true;
	} 

	[HarmonyPatch(typeof(G), nameof(G.Render)), HarmonyPrefix]
	public static void G_Render_Prefix(G __instance) {
		FutureRenderingPatches.RenderFutureThisFrame = null;
		State s = __instance.state;
		if (s != null && s.route is Combat c && c.PlayerCanAct(s)) {
			if (ModEntry.Instance.Settings.ProfileBased.Current.Prognosticate && __instance.hoverKey.HasValue) {
				var key = __instance.hoverKey.Value;
				SimulateClick(__instance, s, c, key);
				if (!TryGetPrognosticationActionPresence(__instance.hoverKey.Value, out var anyActions) || !anyActions) {
					key = new UIKey(StableUK.combat_endTurn);
					if (!HasPrognostication(key)) SimulateClick(__instance, s, c, key);
				}
				DrawFuture(__instance, s, c, key);
			}
			else {
				var key = new UIKey(StableUK.combat_endTurn);
				if (!HasPrognostication(key)) SimulateClick(__instance, s, c, key);
				DrawFuture(__instance, s, c, key);
			}
		}
	}

	private static bool DrawFuture(G g, State oldState, Combat oldCombat, UIKey key) {
		bool futureInvalid = IsFutureDrawInvalid(key, out var reasonAfter);
		bool shouldSalvage = ShouldDrawSalvagedPreview(reasonAfter);
		if (FutureRenderingPatches.RenderFutureThisFrame != key && (!futureInvalid || shouldSalvage) && TryGetPrognosticationActionPresence(key, out bool anyActions) && anyActions && FutureRenderingPatches.ShouldDrawFuture(oldCombat) && TryGetPrognostication(key, out var res)) {
			(State newState, _, _) = res;
			if (anyActions && (shouldSalvage || (reasonAfter & ~InvalidationReasonObvious) == InvalidationReason.NONE) && newState.route is Combat newCombat) {
				FutureRenderingPatches.DrawPreview(g, newState, oldState, newCombat, oldCombat);
				FutureRenderingPatches.RenderFutureThisFrame = key;
				return true;
			}
		}
		return false;
	}

    public static bool IsFutureDrawInvalid(UIKey key, out InvalidationReason reasonAfter) {
        return TryGetPrognosticationValidityAfter(key, out reasonAfter) && (reasonAfter & ~InvalidationReasonObvious) != InvalidationReason.NONE;
    }

    public static bool ShouldDrawSalvagedPreview(InvalidationReason reason) {
        return FutureRenderingPatches.IsPreviewSalvageEnabled() && CanPreviewBeSalvaged(reason);
    }

	private static bool IsClickableBox(Box box) {
		return box.onMouseDown != null && (box.onMouseDown is Combat || box.onMouseDown is not Route);
	}

    private static void SimulateClick(G g, State oldState, Combat oldCombat, UIKey key, InvalidationReason oldResult = InvalidationReason.NONE, InvalidationReason oldAfterResult = InvalidationReason.NONE)
    {
		void Restore(PFXState pfx, UIKey? oldGamepadKey, double oldDt, ref bool isSimulating, State oldState) {
			Input.currentGpKey = oldGamepadKey;
			isSimulating = false;
			pfx.Restore();
			g.dt = oldDt;
			g.state = oldState;
		}

		if (oldCombat.routeOverride == null && !G.IsInCornerMenu(key)) {
			if (!TryGetPrognosticationActionPresence(key, out var anyActionsHere)) {
            	Box? box = g.boxes.FirstOrDefault(b => b.key.Equals(key));
                if (box != null && IsClickableBox(box)) {
                    State fakeState = Duplicate(oldState);
					Combat fakeCombat = (fakeState.route as Combat)!;
					if ((oldAfterResult | oldResult) != InvalidationReason.NONE) MakeStatePredictable(fakeState, fakeCombat, oldResult | oldAfterResult);
					g.state = fakeState;
					var oldGamepadKey = Input.currentGpKey;
					var pfx = PFXState.Create();

					disableDraw = true;
					List<Box> newBoxes = [];
					List<Box> oldBoxes = g.boxes;
					g.boxes = newBoxes;
					Draw.StartAutoBatchFrame();
					fakeCombat.Render(g);
					Draw.EndAutoBatchFrame();
					g.boxes = oldBoxes;
					Box? properBox = newBoxes?.FirstOrDefault(b => b.key.Equals(key));
					disableDraw = false;

					isSimulating = true;
					PFXState.BlankOut();
					var oldDt = g.dt;
					g.dt = 20;
					if (properBox != null && properBox.onMouseDown != null) {
						properBox.onMouseDown.OnMouseDown(g, properBox);
						// bool isPreviewImperfect = false;
						bool anyActions = KeepSimulatingCombat(g, fakeState, fakeCombat);
						var isUndoable = IsUndoable(fakeState, fakeCombat, oldState, out var _);
						InvalidationReason isUndoableAfter = isUndoable;
						
						if (FutureRenderingPatches.IsPreviewEnabled()) {
							if (!ModData.TryGetModData(fakeCombat, UndoInvalidatedDueToTurnEndKey, out bool isInvalid) || !isInvalid) {
								fakeCombat.Queue(new AEndTurn());
								KeepSimulatingCombat(g, fakeState, fakeCombat);
								isUndoableAfter = IsUndoable(fakeState, fakeCombat, oldState, out var _);
							}

							if ((oldResult | oldAfterResult) == InvalidationReason.NONE && ShouldDrawSalvagedPreview(isUndoableAfter)) {
								Restore(pfx, oldGamepadKey, oldDt, ref isSimulating, oldState);
								SimulateClick(g, oldState, oldCombat, key, isUndoable, isUndoableAfter);
								return;
							}
						}

						prognosticationCache.Add(key, (fakeState, oldResult == InvalidationReason.NONE ? isUndoable : oldResult, oldAfterResult == InvalidationReason.NONE ? isUndoableAfter : oldAfterResult));
						prognosticationMattersCache.Add(key, anyActions || isUndoable != InvalidationReason.NONE);

					} else {
						prognosticationMattersCache.Add(key, false);
					}

					Restore(pfx, oldGamepadKey, oldDt, ref isSimulating, oldState);
                } else {
					prognosticationMattersCache.Add(key, false);
				}
            }
        }
    }

	private static bool KeepSimulatingCombat(G g, State s, Combat c) {
		bool anyActions = false;
		// int amount = 0, count = 0;
		stopSimulatingCombat = false;
		while ((!c.EitherShipIsDead(s) || c.cardActions.Any(action => action.canRunAfterKill == true)) && c.cardActions.Count != 0) {
			anyActions = true;
			if (stopSimulatingCombat) break;
			// if (count == c.cardActions.Count) amount++;
			// else count = c.cardActions.Count;
			c.Update(g);
			// fakeCombat.DrainCardActions(g);
			if (c.routeOverride != null) c.routeOverride = null;
		}
		return anyActions;
	}

	private static void MakeStatePredictable(State s, Combat c, InvalidationReason reason) {
		if (reason.HasFlag(InvalidationReason.SECRET_BRITTLE)) RemoveAllSecretBrittle(s, c);
		if (reason.HasFlag(InvalidationReason.PILE_CONTENTS) || reason.HasFlag(InvalidationReason.UNKNOWN_ORDER)) RemoveAllCards(s, c);
	}

	private static void RemoveAllSecretBrittle(State s, Combat c) {
		foreach (Ship ship in new List<Ship>() {s.ship, c.otherShip}) {
			foreach (Part part in ship.parts) {
				if (part.brittleIsHidden && part.damageModifier == PDamMod.brittle) {
					part.brittleIsHidden = false;
					part.damageModifier = PDamMod.none;
				}
			}
		}
	}

	private static void RemoveAllCards(State s, Combat c) {
		s.deck = [];
		c.discard = [];
		c.exhausted = [];
		ModData.SetModData(c, PreventShuffleKey, true);
	}
	
	[HarmonyPrefix]
	[HarmonyPatch(typeof(State), nameof(State.ShuffleDeck))]
	public static bool State_ShuffleDeck_Prefix(State __instance, bool isMidCombat) {
		if (isMidCombat && __instance.route is Combat c)
			return !ModData.TryGetModData(c, PreventShuffleKey, out bool value) || !value;
		return true;
	}


    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.GetCardIsMovingTooMuchToPlay))]
    public static void Card_GetCardIsMovingTooMuchToPlay_Postfix(State s, Card __instance, ref bool __result)
    {
		if (isSimulating) __result = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Combat), nameof(Combat.CheckDeath))]
    public static bool Combat_CheckDeath_Prefix()
    {
		return !isSimulating;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Combat), nameof(Combat.OnMouseDownRight))]
    public static void Combat_OnMouseDownRight_Postfix(G g, Box b, Combat __instance)
    {
		Card? card = __instance.TryGetHandCardFromBox(b);
		if (card != null)
		{
			ClearPrognostication();
		}
    }

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Combat), nameof(Combat.OnInputPhase))]
	public static void Combat_OnInputPhase_Prefix(G g, Box b, Combat __instance)
	{
		if (b.key == Input.currentGpKey)
		{
			Card? card = __instance.TryGetHandCardFromBox(b);
			if (card != null && Input.GetGpDown(Btn.B, false))
			{
				ClearPrognostication();
			}
		}
	}
	
    public static bool SpriteBatch_Draw_Prefix() => !disableDraw;

    public static void ModSoundEntry_CreateInstance_Prefix(ref bool started) {
        if (isSimulating)
            started = false;
    }
    public static void ModSoundEntry_CreateInstance_Postfix(bool started, ref IModSoundInstance __result, IModSoundEntry __instance) {
        if (isSimulating)
			__result = new FakeModSoundInstance(__instance, __result.Channel, 0);
	}
	
    public static void EventSoundEntry_CreateInstance_Prefix(ref bool started) {
        if (isSimulating)
            started = false;
    }
    public static void EventSoundEntry_CreateInstance_Postfix(bool started, ref IEventSoundInstance __result, IEventSoundEntry __instance) {
		if (isSimulating)
			__result = new FakeEventSoundInstance(__instance, __result.Instance, 0);
	}

	[HarmonyPrefix]
    [HarmonyPatch(typeof(Audio), nameof(Audio.PlayEvent))]
    public static bool CutAudioString() => !isSimulating;

	[HarmonyPrefix]
    [HarmonyPatch(typeof(ParticleSystem), nameof(ParticleSystem.Render))]
    public static bool CutParticleSystems() => !isSimulating;

	[HarmonyPrefix]
    [HarmonyPatch(typeof(Input), nameof(Input.Rumble))]
    public static bool CutRumble() => !isSimulating;
}



internal sealed class FakeModSoundInstance(IModSoundEntry entry, Channel channel, int id) : IModSoundInstance
{
	public IModSoundEntry Entry { get; } = entry;
	public Channel Channel { get; } = channel;

	public override string ToString()
		=> Entry.UniqueName;

	public override int GetHashCode()
		=> HashCode.Combine(Entry.UniqueName.GetHashCode(), id);

	public bool IsPaused
	{
		get
		{
			return true;
		}
		set => Audio.Catch(Channel.setPaused(true));
	}

	public float Volume
	{
		get
		{
			return 0;
		}
		set => Audio.Catch(Channel.setVolume(0));
	}

	public float Pitch
	{
		get
		{
			Audio.Catch(this.Channel.getPitch(out var volume));
			return volume;
		}
		set => Audio.Catch(this.Channel.setPitch(value));
	}
}

internal sealed class FakeEventSoundInstance(IEventSoundEntry entry, EventInstance instance	, int id) : IEventSoundInstance
{
	public IEventSoundEntry Entry { get; } = entry;
	public EventInstance Instance { get; } = instance;

	~FakeEventSoundInstance()
	{
		Audio.Catch(Instance.getPlaybackState(out var playbackState));
		if (playbackState is PLAYBACK_STATE.STOPPED)
		{
			Audio.Catch(Instance.release());
			return;
		}

		Instance.setCallback((type, _, _) =>
		{
			if (type is not (EVENT_CALLBACK_TYPE.STOPPED or EVENT_CALLBACK_TYPE.SOUND_STOPPED))
				return RESULT.OK;
			return Instance.release();
		});
	}

	public override string ToString()
		=> Entry.UniqueName;

	public override int GetHashCode()
		=> HashCode.Combine(Entry.UniqueName.GetHashCode(), id);

	public bool IsPaused
	{
		get
		{
			return true;
		}
		set => Audio.Catch(Instance.setPaused(true));
	}

	public float Volume
	{
		get
		{
			return 0;
		}
		set => Audio.Catch(Instance.setVolume(0));
	}

	public float Pitch
	{
		get
		{
			Audio.Catch(Instance.getPitch(out var volume));
			return volume;
		}
		set => Audio.Catch(Instance.setPitch(value));
	}
}
