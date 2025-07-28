using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nickel;
using static TheJazMaster.CombatQoL.ICombatQolApi;
using static TheJazMaster.CombatQoL.SaveStates;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch(typeof(Combat))]
public static class CombatPatches
{
	private static IModData ModData => ModEntry.Instance.Helper.ModData;

    internal readonly static UK undoUK = ModEntry.Instance.Helper.Utilities.ObtainEnumCase<UK>();
	internal readonly static UK futurePreviewUK = ModEntry.Instance.Helper.Utilities.ObtainEnumCase<UK>();
    internal readonly static OnMouseDownHandler handler = new();
	
	internal readonly static string PartsClearedKey = "PartsCleared";
	// internal readonly static string PartsClearedPlayerKey = "PartsClearedPlayer";
	

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Combat.DrainCardActions))]
    public static void Combat_DrainCardActions_Prefix(G g, Combat __instance) {
		if (SimulationPatches.IsSimulating()) return;

        if (__instance.cardActions.Count > 0) ModEntry.ReadyToSave = true;
    }

    [HarmonyPostfix]
	[HarmonyPriority(Priority.VeryLow)] 
    [HarmonyPatch(nameof(Combat.Update))]
    public static void Combat_Update_Postfix(G g, Combat __instance) {
		if (SimulationPatches.IsSimulating()) return;

		if (ModEntry.ReadyToSave && __instance.cardActions.Count == 0 && __instance.currentCardAction == null) {
			ModEntry.ReadyToSave = false;
			SaveState(g.state, __instance);
		}
    }


    [HarmonyPostfix]
    [HarmonyPatch(nameof(Combat.Render))]
    public static void Combat_Render_Postfix(G g, Combat __instance) {
        if (!__instance.IsVisible()) return;

        RenderUndo(g, __instance);
		RenderEyeButton(g, __instance);
		RenderFutureOver(g, g.state, __instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Combat.RenderBehindCockpit))]
    public static void Combat_RenderBehindCockpit_Prefix(G g, Combat __instance) {
        if (!__instance.IsVisible()) return;

		RenderFutureUnder(g, g.state, __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Combat.Make))]
    public static void Combat_Make_Postfix(State s, AI ai, bool doForReal, Combat __result) {
		if (!doForReal) return;

        ResetBrittlenessKnowledge(s, __result, __result.otherShip, __result.otherShip.parts);
		ResetBrittlenessKnowledge(s, __result, s.ship, __result.otherShip.parts);
		ModEntry.ReadyToSave = true;
    }

	internal static void ResetBrittlenessKnowledge(State s, Combat c, Ship ship, List<Part> previousParts) {
		// string key = ship.isPlayerShip ? PartsClearedPlayerKey : PartsClearedKey;
        Dictionary<int, bool> partsCleared = [];

		if (ship.parts.Any(
			part => {
				var pp = previousParts.Find(prt => prt.uuid == part.uuid);
				return part.brittleIsHidden && 
					part.damageModifier == PDamMod.brittle && 
					(pp?.damageModifier == PDamMod.none || pp?.damageModifier == PDamMod.brittle && pp?.brittleIsHidden == true);
			})
		) {
			foreach (Part part in ship.parts) {
				partsCleared.Add(part.uuid, !(part.damageModifier == PDamMod.none || part.damageModifier == PDamMod.brittle && part.brittleIsHidden));
			}
		}
		ModData.SetModData(ship, PartsClearedKey, partsCleared);
	}

	internal static void MarkPartAsRevealed(Combat c, Ship ship, Part part) {
		// string key = ship.isPlayerShip ? PartsClearedPlayerKey : PartsClearedKey;
		var partsCleared = ModData.GetModDataOrDefault<Dictionary<int, bool>>(ship, PartsClearedKey, []);
		if (partsCleared.TryGetValue(part.uuid, out var value) && !value) {
			partsCleared.Remove(part.uuid);
			partsCleared.Add(part.uuid, true);
		}
	}

	private static void RenderFutureUnder(G g, State ls, Combat lc) {
        if (FutureRenderingPatches.RenderFutureThisFrame != null && SimulationPatches.TryGetPrognosticationState(FutureRenderingPatches.RenderFutureThisFrame.Value, out var s)) {
			Draw.SetBlendMode(BlendState.AlphaBlend, null, null);
			Draw.EndAutoBatchFrame();

			Matrix oldMatrix = MG.inst.cameraMatrix;
			MG.inst.cameraMatrix = Matrix.Identity;

			Draw.StartAutoBatchFrame();
			MG.inst.sb.Draw(FutureRenderingPatches.PreviewTexture, MG.inst.renderTarget.Bounds, Colors.healthBarShield.fadeAlpha(.5).ToMgColor());

			Draw.EndAutoBatchFrame();
			MG.inst.cameraMatrix = oldMatrix;
			Draw.StartAutoBatchFrame();
		}
	}

	private static void RenderFutureOver(G g, State ls, Combat lc) {
        if (FutureRenderingPatches.RenderFutureThisFrame != null && SimulationPatches.TryGetPrognosticationState(FutureRenderingPatches.RenderFutureThisFrame.Value, out var s)) {
			if (s.route is Combat c) {
				FutureRenderingPatches.DrawHealthDifference(g, s, ls, c, lc);
				FutureRenderingPatches.DrawMidrowDifference(g, c, lc);
			}
		}
	}

	[HarmonyPatch(typeof(MG), nameof(MG.UpdateRenderPipelineIfNeeded)), HarmonyPostfix]
	private static void UpdateRenderPipelineIfNeeded(MG __instance) {
		if (FutureRenderingPatches.PreviewTexture == null || FutureRenderingPatches.PreviewTexture.Width != __instance.renderTarget.Width || FutureRenderingPatches.PreviewTexture.Height != __instance.renderTarget.Height) {
			FutureRenderingPatches.PreviewTexture?.Dispose();
			FutureRenderingPatches.PreviewTexture = new(MG.inst.GraphicsDevice, __instance.renderTarget.Width, __instance.renderTarget.Height);
		}
	}

    private static Vec undoPos = new(Combat.marginRect.w - 15.0, Combat.marginRect.h - 60.0);
    private static void RenderEyeButton(G g, Combat c)
    {
        // Rect rect = new(5 + Mutil.AnimHelper(c.introTimer, -90, 0, 360, 0), 18, 17, 18);
		Rect rect = new(undoPos.x + 3 + Mutil.AnimHelper(c.introTimer, 90, 0, 360, 0.3), undoPos.y - 15, 14, 15);
        UIKey key = (g.hoverKey.HasValue && SimulationPatches.TryGetPrognosticationActionPresence(g.hoverKey.Value, out bool anyActions) && anyActions) ? g.hoverKey.Value : new UIKey(StableUK.combat_endTurn);
        bool showAsPressed = false;
		bool isInvalid = SimulationPatches.IsFutureDrawInvalid(key, out var reasonAfter);
		bool salvagable = SimulationPatches.ShouldDrawSalvagedPreview(reasonAfter);
        var button = SharedArt.ButtonSprite(g, rect, new(futurePreviewUK), 
			FutureRenderingPatches.IsPreviewEnabled() ? (salvagable ? ModEntry.Instance.EyeConfusedSprite : (isInvalid ? ModEntry.Instance.EyeClosedSprite : ModEntry.Instance.EyeSprite)) : ModEntry.Instance.EyeOffSprite,
			FutureRenderingPatches.IsPreviewEnabled() ? (salvagable ? ModEntry.Instance.EyeOnConfusedSprite : (isInvalid ? ModEntry.Instance.EyeOnClosedSprite : ModEntry.Instance.EyeOnSprite)) : ModEntry.Instance.EyeOffSprite,
			null, null, inactive: false, flipX: true, flipY: false, onMouseDown: handler, autoFocus: false, noHover: false, showAsPressed, gamepadUntargetable: false);
		if (button.isHover) {
			Vec vec = rect.xy + new Vec(20, -18);
			if (isInvalid) {
				g.tooltips.AddText(vec, CardPatches.UndoPrognosticationText(reasonAfter, CardPatches.PrognosticationWarningType.INVALID_AFTER));
			}
			else
				g.tooltips.AddText(vec, ModEntry.Instance.Localizations.Localize(["glossary", "futureVision", FutureRenderingPatches.IsPreviewEnabled() ? "enabled" : "disabled"]));
		}
    }

    private static void RenderUndo(G g, Combat c) {
        UIKey? key = new(undoUK);
		Rect rect = new(undoPos.x + Mutil.AnimHelper(c.introTimer, 90, 0, 360, 0.3), undoPos.y, 17, 18);
		bool undoPossible = IsUndoPossible();
		OnMouseDown? onMouseDown = (c.PlayerCanAct(g.state) && undoPossible) ? handler : null;
		Box box = g.Push(key, rect, null, autoFocus: false, noHoverSound: false, gamepadUntargetable: false, ReticleMode.Quad, onMouseDown);
		Vec xy = box.rect.xy;
		Vec tooltipVec = xy + new Vec(0, -60);
		Spr spriteActive = box.IsHover() ? ModEntry.Instance.UndoOnSprite : ModEntry.Instance.UndoSprite;
		Spr spriteInactive = ModEntry.Instance.UndoOffSprite;
		if (box.IsHover())
		{
			if (undoPossible) {
				g.tooltips.AddText(tooltipVec, ModEntry.Instance.Localizations.Localize(["glossary", "undo", "normal"]));
				g.tooltips.AddText(tooltipVec, UndoText());
			} else {
				InvalidationReason lastReasons = ModData.GetModDataOrDefault(c, LastInvalidationReason, InvalidationReason.NONE);
				if (lastReasons != InvalidationReason.NONE)
					g.tooltips.AddText(tooltipVec, UndoDisabledText(lastReasons));
				else
					g.tooltips.AddText(tooltipVec, ModEntry.Instance.Localizations.Localize(["glossary", "undo", "disabled"]));
			}
		}
		if (c.PlayerCanAct(g.state) && g.hoverKey.HasValue && SimulationPatches.TryGetPrognosticationValidity(g.hoverKey.Value, out var mask) && mask != InvalidationReason.NONE) {
			if (mask.HasFlag(InvalidationReason.DYING_ENEMY)) {
				spriteActive = ModEntry.Instance.UndoGoodSprite;
				spriteInactive = ModEntry.Instance.UndoOffGoodSprite;
			} else {
				spriteActive = ModEntry.Instance.UndoInvalidSprite;
				spriteInactive = ModEntry.Instance.UndoOffInvalidSprite;
			}
			g.tooltips.AddText(tooltipVec, CardPatches.UndoPrognosticationText(mask, CardPatches.PrognosticationWarningType.INVALID_BEFORE));
		}
		Draw.Sprite(IsUndoPossible() ? spriteActive : spriteInactive, xy.x, xy.y);
		g.Pop();
    }

	private static string UndoText()
	{
		if (PlatformIcons.GetPlatform() == Platform.MouseKeyboard) return "<c=keyword>" + ModEntry.Instance.Localizations.Localize(["glossary", "undo", "instructions", "mouse"]) + "</c>";
		string text = PlatformIcons.GetPlatform() switch
		{
			Platform.Xbox => Loc.T("controller.xbox.a"), 
			Platform.NX => Loc.T("controller.nx.a"), 
			Platform.PS => Loc.T("controller.ps.x"), 
			_ => Loc.T("controller.xbox.a"), 
		};
		return "<c=keyword>" + ModEntry.Instance.Localizations.Localize(["glossary", "undo", "instructions", "controller"], new { Button = text } ) + "</c>";
	}

	private static string UndoDisabledText(InvalidationReason mask)
	{
		StringBuilder sb = new();
		foreach (InvalidationReason reason in Enum.GetValues(typeof(InvalidationReason))) {
			if (reason != InvalidationReason.NONE && mask.HasFlag(reason)) {
				sb.Append(ModEntry.Instance.Localizations.Localize(["glossary", "undo", "prognostication", reason.Key()]));
				break;
			}
		}
		return ModEntry.Instance.Localizations.Localize(["glossary", "undo", "disabledReasons"], new { Reasons = sb.ToString() });
	}
}

sealed class OnMouseDownHandler : OnMouseDown, OnInputPhase
{
	public void OnMouseDown(G g, Box b)
	{
		HandleInput(g, b);
	}

	public void OnInputPhase(G g, Box b)
	{
		if (b.key != g.hoverKey)
			return;
		if (!Input.GetGpDown(Btn.A))
			return;

		HandleInput(g, b);
	}

	private static void HandleInput(G g, Box b) {
		if (SimulationPatches.IsSimulating()) return;
		
		if (b.key == CombatPatches.undoUK)
		{
			RollBack(g);
		}
		if (b.key == CombatPatches.futurePreviewUK)
		{
			FutureRenderingPatches.SetPreview(!FutureRenderingPatches.IsPreviewEnabled());
			SimulationPatches.ClearPrognostication();
		}
	}
}
