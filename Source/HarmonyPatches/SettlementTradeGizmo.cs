using System.Collections.Generic;
using HarmonyLib;
using MGAutoSell.Caravans;
using RimWorld.Planet;
using Verse;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patch that adds an automated trade toggle gizmo to world settlements when experimental shuttle trading is enabled.
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetGizmos))]
    public static class SettlementTradeGizmo
    {
        private static string _automaticallyTradeLabel, _automaticallyTradeDescription;

        /// <summary>
        /// Appends the automated trade toggle gizmo to tradeable world settlements.
        /// </summary>
        public static void Postfix(Settlement __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Settings.FF_Shuttles)
            {
                return;
            }

            _automaticallyTradeLabel ??= "MGAutoSell.Caravan.AutomaticallyTradeLabel".Translate();
            _automaticallyTradeDescription ??= "MGAutoSell.Caravan.AutomaticallyTradeDescription".Translate();
            var comp = Find.World.GetComponent<SettlementTracker>();
            if (__instance.CanTradeNow)
            {
                __result = __result.AddItem(new Command_Toggle
                {
                    defaultLabel = _automaticallyTradeLabel,
                    defaultDesc = _automaticallyTradeDescription,
                    isActive = () => comp.Settlements.Contains(__instance),
                    toggleAction = () => ToggleSettlement(comp.Settlements, __instance),
                });
            }
        }

        /// <summary>
        /// Toggles membership of <paramref name="settlement"/> in <paramref name="settlements"/>.
        /// </summary>
        /// <returns><c>true</c> if the settlement is now tracked; <c>false</c> if it was removed.</returns>
        internal static bool ToggleSettlement(HashSet<Settlement> settlements, Settlement settlement)
        {
            if (settlements == null || settlement == null)
            {
                return false;
            }

            if (settlements.Add(settlement))
            {
                return true;
            }

            settlements.Remove(settlement);
            return false;
        }
    }
}
