using System.Collections.Generic;
using HarmonyLib;
using MGAutoSell.Caravans;
using RimWorld;
using Verse;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patches that add an automated trade toggle gizmo to player passenger shuttles and remove destroyed shuttles from tracking.
    /// </summary>
    [HarmonyPatch]
    public static class ShuttleTradeGizmo
    {
        /// <summary>
        /// Appends the automated trade toggle gizmo to player-owned passenger shuttles when experimental shuttle trading is enabled.
        /// </summary>
        [HarmonyPatch(typeof(Building_PassengerShuttle), nameof(Building_PassengerShuttle.GetGizmos))]
        [HarmonyPostfix]
        public static void Gizmo(Building_PassengerShuttle __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Settings.FF_Shuttles)
            {
                return;
            }

            var comp = Find.World.GetComponent<SettlementTracker>();
            if (__instance.Faction == Faction.OfPlayer)
            {
                __result = __result.AddItem(new Command_Toggle
                {
                    isActive = () => comp.Shuttles.Contains(__instance),
                    toggleAction = () => ToggleShuttle(comp.Shuttles, __instance)
                });
            }
        }

        /// <summary>
        /// Removes a destroyed passenger shuttle from the world's <see cref="SettlementTracker"/>.
        /// </summary>
        [HarmonyPatch(typeof(Building), nameof(Building.Destroy))]
        [HarmonyPostfix]
        public static void Destroy(Building __instance)
        {
            if (__instance is not Building_PassengerShuttle)
            {
                return;
            }

            var comp = Find.World?.GetComponent<SettlementTracker>();
            RemoveDestroyedShuttle(comp, __instance);
        }

        /// <summary>
        /// Toggles membership of <paramref name="shuttle"/> in <paramref name="shuttles"/>.
        /// </summary>
        /// <returns><c>true</c> if the shuttle is now tracked; <c>false</c> if it was removed.</returns>
        internal static bool ToggleShuttle(HashSet<Building_PassengerShuttle> shuttles, Building_PassengerShuttle shuttle)
        {
            if (shuttles == null || shuttle == null)
            {
                return false;
            }

            if (shuttles.Add(shuttle))
            {
                return true;
            }

            shuttles.Remove(shuttle);
            return false;
        }

        /// <summary>
        /// Removes <paramref name="building"/> from <paramref name="comp"/>'s tracked shuttles if it is a <see cref="Building_PassengerShuttle"/>.
        /// </summary>
        internal static bool RemoveDestroyedShuttle(SettlementTracker comp, Building building)
        {
            if (comp?.Shuttles == null || building is not Building_PassengerShuttle shuttle)
            {
                return false;
            }

            return comp.Shuttles.Remove(shuttle);
        }
    }
}
