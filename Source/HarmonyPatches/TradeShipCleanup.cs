using HarmonyLib;
using RimWorld;
using Verse;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patch that removes departing orbital trade ships from the tracked traders set.
    /// </summary>
    [HarmonyPatch(typeof(PassingShipManager), nameof(PassingShipManager.RemoveShip))]
    public static class TradeShipCleanup
    {
        /// <summary>
        /// Postfix callback executed when a <see cref="PassingShip"/> leaves orbit.
        /// </summary>
        public static void Postfix(PassingShip vis)
        {
            CleanupShip(Current.Game?.GetComponent<TradeRulesGameComp>(), vis);
        }

        /// <summary>
        /// Removes <paramref name="vis"/> from <paramref name="comp"/>'s processed traders set if it implements <see cref="ITrader"/>.
        /// </summary>
        internal static void CleanupShip(TradeRulesGameComp comp, PassingShip vis)
        {
            if (comp == null || vis is not ITrader trader)
            {
                return;
            }

            comp.traders?.Remove(trader);
        }
    }
}
