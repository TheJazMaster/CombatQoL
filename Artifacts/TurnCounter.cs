namespace TheJazMaster.CombatQoL.Artifacts;

internal sealed class TurnCounter : Artifact
{
	public int counter = 0;

	public override void OnTurnStart(State state, Combat combat) {
		counter++;
	}

	public override void OnCombatStart(State state, Combat combat) {
		counter = 0;
	}

	public override int? GetDisplayNumber(State s) => counter;
}