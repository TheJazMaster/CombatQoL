using Newtonsoft.Json;
using Nickel;
using System;

namespace TheJazMaster.CombatQoL;

internal sealed class Settings
{
	[JsonProperty]
	public ProfileSettings Global = new();

	[JsonIgnore]
	public ProfileBasedValue<IModSettingsApi.ProfileMode, ProfileSettings> ProfileBased;

	public Settings()
	{
		ProfileBased = ProfileBasedValue.Create(
			() => ModEntry.Instance.Helper.ModData.GetModDataOrDefault(MG.inst.g?.state ?? DB.fakeState, "ActiveProfile", IModSettingsApi.ProfileMode.Slot),
			profile => ModEntry.Instance.Helper.ModData.SetModData(MG.inst.g?.state ?? DB.fakeState, "ActiveProfile", profile),
			profile => profile switch
			{
				IModSettingsApi.ProfileMode.Global => Global,
				IModSettingsApi.ProfileMode.Slot => ModEntry.Instance.Helper.ModData.ObtainModData<ProfileSettings>(MG.inst.g?.state ?? DB.fakeState, "ProfileSettings"),
				_ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
			},
			(profile, data) =>
			{
				switch (profile)
				{
					case IModSettingsApi.ProfileMode.Global:
						Global = data;
						break;
					case IModSettingsApi.ProfileMode.Slot:
						ModEntry.Instance.Helper.ModData.SetModData(MG.inst.g?.state ?? DB.fakeState, "ProfileSettings", data);
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			}
		);
	}
}

internal sealed class ProfileSettings
{
	public enum FlashTypes {
		NONE,
		LIGHT_BLUE,
		FAINT_BLUE,
		FAINT_GREEN
	}

	public bool PreviewEnabled = false;
	public bool AllowCheating = false;
	public bool Prognosticate = true;
	public bool SalvageEnabled = false;
	public bool ShowHiddenBrittle = true;
	public FlashTypes FlashType = FlashTypes.FAINT_BLUE;

	public Action<ProfileSettings> GetSettingTransferFunc() {
		bool previewEnabled = PreviewEnabled;
		bool allowCheating = AllowCheating;
		bool prognosticate = Prognosticate;
		bool salvageEnabled = SalvageEnabled;
		bool showHiddenBrittle = ShowHiddenBrittle;
		FlashTypes flashType = FlashType;
		return settings => {
			settings.PreviewEnabled = previewEnabled;
			settings.AllowCheating = allowCheating;
			settings.Prognosticate = prognosticate;
			settings.SalvageEnabled = salvageEnabled;
			settings.ShowHiddenBrittle = showHiddenBrittle;
			settings.FlashType = flashType;
		};
	}
}