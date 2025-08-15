using System;
using static TheJazMaster.CombatQoL.SaveStates;

namespace TheJazMaster.CombatQoL;

public interface ICombatQolApi {
    [Flags] // Ordered from most important to least important (in terms of how much info they reveal)
    public enum InvalidationReason {
        NONE = 0,
        HIDDEN_INFORMATION = 1<<0,
        PILE_CONTENTS = 1<<1,
        RNG_SEED = 1<<2,
        SECRET_BRITTLE = 1<<3,
        UNKNOWN_ORDER = 1<<4,
        DYING_ENEMY = 1<<5,
        DYING_PLAYER = 1<<6,
        TURN_END = 1<<7,
    }
	enum RngTypes {
		ACTION, SHUFFLE, AI, CARD_OFFERINGS, CARD_OFFERINGS_MIDCOMBAT, ARTIFACT_OFFERINGS
	}

    bool IsSimulating();

    bool IsDrawingFuture();

    void MarkSafeRngAdvance(Combat c, RngTypes type, int count = 1);

	void ClearKnownCards(Combat c, CardDestination destination);

	void InvalidateUndos(Combat c, InvalidationReason reason);

	void MarkCardAsOkayToBeGone(Combat c, Card card);

	void MarkCardAsOkayToBeGone(Combat c, int uuid);
}