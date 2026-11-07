using System.Collections.Generic;
using MGAutoSell.Caravans;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Represents a candidate shuttle trade route to a world settlement along with its distance, expected silver, fuel cost, and priority.
    /// </summary>
    /// <param name="Settlement">The destination world settlement.</param>
    /// <param name="Distance">Approximate distance in world tiles.</param>
    /// <param name="Silver">Expected silver revenue from trading at the settlement.</param>
    /// <param name="FuelRequirement">Amount of chemfuel required to launch to the settlement.</param>
    /// <param name="Priority">Calculated priority tier for the route.</param>
    public record TradeRoute(
        Settlement Settlement,
        float Distance,
        float Silver,
        float FuelRequirement,
        SettlementTradePriority Priority);

    /// <summary>
    /// Priority classification for candidate settlement trade routes based on silver-to-fuel efficiency.
    /// </summary>
    public enum SettlementTradePriority
    {
        /// <summary>
        /// Fallback route with low silver-to-fuel ratio (ratio &lt;= 2).
        /// </summary>
        Backup,

        /// <summary>
        /// Low-priority route with moderate silver-to-fuel ratio (2 &lt; ratio &lt; 4).
        /// </summary>
        Low,

        /// <summary>
        /// Medium-priority route with high silver-to-fuel ratio (ratio &gt;= 4) where the settlement has limited extra silver.
        /// </summary>
        Medium,

        /// <summary>
        /// Highest-priority route with high silver-to-fuel ratio (ratio &gt; 4) and abundant settlement silver reserves.
        /// </summary>
        High
    }

    /// <summary>
    /// Game-wide component managing trade rules, processed traders, map sell caches, and scheduled background cache refreshes.
    /// </summary>
    public class TradeRulesGameComp : GameComponent
    {
        /// <summary>
        /// Gets or sets the <see cref="ITradeRouteCalculator"/> used to score and select shuttle trade routes.
        /// </summary>
        public static ITradeRouteCalculator RouteCalculator { get; set; } = TradeRouteCalculator.Instance;

        /// <summary>
        /// Ordered collection of user-defined trade rules.
        /// </summary>
        public TradeRulesGroup tradeRules = new();

        /// <summary>
        /// Set of traders that have already been traded with during their current visit.
        /// </summary>
        public HashSet<ITrader> traders = new();

        /// <summary>
        /// Indicates whether colonists should automatically initiate trade jobs.
        /// </summary>
        public bool autoTrade = false;

        /// <summary>
        /// Set of colonist <see cref="Thing.thingIDNumber"/> values permitted to perform autonomous bartering.
        /// </summary>
        public HashSet<int> autoTraderIDs = new();

        /// <summary>
        /// Cached <see cref="ItemsToSell"/> preview per map.
        /// </summary>
        public Dictionary<Map, ItemsToSell> SellCache = [];

        /// <summary>
        /// Optional manual negotiator override pawn per map.
        /// </summary>
        public Dictionary<Map, Pawn> SellerOverride = [];

        /// <summary>
        /// Best calculated shuttle trade route per map when experimental shuttle trading is enabled.
        /// </summary>
        public Dictionary<Map, TradeRoute> NextTradeRoute = [];

        private readonly Queue<Map> cacheOrder = new();
        private int tickWait => 2500 / Mathf.Max(SellCache.Count, 1);
        private int nextTick = -1;

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRulesGameComp"/> class for the specified <paramref name="game"/>.
        /// </summary>
        /// <param name="game">The active RimWorld <see cref="Game"/> instance.</param>
        public TradeRulesGameComp(Game game)
        {
            tradeRules ??= new();
            traders ??= new();
        }

        /// <summary>
        /// Initializes potential item caches and populates the initial sell cache for each active map after game load.
        /// </summary>
        public override void FinalizeInit()
        {
            LongEventHandler.ExecuteWhenFinished(() => tradeRules.GetPossibleItemsList([]));
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (var map in Find.Maps)
                {
                    var cache = CacheUtility.Cache(this, map, out _);
                    SellCache[map] = cache;
                    cacheOrder.Enqueue(map);
                }
            });
        }

        /// <summary>
        /// Retrieves the cached <see cref="ItemsToSell"/> for <paramref name="map"/>, or recomputes it when <paramref name="refresh"/> is <c>true</c>.
        /// </summary>
        /// <param name="map">The map to fetch the sell cache for.</param>
        /// <param name="refresh">If <c>true</c>, forces an immediate cache recomputation.</param>
        /// <returns>The cached or freshly computed <see cref="ItemsToSell"/>.</returns>
        public ItemsToSell Fetch(Map map, bool refresh = false)
        {
            if (!refresh && SellCache.TryGetValue(map, out var cache))
            {
                return cache;
            }

            SellerOverride.TryGetValue(map, out var seller);
            cache = CacheUtility.Cache(this, map, out _, seller);
            SellCache[map] = cache;
            return cache;
        }

        /// <summary>
        /// Periodically refreshes the sell cache for maps in round-robin order.
        /// </summary>
        public override void GameComponentTick()
        {
            if (nextTick > Find.TickManager.TicksGame)
            {
                return;
            }

            nextTick = Find.TickManager.TicksGame + tickWait;

            if (cacheOrder.Count == 0)
            {
                return;
            }

            var map = cacheOrder.Dequeue();
            if (map == null)
            {
                return;
            }

            try
            {
                SellerOverride.TryGetValue(map, out var seller);
                var cache = CacheUtility.Cache(this, map, out _, seller);
                SellCache[map] = cache;

                if (ModsConfig.OdysseyActive && Settings.FF_Shuttles)
                {
                    var comp = Find.World.GetComponent<SettlementTracker>();
                    var shuttle = comp.GetBestForMap(map);

                    var tradeRoute = GetTradeRoute(shuttle, cache, comp);
                    if (tradeRoute != null)
                    {
                        NextTradeRoute[map] = tradeRoute;
                    }
                }
            }
            finally
            {
                cacheOrder.Enqueue(map);
            }
        }

        /// <summary>
        /// Evaluates reachable settlements and selects the highest-priority shuttle <see cref="TradeRoute"/> in O(S) time.
        /// </summary>
        private TradeRoute GetTradeRoute(Building_PassengerShuttle shuttle, ItemsToSell cache, SettlementTracker comp)
        {
            if (shuttle == null)
            {
                return null;
            }

            float minFuel = shuttle.LaunchableComp.Props.minFuelCost;
            if (shuttle.FuelLevel < minFuel + 5f)
            {
                return null;
            }

            float total = cache.TotalSilver.Value;
            if (total < minFuel * 4f)
            {
                return null;
            }

            float fuelWithBuffer = shuttle.FuelLevel - 5f;

            var distances = comp.GetDistances(
                shuttle.Map,
                maxDistance: layer =>
                {
                    float maxDistance = shuttle.LaunchableComp.MaxLaunchDistanceAtFuelLevel(fuelWithBuffer, layer);
                    if (shuttle.LaunchableComp.Props.fixedLaunchDistanceMax >= 0)
                    {
                        maxDistance = Mathf.Min(maxDistance, shuttle.LaunchableComp.Props.fixedLaunchDistanceMax);
                    }

                    return maxDistance;
                },
                settlementPredicate: settlement =>
                    traders.Contains(settlement) ||
                    settlement.TradeCurrency == TradeCurrency.Favor ||
                    GetTraderSilverCount(settlement) < 200);

            if (distances.Count == 0)
            {
                return null;
            }

            TradeRoute bestRoute = null;
            foreach (var (settlement, distance) in distances)
            {
                var traderKind = settlement.trader.TraderKind;
                float traderTotal = 0f;
                for (int i = 0; i < cache.Items.Count; i++)
                {
                    var sellRecord = cache.Items[i];
                    if (traderKind.WillTrade(sellRecord.Item))
                    {
                        traderTotal += sellRecord.Total.Value;
                    }
                }

                float traderSilver = GetTraderSilverCount(settlement);
                float sales = RouteCalculator.CalculateExpectedSales(traderTotal, traderSilver, total);
                float fuelCost = shuttle.LaunchableComp.FuelNeededToLaunchAtDist(distance, settlement.Tile.Layer);
                var priority = RouteCalculator.EvaluatePriority(sales, fuelCost, traderSilver);

                var candidate = new TradeRoute(settlement, distance, sales, fuelCost, priority);
                if (bestRoute == null || TradeRouteCalculator.IsBetterRoute(candidate, bestRoute))
                {
                    bestRoute = candidate;
                }
            }

            return bestRoute;
        }

        private static int GetTraderSilverCount(Settlement settlement)
        {
            var stock = settlement?.trader?.StockListForReading;
            if (stock == null)
            {
                return 0;
            }

            for (int i = 0; i < stock.Count; i++)
            {
                var thing = stock[i];
                if (thing != null && thing.def == ThingDefOf.Silver)
                {
                    return thing.stackCount;
                }
            }

            return 0;
        }

        /// <summary>
        /// Serializes and deserializes trade rules, processed traders, and auto-trade settings using RimWorld's Scribe system.
        /// </summary>
        public override void ExposeData()
        {
            traders?.RemoveWhere(static x => x is not ILoadReferenceable);

            Scribe_Deep.Look(ref tradeRules, nameof(tradeRules));
            Scribe_Collections.Look(ref traders, nameof(traders), LookMode.Reference);

            Scribe_Values.Look(ref autoTrade, nameof(autoTrade));
            Scribe_Collections.Look(ref autoTraderIDs, nameof(autoTraderIDs));

            tradeRules ??= new();
            traders ??= new();
            autoTraderIDs ??= new();
        }
    }
}
