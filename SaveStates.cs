

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FSPRO;
using Microsoft.Extensions.Logging;
using Nickel;
using TheJazMaster.CombatQoL.Patches;
using static TheJazMaster.CombatQoL.ProfileSettings;
using static TheJazMaster.CombatQoL.ICombatQolApi;
using FMOD;
using System.Runtime.InteropServices;
using System.Diagnostics.Metrics;

namespace TheJazMaster.CombatQoL;

public static class SaveStates 
{
    private static IModData ModData => ModEntry.Instance.Helper.ModData;
    internal static readonly string KnownCardsKey = "KnownCards";
    internal static readonly string SavesKey = "SavesForSaving"; // Only used for saving
    internal static List<State> saves = [];
    internal static State? currentSave = null;
    internal static readonly string SafeRngAdvanceKey = "SafeRngAdvance";
    internal static readonly string CardsOkayToBeGoneKey = "CardsOkayToBeGone";
    internal static readonly string UndoInvalidatedDueToSecretBrittleKey = "UndoInvalidatedDueToSecretBrittle";
	internal static readonly string UndoInvalidatedDueToTurnEndKey = "UndoInvalidatedDueToTurnEnd";
    internal static readonly string LastInvalidationReason = "LastInvalidationReason";
    internal static readonly string UndoInvalidatedCustomKey = "UndoInvalidatedCustom";
    public const InvalidationReason InvalidationReasonObvious = InvalidationReason.DYING_ENEMY | InvalidationReason.DYING_PLAYER | InvalidationReason.TURN_END;

    public static State Duplicate(State s) {
        if (s.route is Combat c)
            ModData.RemoveModData(c, SavesKey);
        return Mutil.DeepCopy(s);
    }

    private static void TransferSave(G g, State state, bool silent = false, bool updateTime = true) {
        var age = g.state.map.age;
        var duration = g.state.storyVars.runTimer;
        var func = ModEntry.Instance.Settings.ProfileBased.Current.GetSettingTransferFunc();

        g.state = state;

        if (updateTime) {
            var oldDt = g.dt;
            g.dt = 1000;
            g.state.Update(g);
            g.dt = oldDt;
        }

        func(ModEntry.Instance.Settings.ProfileBased.Current);
        g.state.map.age = age;
        g.state.storyVars.runTimer = duration;
        if (!silent) {
            PlayFlash(g.state);
        }
    }

    public static void PlayFlash(State state) {
        switch (ModEntry.Instance.Settings.ProfileBased.Current.FlashType) {
            case FlashTypes.FAINT_BLUE: {
                state.flash = new Color(0, 0.25, 0.5);
                break;
            }
            case FlashTypes.LIGHT_BLUE: {
                state.flash = new Color(0, 0.5, 1);
                break;
            }
            case FlashTypes.FAINT_GREEN: {
                state.flash = new Color(0, 0.35, 0.35);
                break;
            }
            default:
                break;
        }
    }

    public static bool RollBack(G g, bool silent = false, bool updateTime = true)
    {
        if (SimulationPatches.IsSimulating()) return false;

        SimulationPatches.ClearPrognostication();

        if (!saves.TryPop(out var ls)) return false;
        currentSave = Duplicate(ls);
        TransferSave(g, ls, silent, updateTime);
        if (!silent) Audio.Play(Event.Plink);//ModEntry.Instance.UndoSound.CreateInstance();

        return true;
    }

    internal static List<int> GetKnownCardsInPile(Combat c, CardDestination place) {
        return ModData.GetModDataOrDefault<List<int>>(c, KnownCardsKey + place, []);
    }
    internal static void SetLastKnownCards(Combat c, List<int> value, CardDestination place) {
        ModData.SetModData(c, KnownCardsKey + place, value);
    }

    private static bool CanCheat() {
        return ModEntry.Instance.Settings.ProfileBased.Current.AllowCheating;
    }

    public static void SaveState(State state, Combat combat) {
        if (SimulationPatches.IsSimulating()) return;

        SimulationPatches.ClearPrognostication();

        State? ls = currentSave;
        if (ls != null) {
            saves.Push(ls);

            var isUndoable = IsUndoable(state, combat, ls, out var newKnownCards);
            if (isUndoable != InvalidationReason.NONE) {
                DeleteSaves(combat);
                ModData.SetModData(combat, LastInvalidationReason, isUndoable);
            }
            if (isUndoable.HasFlag(InvalidationReason.RNG_SEED)) {
                SetLastKnownCards(combat, [], CardDestination.Deck);
                SetLastKnownCards(combat, [], CardDestination.Discard);
                SetLastKnownCards(combat, [], CardDestination.Exhaust);
            }
            else {
                SetLastKnownCards(combat, newKnownCards[CardDestination.Deck], CardDestination.Deck);
                SetLastKnownCards(combat, newKnownCards[CardDestination.Discard], CardDestination.Discard);
                SetLastKnownCards(combat, newKnownCards[CardDestination.Exhaust], CardDestination.Exhaust);
            }
        }
        foreach (RngTypes type in Enum.GetValues<RngTypes>()) {
            ModData.RemoveModData(combat, SafeRngAdvanceKey + type);
        }
        ModData.RemoveModData(combat, CardsOkayToBeGoneKey);
        ModData.RemoveModData(combat, UndoInvalidatedDueToSecretBrittleKey);
        ModData.RemoveModData(combat, UndoInvalidatedDueToTurnEndKey);
        ModData.RemoveModData(combat, UndoInvalidatedCustomKey);

        currentSave = Duplicate(state);
    }

    public static void DeleteSaves(Combat? c = null) {
        if (SimulationPatches.IsSimulating()) return;

        saves.Clear();
        currentSave = null;
    }

    public static void DeleteLatestSave() {
        if (SimulationPatches.IsSimulating()) return;

        saves.TryPop(out var ls);
        currentSave = ls;
    }

    public static bool IsUndoPossible() => saves.Count > 0;

    public static InvalidationReason IsUndoable(State s, Combat c, State ls, out Dictionary<CardDestination, List<int>> newKnownCards) {
        InvalidationReason mask = InvalidationReason.NONE;
        if (ls == null || ls.route is not Combat lc) {
			newKnownCards = new Dictionary<CardDestination, List<int>> {
				{CardDestination.Deck, []},
                {CardDestination.Discard, []},
                {CardDestination.Exhaust, []}
			};
			return mask;
            }
        
        mask |= DeathValidity(s, c);
        mask |= SecretBrittleValidity(c);
        mask |= RngValidity(s, ls, c);
        mask |= EndTurnValidity(c);
        mask |= CustomValidity(c);

        InvalidationReason pileValidity = PileValidity(s, c, ls, lc, out newKnownCards);
        mask |= pileValidity;

        if (CanCheat()) {
            mask &= InvalidationReason.DYING_ENEMY | InvalidationReason.DYING_PLAYER;
        }
        return mask;
    }
    
    private static InvalidationReason CustomValidity(Combat c) {
        return ModData.GetModDataOrDefault(c, UndoInvalidatedCustomKey, InvalidationReason.NONE);
    }
    
    private static InvalidationReason SecretBrittleValidity(Combat c) {
        return ModData.GetModDataOrDefault(c, UndoInvalidatedDueToSecretBrittleKey, false) ? InvalidationReason.SECRET_BRITTLE : InvalidationReason.NONE;
    }

    private static InvalidationReason EndTurnValidity(Combat c) {
        return ModData.GetModDataOrDefault(c, UndoInvalidatedDueToTurnEndKey, false) ? InvalidationReason.TURN_END : InvalidationReason.NONE;
    }

    private static InvalidationReason DeathValidity(State s, Combat c) {
        if (s.ship.hull <= 0) return InvalidationReason.DYING_PLAYER;
        if (c.otherShip.hull <= 0) return InvalidationReason.DYING_ENEMY;
        return InvalidationReason.NONE;
    }

    private static InvalidationReason RngValidity(State s, State ls, Combat c)
    {
        // Exception for stuff being added to draw pile and such
        foreach (RngTypes type in Enum.GetValues<RngTypes>()) {
            int safeAdvances = ModData.GetModDataOrDefault(c, SafeRngAdvanceKey + type, 0);
            uint seed = type switch {
                RngTypes.ACTION => ls.rngActions.seed,
                RngTypes.AI => ls.rngAi.seed,
                RngTypes.SHUFFLE => ls.rngShuffle.seed,
                RngTypes.CARD_OFFERINGS => ls.rngCardOfferings.seed,
                RngTypes.CARD_OFFERINGS_MIDCOMBAT => ls.rngCardOfferingsMidcombat.seed,
                RngTypes.ARTIFACT_OFFERINGS => ls.rngArtifactOfferings.seed,
                _ => 0
            };
            uint currentSeed = type switch {
                RngTypes.ACTION => s.rngActions.seed,
                RngTypes.AI => s.rngAi.seed,
                RngTypes.SHUFFLE => s.rngShuffle.seed,
                RngTypes.CARD_OFFERINGS => s.rngCardOfferings.seed,
                RngTypes.CARD_OFFERINGS_MIDCOMBAT => s.rngCardOfferingsMidcombat.seed,
                RngTypes.ARTIFACT_OFFERINGS => s.rngArtifactOfferings.seed,
                _ => 0
            };
            if (safeAdvances == 0) {
                if (seed != currentSeed) return InvalidationReason.RNG_SEED;
                else continue;
            }

            Rand rng = new() {
                seed = seed
            };
            for (int i = 0; i < safeAdvances; i++)
                rng.NextInt();
            
            if (currentSeed != rng.seed) return InvalidationReason.RNG_SEED;
        }
        
        return InvalidationReason.NONE;
    }

    private static (InvalidationReason, List<int>) PilesEqual(Combat c, List<Card> newPile, List<Card> oldPile, CardDestination place) {
        List<int> lastKnownCards = GetKnownCardsInPile(c, place);
        List<int> okCards = ModData.GetModDataOrDefault<List<int>>(c, CardsOkayToBeGoneKey, []);
        
        List<int> newKnownCards = [];
        bool flag = oldPile.Count == 0;
        for (int i = 0, j = 0, k = 0;; i++) {
            if (!flag && k < lastKnownCards.Count && oldPile[j].uuid == lastKnownCards[k]) {
                i--;
                k++;
                j++;
            }
            else if (i >= newPile.Count) break;
            else if (!flag && newPile[i].uuid == oldPile[j].uuid) j++;
            else if (!flag && okCards.Any(id => id == oldPile[j].uuid)) {
                i--;
                j++;
            }
            else {
                newKnownCards.Add(newPile[i].uuid);
            }
            if (j >= oldPile.Count) flag = true;
        }
        return (flag ? InvalidationReason.NONE : InvalidationReason.PILE_CONTENTS, newKnownCards);
    }

    private static (InvalidationReason, List<int>) ValidatePile(Combat c, List<Card> newPile, List<Card> oldPile, CardDestination place) {
        if (newPile.Count == 0 && oldPile.Count == 1) return (InvalidationReason.NONE, []);
        var (reason, newCards) = PilesEqual(c, newPile, oldPile, place);
        if (newPile.Count == 0 && reason == InvalidationReason.PILE_CONTENTS) reason = InvalidationReason.UNKNOWN_ORDER;
        return (reason, newCards);
    }

    public static InvalidationReason PileValidity(State s, Combat c, State ls, Combat lc, out Dictionary<CardDestination, List<int>> newCards) {
        (InvalidationReason drawValid, List<int> newDraw) = ValidatePile(c, s.deck, ls.deck, CardDestination.Deck);
        (InvalidationReason discardValid, List<int> newDiscard) = ValidatePile(c, c.discard, lc.discard, CardDestination.Discard);
        (InvalidationReason exhaustValid, List<int> newExhaust) = ValidatePile(c, c.exhausted, lc.exhausted, CardDestination.Exhaust);
        newCards = new() { 
            { CardDestination.Deck, newDraw },
            { CardDestination.Discard, newDiscard },
            { CardDestination.Exhaust, newExhaust } 
        };
        return drawValid | discardValid | exhaustValid;
    }
}

public static class InvalidationReasonExtensions
{
    public static string Key(this InvalidationReason me) => me switch
	{
		InvalidationReason.NONE => "none",
        InvalidationReason.UNKNOWN_ORDER => "unknownOrder",
		InvalidationReason.SECRET_BRITTLE => "invalidBrittle",
		InvalidationReason.RNG_SEED => "invalidRng",
		InvalidationReason.PILE_CONTENTS => "invalidPiles",
        InvalidationReason.DYING_ENEMY => "dyingEnemy",
        InvalidationReason.DYING_PLAYER => "dyingPlayer",
        InvalidationReason.TURN_END => "turnEnd",
		_ => throw new NotImplementedException(),
	};
}