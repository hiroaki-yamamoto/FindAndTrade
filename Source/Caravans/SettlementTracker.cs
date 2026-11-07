using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MGAutoSell.Caravans
{
    /*
     * Every hour, check settlements that are within range and still have money
     * Do work at the comms
     * Work out the exact trade ahead of time, and pack those items and silver
     * Once loaded, trader finds work to get in
     *
     */
    /// <summary>
    /// World component that tracks player-selected settlements and shuttles for automated shuttle trading,
    /// caching inter-tile distances for fast route evaluation.
    /// </summary>
    public class SettlementTracker(World world) : WorldComponent(world)
    {
        /// <summary>
        /// Set of world settlements opted into automated shuttle trading.
        /// </summary>
        public HashSet<Settlement> Settlements = [];

        /// <summary>
        /// Set of player shuttles opted into automated trading.
        /// </summary>
        public HashSet<Building_PassengerShuttle> Shuttles = [];

        private readonly Dictionary<(Map, Settlement), float> _distanceCache = new();

        /// <summary>
        /// Computes or retrieves cached tile distances from <paramref name="map"/> to all tracked <see cref="Settlements"/>
        /// that pass the optional <paramref name="settlementPredicate"/> exclusion filter and <paramref name="maxDistance"/> limit.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(S) single pass over <see cref="Settlements"/> without intermediate list cloning or <c>RemoveAll</c>.
        /// Space Complexity: O(K) where K is the number of eligible settlements within range.
        /// </remarks>
        /// <param name="map">The origin map.</param>
        /// <param name="trader">Optional colonist negotiator to verify trade eligibility with each settlement.</param>
        /// <param name="maxDistance">Optional callback returning the maximum allowed launch distance for a given <see cref="PlanetLayer"/>.</param>
        /// <param name="settlementPredicate">Optional exclusion predicate; settlements returning <c>true</c> are skipped.</param>
        /// <param name="distanceCalculator">Optional delegate to calculate tile distance when not yet cached; defaults to <see cref="WorldGrid.ApproxDistanceInTiles"/>.</param>
        /// <returns>A dictionary mapping each eligible <see cref="Settlement"/> to its distance in tiles.</returns>
        public Dictionary<Settlement, float> GetDistances(
            Map map,
            Pawn trader = null,
            Func<PlanetLayer, float> maxDistance = null,
            Predicate<Settlement> settlementPredicate = null,
            Func<Map, Settlement, float> distanceCalculator = null)
        {
            var distances = new Dictionary<Settlement, float>();
            if (map == null || Settlements == null || Settlements.Count == 0)
            {
                return distances;
            }

            distanceCalculator ??= static (m, s) => Find.WorldGrid.ApproxDistanceInTiles(m.Tile, s.Tile);

            foreach (var settlement in Settlements)
            {
                if (settlement == null)
                {
                    continue;
                }

                if (settlementPredicate != null && settlementPredicate(settlement))
                {
                    continue;
                }

                if (trader is not null && !trader.CanTradeWith(settlement.Faction, settlement.trader.TraderKind))
                {
                    continue;
                }

                if (!_distanceCache.TryGetValue((map, settlement), out float distance))
                {
                    distance = distanceCalculator(map, settlement);
                    _distanceCache[(map, settlement)] = distance;
                }

                if (maxDistance != null)
                {
                    var layer = Find.World != null ? settlement.Tile.Layer : null;
                    if (distance > maxDistance(layer))
                    {
                        continue;
                    }
                }

                distances[settlement] = distance;
            }

            return distances;
        }

        /// <summary>
        /// Selects the best available <see cref="Building_PassengerShuttle"/> spawned on <paramref name="map"/>,
        /// prioritizing undamaged shuttles first and higher fuel levels second.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N) single pass over <see cref="Shuttles"/> instead of O(N log N) sorting.
        /// Space Complexity: O(1) auxiliary memory.
        /// </remarks>
        /// <param name="map">The map to search for shuttles.</param>
        /// <param name="fuelGetter">Optional delegate to retrieve a shuttle's fuel level; defaults to <see cref="Building_PassengerShuttle.FuelLevel"/>.</param>
        /// <param name="mapGetter">Optional delegate to retrieve a shuttle's map; defaults to <see cref="Thing.Map"/>.</param>
        /// <param name="isFullHealthEvaluator">Optional delegate to determine whether a shuttle is at full health; defaults to <c>HitPoints &gt;= BaseMaxHitPoints</c>.</param>
        /// <returns>The best <see cref="Building_PassengerShuttle"/> on <paramref name="map"/>, or <c>null</c> if none are found.</returns>
        public Building_PassengerShuttle GetBestForMap(
            Map map,
            Func<Building_PassengerShuttle, float> fuelGetter = null,
            Func<Building_PassengerShuttle, Map> mapGetter = null,
            Func<Building_PassengerShuttle, bool> isFullHealthEvaluator = null)
        {
            if (map == null || Shuttles == null || Shuttles.Count == 0)
            {
                return null;
            }

            fuelGetter ??= static s => s.FuelLevel;
            mapGetter ??= static s => s.Map;
            isFullHealthEvaluator ??= static s => s.HitPoints >= s.def.BaseMaxHitPoints;

            Building_PassengerShuttle best = null;
            bool bestIsFullHealth = false;
            float bestFuel = 0f;

            foreach (var shuttle in Shuttles)
            {
                if (shuttle == null || mapGetter(shuttle) != map)
                {
                    continue;
                }

                bool isFullHealth = isFullHealthEvaluator(shuttle);
                float fuel = fuelGetter(shuttle);
                if (best == null)
                {
                    best = shuttle;
                    bestIsFullHealth = isFullHealth;
                    bestFuel = fuel;
                    continue;
                }

                if (isFullHealth != bestIsFullHealth)
                {
                    if (isFullHealth)
                    {
                        best = shuttle;
                        bestIsFullHealth = true;
                        bestFuel = fuel;
                    }

                    continue;
                }

                if (fuel > bestFuel)
                {
                    best = shuttle;
                    bestFuel = fuel;
                }
            }

            return best;
        }

        /// <summary>
        /// Serializes and deserializes tracked settlements and shuttles.
        /// </summary>
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Settlements, "settlements", LookMode.Reference);
            Scribe_Collections.Look(ref Shuttles, "shuttles", LookMode.Reference);

            Settlements ??= [];
            Shuttles ??= [];
        }
    }
}
