using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nickel;

namespace TheJazMaster.CombatQoL.Patches;

[HarmonyPatch]
public static class FutureRenderingPatches
{
    private static IModData ModData => ModEntry.Instance.Helper.ModData;
    public static RenderTarget2D? PreviewTexture { get; set; }
    public static UIKey? RenderFutureThisFrame { get; set; }

    public static bool isRenderingFuture = false;

    internal readonly static OnMouseDownHandler handler = new();


    public static bool ShouldDrawFuture(Combat currentCombat) {
        return IsPreviewEnabled() && currentCombat.isPlayerTurn;
    }

    public static void SetPreview(bool value) {
        ModEntry.Instance.Settings.ProfileBased.Current.PreviewEnabled = value;
    }

    public static bool IsPreviewEnabled() {
        return ModEntry.Instance.Settings.ProfileBased.Current.PreviewEnabled;
    }

    public static bool IsPreviewSalvageEnabled() {
        return ModEntry.Instance.Settings.ProfileBased.Current.SalvageEnabled;
    }

    public static void DrawPreview(G g, State s, State ls, Combat c, Combat lc) {
        MG.inst.GraphicsDevice.SetRenderTarget(PreviewTexture);
        MG.inst.GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Transparent);

        Draw.StartAutoBatchFrame();

        c.camX = lc.camX;
        s.ship.xLerped = s.ship.x; s.ship.shake = 0;
        c.otherShip.xLerped = c.otherShip.x; c.otherShip.shake = 0;

        g.state = s;
        isRenderingFuture = true;
        var pfx = PFXState.Create();
		PFXState.BlankOut();
        g.Push(null, Combat.marginRect, noInput: true);
        c.RenderShipsUnder(g);
        c.RenderShipsOver(g);
        c.RenderDrones(g);
        DrawMidrowDifference(g, c, lc);
        DrawHealthDifference(g, s, ls, c, lc);
        g.Pop();
        isRenderingFuture = false;
        pfx.Restore();
        g.state = ls;

        Draw.EndAutoBatchFrame();

        MG.inst.GraphicsDevice.SetRenderTarget(MG.inst.renderTarget);
    }
    
    public static void DrawMidrowDifference(G g, Combat c, Combat lc)
    {
        Rect rect = Combat.marginRect + Combat.arenaPos + lc.GetCamOffset();
        
        g.Push(null, rect);

        foreach ((int x, StuffBase thing) in lc.stuff)
        {
            Vec loc = thing.GetGetRect().xy + rect.xy;
            // thing.Render(g, loc);

            if (c.stuff.TryGetValue(x, out var thingAfter) && thingAfter.GetType() == thing.GetType()) continue;
            Draw.Sprite(StableSpr.icons_heat_warning, loc.x + 2, loc.y + 10, color: Colors.white.fadeAlpha(Fade(g)));
        }

        g.Pop();
    }

    public static void DrawHealthDifference(G g, State s, State ls, Combat c, Combat lc)
    {
        DrawHealthDifferenceForShip(g, s.ship, ls.ship);
        DrawHealthDifferenceForShip(g, c.otherShip, lc.otherShip);
    }

    private static double Fade(G g, double mult = 1) =>
        Math.Abs(Math.Sin(g.time * 2.5)) * mult;

    private static void DrawHealthDifferenceForShip(G g, Ship ship, Ship lastShip) {
        int hull = lastShip.hull;
        int hullAfter = ship.hull;

        int maxHull = lastShip.hullMax;
        int maxHullAfter = ship.hullMax;
        
        int shield = lastShip.Get(Status.shield);
        int shieldAfter = ship.Get(Status.shield);

        int tempShield = lastShip.Get(Status.tempShield);
        int tempShieldAfter = ship.Get(Status.tempShield);

        int maxShield = lastShip.GetMaxShield();
        int maxShieldAfter = ship.GetMaxShield();

        int hullPlusMaxShield = lastShip.hullMax + maxShield;
        int shipWidth = 16 * (lastShip.parts.Count + 2);
        int chunkWidth = Mutil.Clamp(shipWidth / hullPlusMaxShield, 2, 4) - 1;
        int healthChunkHeight = 5;
        int shieldChunkHeight = 3;
        int chunkMargin = 1;

        bool isPlayer = lastShip.isPlayerShip;

        UIKey healthBarUIKey = new(StableUK.healthBar, 0, isPlayer ? "combat_ship_player" : "combat_ship_enemy");
        if (g.boxes.Find(box => box.key.HasValue && box.key.Value == healthBarUIKey) is not { } box) return;
        Vec v = box.rect.xy;
        int hullDiff = ship.hullMax - lastShip.hullMax;
        int shieldDiff = maxShieldAfter + tempShieldAfter - (maxShield + tempShield);
        Rect hullRect = new(v.x + lastShip.hullMax * (chunkWidth + chunkMargin), v.y, hullDiff * (chunkWidth + chunkMargin) + chunkMargin, healthChunkHeight + 2*chunkMargin);
            Rect shieldRect = new(hullRect.x + hullRect.w + (maxShield + tempShield) * (chunkWidth + chunkMargin), 
                hullRect.y, shieldDiff * (chunkWidth + chunkMargin) + chunkMargin, shieldChunkHeight + 2*chunkMargin);

        if (hullDiff > 0) {
            Draw.Rect(hullRect.x, hullRect.y - 2, hullRect.w + (maxShield == 0 ? 1 : 0), hullRect.h + 4, Colors.black.fadeAlpha(0.75));
            Draw.Rect(hullRect.x, hullRect.y - 1, hullRect.w, hullRect.h + 2, Colors.healthBarBorder);
            Draw.Rect(hullRect.x, hullRect.y, hullRect.w - 1, hullRect.h, Colors.healthBarBbg);
            Draw.Rect(hullRect.x + 1, hullRect.y, hullRect.w - 1, shieldRect.h, Colors.healthBarBbg);
            DrawChunks(lastShip.hullMax, ship.hullMax, healthChunkHeight, Colors.healthBarHealth.fadeAlpha(0.25));
        }
        if (shieldDiff > 0) {

            Draw.Rect(shieldRect.x, shieldRect.y - 2, shieldRect.w + 1, shieldRect.h + 4, Colors.black.fadeAlpha(0.75));
            Draw.Rect(shieldRect.x, shieldRect.y - 1, shieldRect.w, shieldRect.h + 2, Colors.healthBarBorder);
            Draw.Rect(shieldRect.x, shieldRect.y, shieldRect.w - 1, shieldRect.h, Colors.healthBarBbg);
            DrawChunks(maxHull + maxShield, maxHullAfter + maxShieldAfter, shieldChunkHeight, Colors.healthBarShield.fadeAlpha(0.25));
            DrawChunks(maxHull + maxShield + tempShield, maxHullAfter + maxShieldAfter + tempShieldAfter, shieldChunkHeight, Colors.healthBarTempShield.fadeAlpha(0.25));
        }


        if (hull != hullAfter) DrawChunks(hull, hullAfter, healthChunkHeight, (hullAfter > hull ? Colors.heal : Colors.white).fadeAlpha(Fade(g)));
        if (shield != shieldAfter) DrawChunks(maxHull + shield, maxHull + shieldAfter, shieldChunkHeight, (shieldAfter > shield ? Colors.healthBarShield : Colors.white).fadeAlpha(Fade(g, shieldAfter > shield ? 0.7 : 1)));    
        if (tempShield != tempShieldAfter) DrawChunks(maxHull + maxShield + tempShield, maxHull + maxShield + tempShieldAfter, shieldChunkHeight, (tempShieldAfter > tempShield ? Colors.healthBarTempShield : Colors.white).fadeAlpha(Fade(g, tempShieldAfter > tempShield ? 0.7 : 1)));    
       
        void DrawChunks(int startIndex, int endIndex, int height, Color color) {
            int diff = endIndex - startIndex;
            for (int i = 0; i < diff; i++) {
                DrawChunk(i + startIndex, height, color, false);
            }
            for (int i = 0; i > diff; i--) {
                DrawChunk(i - 1 + startIndex, height, color, false);
            }
        }

        void DrawChunk(int i, int height, Color color, bool rightMargin)
        {
            double num9 = v.x + 1 + (i * (chunkWidth + chunkMargin));
            double y = v.y + 1;
            Draw.Rect(num9, y, chunkWidth, height, color);
            if (rightMargin)
            {
                Draw.Rect(num9 + chunkWidth, y, chunkMargin, height, color.fadeAlpha(0.5));
            }
        }
    }

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Ship), nameof(Ship.GetShowHull))]
	private static void Ship_GetShowHull_Postfix(Ship __instance, ref bool __result)
	{
		if (isRenderingFuture) __result = true;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Ship), nameof(Ship.DrawTopEffects))]
	private static bool Ship_DrawTopEffects_Prefix(Ship __instance, G g, Vec v, Vec worldPos)
	{
		return !isRenderingFuture;
	}

    [HarmonyPatch(typeof(Glow), nameof(Glow.Draw), [typeof(Vec), typeof(double), typeof(Color)]), HarmonyPrefix]
    public static bool CutGlow() => !isRenderingFuture;

    [HarmonyPatch(typeof(Glow), nameof(Glow.Draw), [typeof(Vec), typeof(Vec), typeof(Color)]), HarmonyPrefix]
    public static bool CutGlowAlt() => !isRenderingFuture;
}