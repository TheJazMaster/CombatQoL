using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Nickel;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch(typeof(Ship))]
public static class ShipPatches
{
	private static IModData ModData => ModEntry.Instance.Helper.ModData;


    [HarmonyPrefix]
    [HarmonyPatch(nameof(Ship.ModifyDamageDueToParts))]
    public static void Ship_ModifyDamageDueToParts_Prefix(Ship __instance, State s, Combat c, int incomingDamage, Part part, bool piercing = false) {      
        if (DoesPartBeingHitPotentiallyRevealBrittle(__instance, part)) {
            ModData.SetModData(c, SaveStates.UndoInvalidatedDueToSecretBrittleKey, true);
            CombatPatches.MarkPartAsRevealed(c, __instance, part);
        }
    }

	internal static bool DoesPartBeingHitPotentiallyRevealBrittle(Ship ship, Part part) {
		// string key = ship.isPlayerShip ? PartsClearedPlayerKey : PartsClearedKey;
        var partsCleared = ModData.GetModDataOrDefault<Dictionary<int, bool>>(ship, CombatPatches.PartsClearedKey, []);
		if (ship.parts.Count(p => p.type != PType.empty && ((p.brittleIsHidden && p.damageModifier == PDamMod.brittle) || p.damageModifier == PDamMod.none) && !(partsCleared.TryGetValue(p.uuid, out var v) && v)) <= 1) return false;
		
		if (!ship.parts.Any(p => p.brittleIsHidden && p.damageModifier == PDamMod.brittle)) return false;
		if (part.damageModifier != PDamMod.none && (part.damageModifier != PDamMod.brittle || !part.brittleIsHidden)) return false;
		
		return partsCleared.TryGetValue(part.uuid, out var value) && !value;
	}

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Ship.RenderPartUI))]
	private static void Ship_RenderPartUI_Postfix(Ship __instance, G g, Part part, int localX, string keyPrefix, bool isPreview)
	{
        if (!ModEntry.Instance.Settings.ProfileBased.Current.ShowHiddenBrittle)
            return;
		if (part.invincible || part.type == PType.empty || (part.damageModifier != PDamMod.none && (part.damageModifier != PDamMod.brittle || !part.brittleIsHidden)))
			return;
		if (g.boxes.FirstOrDefault(b => b.key == new UIKey(StableUK.part, localX, keyPrefix)) is not { } box)
			return;
        bool final = false;
        if (!DoesPartBeingHitPotentiallyRevealBrittle(__instance, part)) {
            if (part.damageModifier == PDamMod.brittle && part.brittleIsHidden) final = true;
            else return;
        }

		var offset = isPreview ? 25 : 34;
		var v = box.rect.xy + new Vec(0, __instance.isPlayerShip ? (offset - 16) : 8);

		var color = new Color(1, 1, 1, 0.8 + Math.Sin(g.state.time * 4.0) * 0.3);
		Draw.Sprite(ModEntry.Instance.BrittleSprite, v.x, v.y, color: color);

		if (!box.IsHover())
			return;
		g.tooltips.Add(g.tooltips.pos,
            new GlossaryTooltip($"{ModEntry.Instance.Package.Manifest.UniqueName}::PartModifier::PotentialBrittle")
			{
				Icon = ModEntry.Instance.BrittleSprite,
				TitleColor = Colors.parttrait,
				Title = ModEntry.Instance.Localizations.Localize(["glossary", "potentialBrittle", "name"]),
				Description = ModEntry.Instance.Localizations.Localize(["glossary", "potentialBrittle", final ? "descriptionFinal" : "description"])
			});
        g.tooltips.Add(g.tooltips.pos, new TTGlossary("parttrait.brittle"));
	}
}