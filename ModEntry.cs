using FMOD;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;
using Nanoray.PluginManager;
using Newtonsoft.Json;
using Nickel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.RegularExpressions;
using TheJazMaster.CombatQoL.Artifacts;
using TheJazMaster.CombatQoL.Patches;
using static TheJazMaster.CombatQoL.ProfileSettings;

namespace TheJazMaster.CombatQoL;

public sealed class ModEntry : SimpleMod {

	internal static ModEntry Instance { get; private set; } = null!;
    internal Harmony Harmony { get; }
	internal ICombatQolApi Api { get; }

	internal ILocalizationProvider<IReadOnlyList<string>> AnyLocalizations { get; }
	internal ILocaleBoundNonNullLocalizationProvider<IReadOnlyList<string>> Localizations { get; }

	internal static bool ReadyToSave = true;

	internal Spr UndoOffSprite;
	internal Spr UndoSprite;
	internal Spr UndoOnSprite;
	internal Spr UndoInvalidSprite;
	internal Spr UndoOffInvalidSprite;
	internal Spr UndoGoodSprite;
	internal Spr UndoOffGoodSprite;

	internal Spr EyeSprite;
	internal Spr EyeOnSprite;
	internal Spr EyeOffSprite;
	internal Spr EyeClosedSprite;
	internal Spr EyeOnClosedSprite;
	internal Spr EyeConfusedSprite;
	internal Spr EyeOnConfusedSprite;

	internal Spr BrittleSprite;

	internal ISoundEntry UndoSound;

	internal Settings Settings { get; private set; }

    public ModEntry(IPluginPackage<IModManifest> package, IModHelper helper, ILogger logger) : base(package, helper, logger)
	{
		Instance = this;
		Harmony = new(package.Manifest.UniqueName);
		Api = new ApiImplementation();
		foreach (MethodInfo method in typeof(SpriteBatch).GetMethods().Where(m => m.Name == "Draw")) {
			Harmony.TryPatch(
				logger: Logger,
				original: method,
				prefix: new HarmonyMethod(typeof(SimulationPatches), nameof(SimulationPatches.SpriteBatch_Draw_Prefix))
			);
		}
		Harmony.PatchAll();

		StatePatches.displayClass = typeof(State).GetNestedTypes(AccessTools.all).Where(t => t.GetMethods(AccessTools.all).Any(m => m.Name.StartsWith("<PopulateRun>") && m.ReturnType == typeof(Route))).First();
		Harmony.TryPatch(
			logger: Logger,
			original: typeof(State).GetNestedTypes(AccessTools.all).SelectMany(t => t.GetMethods(AccessTools.all)).First(m => m.Name.StartsWith("<PopulateRun>") && m.ReturnType == typeof(Route)),
			postfix: new HarmonyMethod(typeof(StatePatches), nameof(StatePatches.State_PopulateRun_Delegate_Postfix))
		);
		Harmony.Patch(
			original: typeof(IModData).Assembly
				.GetType("Nickel.EventSoundEntry")!
				.GetMethod("CreateInstance", AccessTools.all)!,
			postfix: new HarmonyMethod(typeof(SimulationPatches), nameof(SimulationPatches.EventSoundEntry_CreateInstance_Postfix))
		);
		Harmony.Patch(
			original: typeof(IModData).Assembly
				.GetType("Nickel.ModSoundEntry")!
				.GetMethod("CreateInstance", AccessTools.all)!,
			postfix: new HarmonyMethod(typeof(SimulationPatches), nameof(SimulationPatches.ModSoundEntry_CreateInstance_Postfix))
		);

		AnyLocalizations = new JsonLocalizationProvider(
			tokenExtractor: new SimpleLocalizationTokenExtractor(),
			localeStreamFunction: locale => package.PackageRoot.GetRelativeFile($"I18n/en.json").OpenRead()
		);
		Localizations = new MissingPlaceholderLocalizationProvider<IReadOnlyList<string>>(
			new CurrentLocaleOrEnglishLocalizationProvider<IReadOnlyList<string>>(AnyLocalizations)
		);
		Settings = helper.Storage.LoadJson<Settings>(helper.Storage.GetMainStorageFile("json"));

		UndoOffSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_off.png")).Sprite;
		UndoSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo.png")).Sprite;
		UndoOnSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_on.png")).Sprite;
		UndoOffInvalidSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_off_invalid.png")).Sprite;
		UndoInvalidSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_invalid.png")).Sprite;
		UndoOffGoodSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_off_good.png")).Sprite;
		UndoGoodSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/undo_good.png")).Sprite;

		EyeSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye.png")).Sprite;
		EyeOnSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_on.png")).Sprite;
		EyeOffSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_off.png")).Sprite;
		EyeClosedSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_closed.png")).Sprite;
		EyeOnClosedSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_on_closed.png")).Sprite;
		EyeConfusedSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_confused.png")).Sprite;
		EyeOnConfusedSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/eye_on_confused.png")).Sprite;

		BrittleSprite = helper.Content.Sprites.RegisterSprite(package.PackageRoot.GetRelativeFile("Sprites/brittle.png")).Sprite;

		UndoSound = helper.Content.Audio.RegisterSound(package.PackageRoot.GetRelativeFile("Sounds/click.wav"));

		helper.Events.RegisterAfterArtifactsHook(nameof(Artifact.OnTurnEnd), (State state, Combat combat) => {
			if (!Settings.ProfileBased.Current.AllowCheating) SaveStates.DeleteSaves(combat);
		});

		helper.Events.RegisterAfterArtifactsHook(nameof(Artifact.OnCombatEnd), (State state) => {
			SaveStates.DeleteSaves();
			SimulationPatches.ClearPrognostication();
		});

		helper.ModRegistry.AwaitApi<IModSettingsApi>(
			"Nickel.ModSettings",
			api => api.RegisterModSettings(api.MakeList([
				api.MakeEnumStepper(
					() => Localizations.Localize(["settings", "flash", "name"]),
					() => Settings.ProfileBased.Current.FlashType,
					(value) => {
						Settings.ProfileBased.Current.FlashType = value;
					}
				).SetTooltips(() => [
					new GlossaryTooltip($"settings.{package.Manifest.UniqueName}::{nameof(ProfileSettings.FlashType)}")
					{
						TitleColor = Colors.textBold,
						Title = Localizations.Localize(["settings", "flash", "name"]),
						Description = Localizations.Localize(["settings", "flash", "description"])
					}
				]).SetValueFormatter(value => {
					return Localizations.Localize(["settings", "flash", "values", Enum.GetName(value) ?? "NONE"]);
				})
				.SetValueWidth(rect => 100),
				api.MakeCheckbox(
					() => Localizations.Localize(["settings", "prognostication", "name"]),
					() => Settings.ProfileBased.Current.Prognosticate,
					(_, _, value) => {
						Settings.ProfileBased.Current.Prognosticate = value;
						SimulationPatches.ClearPrognostication();
					}
				).SetTooltips(() => [
					new GlossaryTooltip($"settings.{package.Manifest.UniqueName}::{nameof(ProfileSettings.Prognosticate)}")
					{
						TitleColor = Colors.textBold,
						Title = Localizations.Localize(["settings", "prognostication", "name"]),
						Description = Localizations.Localize(["settings", "prognostication", "description"])
					}
				]),
				api.MakeCheckbox(
					() => Localizations.Localize(["settings", "salvage", "name"]),
					() => Settings.ProfileBased.Current.SalvageEnabled,
					(_, _, value) => {
						Settings.ProfileBased.Current.SalvageEnabled = value;
						SimulationPatches.ClearPrognostication();
					}
				).SetTooltips(() => [
					new GlossaryTooltip($"settings.{package.Manifest.UniqueName}::{nameof(ProfileSettings.SalvageEnabled)}")
					{
						TitleColor = Colors.textBold,
						Title = Localizations.Localize(["settings", "salvage", "name"]),
						Description = Localizations.Localize(["settings", "salvage", "description"])
					}
				]),
				api.MakeCheckbox(
					() => Localizations.Localize(["settings", "showHiddenBrittle", "name"]),
					() => Settings.ProfileBased.Current.ShowHiddenBrittle,
					(_, _, value) => {
						Settings.ProfileBased.Current.ShowHiddenBrittle = value;
						SimulationPatches.ClearPrognostication();
					}
				).SetTooltips(() => [
					new GlossaryTooltip($"settings.{package.Manifest.UniqueName}::{nameof(ProfileSettings.ShowHiddenBrittle)}")
					{
						TitleColor = Colors.textBold,
						Title = Localizations.Localize(["settings", "showHiddenBrittle", "name"]),
						Description = Localizations.Localize(["settings", "showHiddenBrittle", "description"])
					}
				]),
				api.MakeCheckbox(
					() => Localizations.Localize(["settings", "cheating", "name"]),
					() => Settings.ProfileBased.Current.AllowCheating,
					(_, _, value) => Settings.ProfileBased.Current.AllowCheating = value
				).SetTooltips(() => [
					new GlossaryTooltip($"settings.{package.Manifest.UniqueName}::{nameof(ProfileSettings.AllowCheating)}")
					{
						TitleColor = Colors.textBold,
						Title = Localizations.Localize(["settings", "cheating", "name"]),
						Description = Localizations.Localize(["settings", "cheating", "description"])
					}
				])
			]).SubscribeToOnMenuClose(_ =>
			{
				helper.Storage.SaveJson(helper.Storage.GetMainStorageFile("json"), Settings);
			}))
		);

		helper.Content.Artifacts.RegisterArtifact("TurnCounter", new()
		{
			ArtifactType = typeof(TurnCounter),
			Meta = new()
			{
				owner = Deck.colorless,
				pools = [ArtifactPool.EventOnly],
				unremovable = true
			},
			Sprite = helper.Content.Sprites.RegisterSprite(Instance.Package.PackageRoot.GetRelativeFile("Sprites/TurnCounter.png")).Sprite,
			Name = AnyLocalizations.Bind(["artifact", "TurnCounter", "name"]).Localize,
			Description = AnyLocalizations.Bind(["artifact", "TurnCounter", "description"]).Localize
		});
    }

	public override object? GetApi(IModManifest requestingMod)
	{
		return Api;
	}
}