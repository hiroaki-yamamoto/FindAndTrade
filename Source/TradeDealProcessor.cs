using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LudeonTK;
using MGAutoSell.Extensions;
using MGAutoSell.Filter;
using MGAutoSell.HarmonyPatches;
using MGAutoSell.Processing;
using MGAutoSell.Query;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Represents a tradeable item entry along with the quantities held by the colony and the trader.
    /// </summary>
    /// <param name="Tradeable">The underlying RimWorld <see cref="RimWorld.Tradeable"/> row.</param>
    /// <param name="ThingDef">The item definition of the tradeable.</param>
    /// <param name="ColonyCount">Quantity available from the colony.</param>
    /// <param name="TraderCount">Quantity available from the trader.</param>
    public record TradeEntry(Tradeable Tradeable, ThingDef ThingDef, int ColonyCount, int TraderCount);

    /// <summary>
    /// Represents a queued buy or sell order for a specific <see cref="RimWorld.Tradeable"/> and quantity.
    /// </summary>
    /// <param name="Tradeable">The target <see cref="RimWorld.Tradeable"/>.</param>
    /// <param name="Count">The quantity scheduled to be transferred.</param>
    public record SellItem(Tradeable Tradeable, int Count)
    {
        /// <summary>
        /// Gets or sets the target <see cref="RimWorld.Tradeable"/>.
        /// </summary>
        public Tradeable Tradeable { get; set; } = Tradeable;

        /// <summary>
        /// Gets or sets the quantity scheduled to be transferred.
        /// </summary>
        public int Count { get; set; } = Count;
    }

    /// <summary>
    /// Orchestrates automated trade deal evaluation, stack splitting, currency normalization, and summary notifications.
    /// </summary>
    /// <remarks>
    /// Delegates inventory tracking to <see cref="ITradeInventoryAggregator"/>, quantity math to
    /// <see cref="ITradeOrderCalculator"/>, and over-budget trimming to <see cref="ITradeBudgetNormalizer"/>
    /// in compliance with SOLID principles.
    /// </remarks>
    [StaticConstructorOnStartup]
    public static class TradeDealProcessor
    {
        private static readonly MethodInfo PropertySetter =
            AccessTools.PropertySetter(typeof(Tradeable), nameof(Tradeable.CountToTransfer));

        private static string _gray;
        private static string Gray => _gray ??= SafeGetGrayHex();

        private static string _spent, _earned, _bought, _sold;
        private static string Spent => _spent ??= "MGAutoSell.Spent".Translate();
        private static string Earned => _earned ??= "MGAutoSell.Earned".Translate();
        private static string Bought => _bought ??= "MGAutoSell.Bought".Translate();
        private static string Sold => _sold ??= "MGAutoSell.Sold".Translate();

        private static string SafeGetGrayHex()
        {
            try
            {
                return ColorUtility.ToHtmlStringRGB(TradeUI.NoTradeColor);
            }
            catch
            {
                return "808080";
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="ITradeInventoryAggregator"/> responsible for tracking item counts during trade sessions.
        /// </summary>
        public static ITradeInventoryAggregator InventoryAggregator { get; set; } = new TradeInventoryAggregator();

        /// <summary>
        /// Gets or sets the <see cref="ITradeOrderCalculator"/> responsible for calculating buy and sell quantities.
        /// </summary>
        public static ITradeOrderCalculator OrderCalculator { get; set; } = TradeOrderCalculator.Instance;

        /// <summary>
        /// Gets or sets the <see cref="ITradeBudgetNormalizer"/> responsible for trimming over-budget trade orders.
        /// </summary>
        public static ITradeBudgetNormalizer BudgetNormalizer { get; set; } = TradeBudgetNormalizer.Instance;

        /// <summary>
        /// Starts a grouped trading session across multiple traders (such as multiple passing orbital ships).
        /// </summary>
        public static void StartGroupedTrading()
        {
            if (!InventoryAggregator.StartGroupedSession())
            {
                Log.Error("Attempted to start a trading session while one is already running");
            }
        }

        /// <summary>
        /// Ends a grouped trading session and clears all session aggregations.
        /// </summary>
        public static void EndGroupedTrading()
        {
            if (!InventoryAggregator.EndGroupedSession())
            {
                Log.Error("Attempted to end a trading session while one is not running.");
            }
        }

        /// <summary>
        /// Development debug action that executes automated trades against all passing orbital ships on the current map.
        /// </summary>
        [DebugAction("FindAndTrade", "DoTrade")]
        public static void DEBUG_DoTrade()
        {
            var pawns = Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer);
            var socialPawn = pawns.MaxBy(static x => x.skills?.GetSkill(SkillDefOf.Social).Level);
            var ships = Find.CurrentMap.passingShipManager.passingShips;

            StartGroupedTrading();
            try
            {
                foreach (var passingShip in ships)
                {
                    if (passingShip is not TradeShip tradeShip)
                    {
                        continue;
                    }

                    ExecuteSingleAutomatedTrade(tradeShip, socialPawn, recordTrader: false, breakOnUnexecuted: true);
                }
            }
            finally
            {
                EndGroupedTrading();
            }
        }

        /// <summary>
        /// Executes an automated trade session between <paramref name="socialPawn"/> and <paramref name="trader"/>.
        /// </summary>
        /// <param name="socialPawn">The colonist negotiator performing the trade.</param>
        /// <param name="trader">The visiting trader or settlement to trade with.</param>
        public static void DoTrade(Pawn socialPawn, ITrader trader)
        {
            if (trader is not { CanTradeNow: true })
            {
                return;
            }

            var comp = Current.Game.GetComponent<TradeRulesGameComp>();
            if (comp.traders.Contains(trader))
            {
                return;
            }

            ExecuteSingleAutomatedTrade(trader, socialPawn, recordTrader: trader is ILoadReferenceable, breakOnUnexecuted: false, comp: comp);
        }

        /// <summary>
        /// Executes automated trade sessions with all untraded passing orbital trade ships on <paramref name="socialPawn"/>'s map.
        /// </summary>
        /// <param name="socialPawn">The colonist negotiator operating the comms console.</param>
        public static void DoTradeShips(Pawn socialPawn)
        {
            var comp = Current.Game.GetComponent<TradeRulesGameComp>();
            var passingShips = socialPawn.Map.passingShipManager.passingShips;
            if (passingShips == null || passingShips.Count == 0)
            {
                return;
            }

            var eligibleShips = new List<TradeShip>(passingShips.Count);
            for (int i = 0; i < passingShips.Count; i++)
            {
                if (passingShips[i] is TradeShip ship &&
                    !comp.traders.Contains(ship) &&
                    socialPawn.CanTradeWith(ship.Faction, ((ITrader)ship).TraderKind))
                {
                    eligibleShips.Add(ship);
                }
            }

            if (eligibleShips.Count == 0)
            {
                return;
            }

            try
            {
                StartGroupedTrading();

                for (int i = 0; i < eligibleShips.Count; i++)
                {
                    var ship = eligibleShips[i];
                    ExecuteSingleAutomatedTrade(ship, socialPawn, recordTrader: true, breakOnUnexecuted: false, comp: comp);
                }
            }
            finally
            {
                EndGroupedTrading();
            }
        }

        /// <summary>
        /// Evaluates active trade rules against the given <paramref name="deal"/>, populating buy/sell transfer counts
        /// and balancing against available silver.
        /// </summary>
        /// <remarks>
        /// Time Complexity:
        /// - Map stock exclusions use a <see cref="HashSet{T}"/> of colony tradeable things in O(N + T) time instead of O(N * T) list removals.
        /// - Stack splitting partitions stacks in a single pass with lazy list creation instead of multiple <c>GroupBy</c>/<c>ToDictionary</c> calls.
        /// - Budget normalization trims from the end of the order list in O(1) time per step.
        /// </remarks>
        /// <param name="deal">The active RimWorld <see cref="TradeDeal"/> to populate.</param>
        /// <param name="doBuy">Whether buy orders should be evaluated in addition to sell orders.</param>
        public static void DoTradeDeal(TradeDeal deal, bool doBuy = true)
        {
            var map = TradeSession.playerNegotiator?.Map;
            if (map == null)
            {
                return;
            }

            var autoTrade = Current.Game.GetComponent<TradeRulesGameComp>();
            if (autoTrade == null || !autoTrade.tradeRules.Any())
            {
                return;
            }

            var tradeables = deal?.AllTradeables;
            if (tradeables == null)
            {
                return;
            }

            InventoryAggregator.ClearIfNotGrouped();

            var itemCache = new List<Tradeable>(tradeables.Count);
            var initialCountsByDef = new Dictionary<ThingDef, int>();
            var colonyTradeableThings = new HashSet<Thing>();

            for (int i = 0; i < tradeables.Count; i++)
            {
                var tradeable = tradeables[i];
                if (tradeable.thingsColony != null)
                {
                    for (int c = 0; c < tradeable.thingsColony.Count; c++)
                    {
                        colonyTradeableThings.Add(tradeable.thingsColony[c]);
                    }
                }

                if (!tradeable.TraderWillTrade || tradeable.ThingDef == null)
                {
                    continue;
                }

                itemCache.Add(tradeable);
                int heldByColony = tradeable.CountHeldBy(Transactor.Colony);
                if (initialCountsByDef.TryGetValue(tradeable.ThingDef, out int existing))
                {
                    initialCountsByDef[tradeable.ThingDef] = existing + heldByColony;
                }
                else
                {
                    initialCountsByDef[tradeable.ThingDef] = heldByColony;
                }
            }

            if (itemCache.Count == 0)
            {
                return;
            }

            foreach (var (def, count) in initialCountsByDef)
            {
                InventoryAggregator.TryAddInitialCount(def, count);
            }

            // Don't buy more medicine if there's literally heaps in the Hospital already...
            // Filter in O(N) using colonyTradeableThings HashSet instead of O(N * T) List.Remove.
            var allMapThings = map.listerThings.AllThings;
            var itemsOnMap = new List<Thing>();
            var playerFaction = Faction.OfPlayer;
            var zoneManager = map.zoneManager;
            var fogGrid = map.fogGrid;

            for (int i = 0; i < allMapThings.Count; i++)
            {
                var thing = allMapThings[i];
                if (thing == null || colonyTradeableThings.Contains(thing))
                {
                    continue;
                }

                var pos = thing.Position;
                if ((!thing.IsForbidden(playerFaction) || zoneManager.ZoneAt(pos) != null) &&
                    (fogGrid == null || !fogGrid.IsFogged(pos)))
                {
                    itemsOnMap.Add(thing);
                }
            }

            var sellDictionary = new Dictionary<Tradeable, int>();
            var buyDictionary = new Dictionary<Tradeable, int>();
            var junkDesignations = BuildJunkSetForMap(map);

            for (int i = 0; i < tradeables.Count; i++)
            {
                var tradeable = tradeables[i];
                if (tradeable.thingsColony == null || tradeable.thingsColony.Count == 0)
                {
                    continue;
                }

                int junkTotal = 0;
                for (int c = 0; c < tradeable.thingsColony.Count; c++)
                {
                    var colonyThing = tradeable.thingsColony[c];
                    if (CacheUtility.IsDesignatedJunk(colonyThing, junkDesignations))
                    {
                        junkTotal += colonyThing.stackCount;
                    }
                }

                if (junkTotal > 0)
                {
                    InventoryAggregator.AddCount(null, tradeable.ThingDef, -junkTotal);
                    sellDictionary[tradeable] = junkTotal;
                }
            }

            itemCache.RemoveAll(static x =>
                (x.thingsColony == null || x.thingsColony.Count == 0) &&
                (x.thingsTrader == null || x.thingsTrader.Count == 0));

            var pairings = new Dictionary<Tradeable, TradeRule>();
            bool scanEveryStack = Mod.Settings?.scanEveryStack ?? true;

            foreach (var rule in autoTrade.tradeRules)
            {
                if (rule is not { Enabled: true } || rule.search?.Children?.queries == null || rule.search.Children.queries.Count == 0)
                {
                    continue;
                }

                var items = scanEveryStack
                    ? CollectEntriesByScanningStacks(deal.AllTradeables, itemCache, rule)
                    : CollectEntriesByFirstItem(itemCache, rule);

                ProcessRuleEntries(
                    rule,
                    items,
                    itemCache,
                    itemsOnMap,
                    doBuy,
                    InventoryAggregator,
                    OrderCalculator,
                    sellDictionary,
                    buyDictionary,
                    pairings,
                    mapItemMatcher: static (r, m) => r.search.AppliesTo(m, m.Map),
                    tradeContextMatcher: (r, entry, action) =>
                        r.search.TradeQueries.Length == 0 ||
                        r.search.AppliesTo(new TradeContext(deal, entry.Tradeable, action)));
            }

            foreach (var (tradeable, toSell) in sellDictionary)
            {
                SetTradeCount(tradeable, -toSell);
            }

            foreach (var (tradeable, toBuy) in buyDictionary)
            {
                SetTradeCount(tradeable, toBuy);
            }

            deal.UpdateCurrencyCount();
            AddRuleLabelsToTradeUI.TradeWindow?.Notify_CommonSearchChanged();

            // Buying too much: trim from the end of buyOrdersList (O(1) removal at tail)
            if (deal.CurrencyTradeable != null)
            {
                var buyOrdersList = new List<SellItem>(buyDictionary.Count);
                foreach (var (tradeable, count) in buyDictionary)
                {
                    buyOrdersList.Add(new SellItem(tradeable, count));
                }

                int colonyCurrency = deal.CurrencyTradeable.CountHeldBy(Transactor.Colony);
                while (buyOrdersList.Count > 0 &&
                       deal.CurrencyTradeable.CountToTransfer < 0 &&
                       -deal.CurrencyTradeable.CountToTransfer > colonyCurrency)
                {
                    int gap = -deal.CurrencyTradeable.CountToTransfer - colonyCurrency;
                    if (!BudgetNormalizer.NormalizeStep(buyOrdersList, pairings, gap, InventoryAggregator, SetTradeCount))
                    {
                        break;
                    }

                    deal.UpdateCurrencyCount();
                }

                // Selling too much: trim from the end of sellOrdersList (O(1) removal at tail)
                var sellOrdersList = new List<SellItem>(sellDictionary.Count);
                foreach (var (tradeable, count) in sellDictionary)
                {
                    sellOrdersList.Add(new SellItem(tradeable, count));
                }

                int traderCurrency = deal.CurrencyTradeable.CountHeldBy(Transactor.Trader);
                while (sellOrdersList.Count > 0 &&
                       deal.CurrencyTradeable.CountToTransfer > traderCurrency)
                {
                    int gap = deal.CurrencyTradeable.CountToTransfer - traderCurrency;
                    if (!BudgetNormalizer.NormalizeStep(sellOrdersList, pairings, gap, InventoryAggregator, SetTradeCount))
                    {
                        break;
                    }

                    deal.UpdateCurrencyCount();
                }
            }

            // Sell marked items first: sort in-place only when there are multiple colony stacks.
            for (int i = 0; i < deal.AllTradeables.Count; i++)
            {
                SortColonyThingsJunkFirst(deal.AllTradeables[i].thingsColony, junkDesignations);
            }
        }

        /// <summary>
        /// Evaluates a single <paramref name="rule"/> against its matched <paramref name="items"/>, computing sell and buy
        /// quantities, updating <paramref name="aggregator"/> stock counts, removing claimed tradeables from
        /// <paramref name="itemCache"/>, and cleaning up unused UI rule labels.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(E + M) where E is the number of matched trade entries and M is the number of non-tradeable map items.
        /// Space Complexity: O(E) for sell and buy order buffers.
        /// </remarks>
        internal static void ProcessRuleEntries(
            TradeRule rule,
            List<TradeEntry> items,
            List<Tradeable> itemCache,
            IReadOnlyList<Thing> itemsOnMap,
            bool doBuy,
            ITradeInventoryAggregator aggregator,
            ITradeOrderCalculator calculator,
            Dictionary<Tradeable, int> sellDictionary,
            Dictionary<Tradeable, int> buyDictionary,
            Dictionary<Tradeable, TradeRule> pairings,
            Func<TradeRule, Thing, bool> mapItemMatcher = null,
            Func<TradeRule, TradeEntry, TradeAction, bool> tradeContextMatcher = null)
        {
            if (rule == null || items == null)
            {
                return;
            }

            mapItemMatcher ??= static (r, m) => r.search != null && r.search.AppliesTo(m, m.Map);
            tradeContextMatcher ??= static (_, _, _) => true;

            if (rule.Aggregation == TradeRuleAggregation.Rule)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var entry = items[i];
                    aggregator.AddCount(rule, entry.ThingDef, entry.ColonyCount);
                }
            }

            var claimedTradeables = new HashSet<Tradeable>();
            var sellOrders = new List<(Tradeable Tradeable, int Count)>();

            if (rule.AllowSell)
            {
                int writeIdx = 0;

                for (int i = 0; i < items.Count; i++)
                {
                    var entry = items[i];
                    int currentStock = aggregator.GetCount(rule, entry.ThingDef);
                    if (currentStock > rule.Export && tradeContextMatcher(rule, entry, TradeAction.PlayerSells))
                    {
                        int sellQty = calculator.CalculateSellQuantity(currentStock, rule.Export, entry.ColonyCount);
                        aggregator.AddCount(rule, entry.ThingDef, -sellQty);
                        sellOrders.Add((entry.Tradeable, sellQty));
                        claimedTradeables.Add(entry.Tradeable);
                        pairings?.TryAdd(entry.Tradeable, rule);
                    }
                    else
                    {
                        items[writeIdx++] = entry;
                    }
                }

                if (writeIdx < items.Count)
                {
                    items.RemoveRange(writeIdx, items.Count - writeIdx);
                }

                if (claimedTradeables.Count > 0 && itemCache != null)
                {
                    itemCache.RemoveAll(claimedTradeables.Contains);
                }
            }

            for (int i = 0; i < sellOrders.Count; i++)
            {
                var (tradeable, count) = sellOrders[i];
                if (!sellDictionary.TryAdd(tradeable, count))
                {
                    sellDictionary[tradeable] += count;
                }
            }

            // Single-pass stock addition for extra items on the map (no GroupBy allocation needed).
            if (itemsOnMap != null)
            {
                for (int i = 0; i < itemsOnMap.Count; i++)
                {
                    var mapItem = itemsOnMap[i];
                    if (mapItem != null && mapItemMatcher(rule, mapItem))
                    {
                        aggregator.AddCount(rule, mapItem.def, mapItem.stackCount);
                    }
                }
            }

            var buyOrders = new List<(Tradeable Tradeable, int Count)>();
            if (doBuy && rule.AllowBuy)
            {
                claimedTradeables.Clear();

                for (int i = 0; i < items.Count; i++)
                {
                    var entry = items[i];
                    int currentStock = aggregator.GetCount(rule, entry.ThingDef);
                    if (currentStock < rule.Import && tradeContextMatcher(rule, entry, TradeAction.PlayerBuys))
                    {
                        int buyQty = calculator.CalculateBuyQuantity(currentStock, rule.Import, entry.TraderCount);
                        aggregator.AddCount(rule, entry.ThingDef, buyQty);
                        buyOrders.Add((entry.Tradeable, buyQty));
                        claimedTradeables.Add(entry.Tradeable);
                    }
                }

                if (claimedTradeables.Count > 0 && itemCache != null)
                {
                    itemCache.RemoveAll(claimedTradeables.Contains);
                }
            }

            for (int i = 0; i < buyOrders.Count; i++)
            {
                var (tradeable, count) = buyOrders[i];
                if (!buyDictionary.TryAdd(tradeable, count))
                {
                    buyDictionary[tradeable] += count;
                }
            }

            // Remove rule labels from entries that resulted in neither a sell nor a buy order.
            for (int i = 0; i < items.Count; i++)
            {
                var candidate = items[i].Tradeable;
                bool inSell = false;
                for (int s = 0; s < sellOrders.Count; s++)
                {
                    if (sellOrders[s].Tradeable == candidate)
                    {
                        inSell = true;
                        break;
                    }
                }

                if (inSell)
                {
                    continue;
                }

                bool inBuy = false;
                for (int b = 0; b < buyOrders.Count; b++)
                {
                    if (buyOrders[b].Tradeable == candidate)
                    {
                        inBuy = true;
                        break;
                    }
                }

                if (!inBuy)
                {
                    AddRuleLabelsToTradeUI.ExtraLabels.Remove(candidate);
                }
            }
        }

        /// <summary>
        /// Sorts <paramref name="colonyList"/> in-place so that items designated as junk appear before non-junk items.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(C log C) where C is the number of stacks in <paramref name="colonyList"/> (or O(1) when C &lt;= 1).
        /// Space Complexity: O(1).
        /// </remarks>
        internal static void SortColonyThingsJunkFirst(List<Thing> colonyList, HashSet<Thing> junkDesignations)
        {
            if (colonyList is not { Count: > 1 })
            {
                return;
            }

            colonyList.Sort((a, b) =>
            {
                bool aNotJunk = !CacheUtility.IsDesignatedJunk(a, junkDesignations);
                bool bNotJunk = !CacheUtility.IsDesignatedJunk(b, junkDesignations);
                return aNotJunk.CompareTo(bNotJunk);
            });
        }

        internal static List<TradeEntry> CollectEntriesByScanningStacks(
            List<Tradeable> allTradeables,
            IReadOnlyList<Tradeable> itemCache,
            TradeRule rule,
            Func<Thing, bool> stackMatcher = null)
        {
            var items = new List<TradeEntry>();
            if (itemCache == null || rule == null)
            {
                return items;
            }

            stackMatcher ??= stack => rule.search.AppliesTo(stack, stack.Map);
            string ruleTag = null;
            string ruleName = rule.search?.name ?? string.Empty;

            for (int i = 0; i < itemCache.Count; i++)
            {
                var tradeable = itemCache[i];
                if (tradeable == null || tradeable.IsCurrency || AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(tradeable))
                {
                    continue;
                }

                PartitionStacks(tradeable.thingsColony, stackMatcher, out var matchedColonyStacks, out var unmatchedColonyStacks);
                PartitionStacks(tradeable.thingsTrader, stackMatcher, out var matchedTraderStacks, out var unmatchedTraderStacks);

                bool matchedColony = matchedColonyStacks is { Count: > 0 };
                bool unmatchedColony = unmatchedColonyStacks is { Count: > 0 };
                bool matchedTrader = matchedTraderStacks is { Count: > 0 };
                bool unmatchedTrader = unmatchedTraderStacks is { Count: > 0 };

                // No match
                if (!matchedColony && !matchedTrader)
                {
                    continue;
                }

                // Claim full Tradeable
                if (!unmatchedColony && !unmatchedTrader)
                {
                    ruleTag ??= $"<i><color=#{Gray}> - ({ruleName})</color></i>";
                    AddRuleLabelsToTradeUI.ExtraLabels[tradeable] = ruleTag;
                    items.Add(new TradeEntry(
                        tradeable,
                        tradeable.ThingDef,
                        tradeable.CountHeldBy(Transactor.Colony),
                        tradeable.CountHeldBy(Transactor.Trader)));
                }
                // Split Tradeable into matched and unmatched portions
                else
                {
                    var claimedTradeable = new Tradeable
                    {
                        thingsColony = matchedColonyStacks ?? [],
                        thingsTrader = matchedTraderStacks ?? []
                    };

                    tradeable.thingsColony = unmatchedColonyStacks ?? [];
                    tradeable.thingsTrader = unmatchedTraderStacks ?? [];

                    if (!claimedTradeable.HasAnyThing || !tradeable.HasAnyThing)
                    {
                        throw new InvalidOperationException("Empty tradeable produced while splitting stacks.");
                    }

                    ruleTag ??= $"<i><color=#{Gray}> - ({ruleName})</color></i>";
                    AddRuleLabelsToTradeUI.ExtraLabels[claimedTradeable] = ruleTag;

                    allTradeables?.Add(claimedTradeable);
                    items.Add(new TradeEntry(
                        claimedTradeable,
                        claimedTradeable.ThingDef,
                        claimedTradeable.CountHeldBy(Transactor.Colony),
                        claimedTradeable.CountHeldBy(Transactor.Trader)));
                }
            }

            return items;
        }

        internal static List<TradeEntry> CollectEntriesByFirstItem(
            IReadOnlyList<Tradeable> itemCache,
            TradeRule rule,
            HashSet<Thing> junkSet = null,
            Func<Thing, bool> itemMatcher = null)
        {
            var items = new List<TradeEntry>();
            if (itemCache == null || rule == null)
            {
                return items;
            }

            itemMatcher ??= thing => rule.search.AppliesTo(thing);

            for (int i = 0; i < itemCache.Count; i++)
            {
                var tradeable = itemCache[i];
                if (tradeable != null &&
                    !tradeable.IsCurrency &&
                    tradeable.AnyThingNotJunk(out var thing, junkSet) &&
                    itemMatcher(thing))
                {
                    items.Add(new TradeEntry(
                        tradeable,
                        tradeable.ThingDef,
                        tradeable.CountHeldBy(Transactor.Colony),
                        tradeable.CountHeldBy(Transactor.Trader)));
                }
            }

            return items;
        }

        /// <summary>
        /// Partitions a list of <see cref="Thing"/> stacks into matching and non-matching lists in a single O(S) pass,
        /// allocating lists lazily only when elements exist for that partition.
        /// </summary>
        internal static void PartitionStacks(
            IReadOnlyList<Thing> source,
            Func<Thing, bool> predicate,
            out List<Thing> matched,
            out List<Thing> unmatched)
        {
            matched = null;
            unmatched = null;
            if (source == null || source.Count == 0 || predicate == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                var stack = source[i];
                if (predicate(stack))
                {
                    matched ??= [];
                    matched.Add(stack);
                }
                else
                {
                    unmatched ??= [];
                    unmatched.Add(stack);
                }
            }
        }

        private static HashSet<Thing> BuildJunkSetForMap(Map map)
        {
            var def = MGDesignatorDefOf.MGAutoSell;
            var designations = def != null ? map?.designationManager?.SpawnedDesignationsOfDef(def) : null;
            if (designations == null)
            {
                return null;
            }

            var set = new HashSet<Thing>();
            foreach (var designation in designations)
            {
                if (designation.target.HasThing)
                {
                    set.Add(designation.target.Thing);
                }
            }

            return set;
        }

        private static void ExecuteSingleAutomatedTrade(
            ITrader trader,
            Pawn socialPawn,
            bool recordTrader,
            bool breakOnUnexecuted,
            TradeRulesGameComp comp = null)
        {
            TradeSession.SetupWith(trader, socialPawn, false);
            var deal = TradeSession.deal;
            DoTradeDeal(deal);

            if (recordTrader && comp != null)
            {
                comp.traders.Add(trader);
            }

            int silver = deal.CurrencyTradeable.CountToTransfer;
            ExtractExecutedTransfers(deal.AllTradeables, out var buy, out var sell);

            if (buy.Count == 0 && sell.Count == 0)
            {
                return;
            }

            if (!deal.TryExecute(out bool actuallyTraded))
            {
                return;
            }

            if (breakOnUnexecuted && !actuallyTraded)
            {
                Debugger.Break();
            }

            DoLetter(trader, socialPawn, deal, socialPawn.Position, buy, sell, silver);
        }

        internal static void ExtractExecutedTransfers(
            IReadOnlyList<Tradeable> allTradeables,
            out List<(string label, int count)> buy,
            out List<(string label, int count)> sell)
        {
            buy = [];
            sell = [];
            if (allTradeables == null)
            {
                return;
            }

            for (int i = 0; i < allTradeables.Count; i++)
            {
                var tradeable = allTradeables[i];
                if (tradeable == null || tradeable.IsCurrency || tradeable.CountToTransfer == 0)
                {
                    continue;
                }

                string label = tradeable.ThingDef?.label ?? tradeable.ThingDef?.defName ?? string.Empty;
                if (tradeable.CountToTransfer > 0)
                {
                    buy.Add((label, tradeable.CountToTransfer));
                }
                else
                {
                    sell.Add((label, tradeable.CountToTransfer));
                }
            }
        }

        internal static void SetTradeCount(Tradeable tradeable, int count)
        {
            PropertySetter.Invoke(tradeable, [count]);
        }

        /// <summary>
        /// Dispatches an in-game notification letter summarizing items bought, items sold, and net silver transferred.
        /// </summary>
        /// <param name="trader">The trader involved in the transaction.</param>
        /// <param name="pawn">The colonist negotiator who completed the trade.</param>
        /// <param name="deal">The executed <see cref="TradeDeal"/>.</param>
        /// <param name="location">The map cell location for the letter target.</param>
        /// <param name="buy">List of purchased item labels and quantities.</param>
        /// <param name="sell">List of sold item labels and quantities.</param>
        /// <param name="silver">Net currency transfer count.</param>
        public static void DoLetter(
            ITrader trader,
            Pawn pawn,
            TradeDeal deal,
            IntVec3 location,
            List<(string label, int count)> buy,
            List<(string label, int count)> sell,
            int silver)
        {
            if (buy.Count == 0 && sell.Count == 0)
            {
                return;
            }

            string word = silver > 0 ? Earned : Spent;
            float absSilver = Math.Abs(silver);

            string buySection = string.Empty;
            if (buy.Count > 0)
            {
                var buyStringBuilder = new StringBuilder();
                buyStringBuilder.AppendLine(Bought.Colorize(ColoredText.TipSectionTitleColor));
                buyStringBuilder.AppendJoin("\n", buy.Select(static x => $"  {x.label} x{Math.Abs(x.count)}"));
                buySection = buyStringBuilder.ToString();
            }

            string sellSection = string.Empty;
            if (sell.Count > 0)
            {
                var sellStringBuilder = new StringBuilder();
                sellStringBuilder.AppendLine(Sold.Colorize(ColoredText.TipSectionTitleColor));
                sellStringBuilder.AppendJoin("\n", sell.Select(static x => $"  {x.label} x{Math.Abs(x.count)}"));
                sellSection = sellStringBuilder.ToString();
            }

            string pawnName = pawn.Name.ToStringShort.Colorize(ColoredText.NameColor);
            var traderColor = (trader?.Faction?.PlayerRelationKind ?? FactionRelationKind.Neutral).GetColor();
            string traderName = trader?.TraderName?.Colorize(traderColor) ?? "someone".Colorize(Color.magenta);
            string silverLabel = absSilver.ToStringMoney().Colorize(ColoredText.CurrencyColor);

            var body = "MGAutoSell.Letter".Translate(
                pawnName,
                traderName,
                silverLabel,
                word,
                buySection,
                sellSection);

            var globalTargetInfo = new GlobalTargetInfo(location, pawn.Map);

            Find.LetterStack.ReceiveLetter(
                "MGAutoSell.LetterTitle".Translate(traderName),
                body,
                LetterDefOf.PositiveEvent,
                globalTargetInfo);
        }
    }
}
