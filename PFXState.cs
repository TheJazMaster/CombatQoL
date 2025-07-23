public sealed class PFXState
{
	public required ParticleSystem combatAlpha { private get; init; }
	public required ParticleSystem combatAdd { private get; init; }
	public required ParticleSystem combatExplosion { private get; init; }
	public required ParticleSystem combatExplosionUnder { private get; init; }
	public required ParticleSystem combatExplosionSmoke { private get; init; }
	public required ParticleSystem combatExplosionWhiteSmoke { private get; init; }
	public required ParticleSystem combatScreenFadeOut { private get; init; }
	public required ParticleSystem screenSpaceAdd { private get; init; }
	public required ParticleSystem screenSpaceAlpha { private get; init; }
	public required ParticleSystem screenSpaceExplosion { private get; init; }
	public required Sparks combatSparks { private get; init; }
	public required Sparks screenSpaceSparks { private get; init; }

	public static PFXState Create()
		=> new()
		{
			combatAlpha = PFX.combatAlpha,
			combatAdd = PFX.combatAdd,
			combatExplosion = PFX.combatExplosion,
			combatExplosionUnder = PFX.combatExplosionUnder,
			combatExplosionSmoke = PFX.combatExplosionSmoke,
			combatExplosionWhiteSmoke = PFX.combatExplosionWhiteSmoke,
			combatScreenFadeOut = PFX.combatScreenFadeOut,
			screenSpaceAdd = PFX.screenSpaceAdd,
			screenSpaceAlpha = PFX.screenSpaceAlpha,
			screenSpaceExplosion = PFX.screenSpaceExplosion,
			combatSparks = PFX.combatSparks,
			screenSpaceSparks = PFX.screenSpaceSparks,
		};

	public static void BlankOut()
	{
		PFX.combatAlpha = new();
		PFX.combatAdd = new();
		PFX.combatExplosion = new();
		PFX.combatExplosionUnder = new();
		PFX.combatExplosionSmoke = new();
		PFX.combatExplosionWhiteSmoke = new();
		PFX.combatScreenFadeOut = new();
		PFX.screenSpaceAdd = new();
		PFX.screenSpaceAlpha = new();
		PFX.screenSpaceExplosion = new();
		PFX.combatSparks = new();
		PFX.screenSpaceSparks = new();
	}

	public void Restore()
	{
		PFX.combatAlpha = combatAlpha;
		PFX.combatAdd = combatAdd;
		PFX.combatExplosion = combatExplosion;
		PFX.combatExplosionUnder = combatExplosionUnder;
		PFX.combatExplosionSmoke = combatExplosionSmoke;
		PFX.combatExplosionWhiteSmoke = combatExplosionWhiteSmoke;
		PFX.combatScreenFadeOut = combatScreenFadeOut;
		PFX.screenSpaceAdd = screenSpaceAdd;
		PFX.screenSpaceAlpha = screenSpaceAlpha;
		PFX.screenSpaceExplosion = screenSpaceExplosion;
		PFX.combatSparks = combatSparks;
		PFX.screenSpaceSparks = screenSpaceSparks;
	}
}