using HarmonyLib;
using Verse;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patch that removes despawned pawns from the tracked traders set and negotiator overrides.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class TradePawnCleanup
    {
        /// <summary>
        /// Postfix callback executed when a <see cref="Pawn"/> despawns from a map.
        /// </summary>
        public static void Postfix(Pawn __instance)
        {
            CleanupPawn(Current.Game?.GetComponent<TradeRulesGameComp>(), __instance);
        }

        /// <summary>
        /// Removes <paramref name="pawn"/> from <paramref name="comp"/>'s processed traders set and per-map seller overrides.
        /// </summary>
        internal static void CleanupPawn(TradeRulesGameComp comp, Pawn pawn)
        {
            if (comp == null || pawn == null)
            {
                return;
            }

            comp.traders?.Remove(pawn);
            comp.SellerOverride?.RemoveAll(x => x.Value == pawn);
        }
    }
}
