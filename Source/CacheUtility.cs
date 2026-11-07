using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JetBrains.Annotations;
using MGAutoSell.Filter;
using MGAutoSell.Processing;
using MGAutoSell.Records;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Stores execution duration measurements for each phase of the map trade cache pipeline.
    /// </summary>
    /// <param name="AllItems">Time spent scanning the map for haulable items and sellable colony pawns.</param>
    /// <param name="Junk">Time spent partitioning items designated as junk.</param>
    /// <param name="Sell">Time spent matching map items against active trade rules.</param>
    /// <param name="Traders">Time spent discovering and filtering available colony negotiators.</param>
    /// <param name="SellEntries">Time spent calculating sell prices and quantities per <see cref="ThingDef"/>.</param>
    /// <param name="PossibleItems">Time spent resolving potential buyable <see cref="ThingDef"/> entries.</param>
    /// <param name="Quantity">Time spent aggregating item counts per rule.</param>
    /// <param name="BuildCache">Time spent assembling the final <see cref="ItemsToSell"/> record.</param>
    /// <param name="Total">Total cumulative execution time across all phases.</param>
    public record BenchmarkResults(
        ItemAndLabel<long> AllItems,
        ItemAndLabel<long> Junk,
        ItemAndLabel<long> Sell,
        ItemAndLabel<long> Traders,
        ItemAndLabel<long> SellEntries,
        ItemAndLabel<long> PossibleItems,
        ItemAndLabel<long> Quantity,
        ItemAndLabel<long> BuildCache,
        ItemAndLabel<long> Total);

    /// <summary>
    /// Provides high-performance map item scanning, rule matching, sell valuation, and cache construction.
    /// </summary>
    public static class CacheUtility
    {
        internal static List<Thing> itemCache;

        /// <summary>
        /// Gets or sets the <see cref="ITradeOrderCalculator"/> used to compute sell valuations and rule count ranges.
        /// </summary>
        public static ITradeOrderCalculator OrderCalculator { get; set; } = TradeOrderCalculator.Instance;

        /// <summary>
        /// Extracts all items designated with <see cref="MGDesignatorDefOf.MGAutoSell"/> from <paramref name="allItems"/>
        /// in a single O(N) pass and groups them by <see cref="Thing.def"/>.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N + D) where N is the number of items in <paramref name="allItems"/> and D is the number
        /// of active junk designations on the map. Uses two-pointer in-place list compaction instead of repeated
        /// <see cref="List{T}.Remove"/> calls (which previously caused O(N * J) quadratic complexity).
        /// Space Complexity: O(J) where J is the number of junk items.
        /// </remarks>
        /// <param name="allItems">The list of map items to partition in-place.</param>
        /// <param name="designatedSet">Optional precomputed set of junk-designated items; if <c>null</c>, queries the map's designation manager.</param>
        /// <returns>A list of groupings of junk items keyed by <see cref="ThingDef"/>.</returns>
        public static List<IGrouping<ThingDef, Thing>> GetJunk(this List<Thing> allItems, HashSet<Thing> designatedSet = null)
        {
            if (allItems == null || allItems.Count == 0)
            {
                return [];
            }

            var junkItems = ExtractJunkItemsInPlace(allItems, designatedSet);
            return junkItems.Count == 0 ? [] : junkItems.GroupBy(static x => x.def).ToList();
        }

        /// <summary>
        /// Extracts all junk-designated items from <paramref name="allItems"/> in-place and adds them directly into
        /// <paramref name="thingDictionary"/> without intermediate LINQ grouping allocations.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N + D) single pass. Space Complexity: O(J) for stored item references.
        /// </remarks>
        /// <param name="allItems">The list of map items to compact in-place.</param>
        /// <param name="thingDictionary">The destination dictionary mapping each <see cref="ThingDef"/> to its sellable items.</param>
        /// <param name="designatedSet">Optional precomputed set of junk-designated items; if <c>null</c>, queries the map's designation manager.</param>
        public static void ExtractJunkInto(
            this List<Thing> allItems,
            Dictionary<ThingDef, List<Thing>> thingDictionary,
            HashSet<Thing> designatedSet = null)
        {
            if (allItems == null || allItems.Count == 0 || thingDictionary == null)
            {
                return;
            }

            designatedSet ??= BuildJunkDesignationSet(allItems);
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < allItems.Count; readIndex++)
            {
                var item = allItems[readIndex];
                if (IsDesignatedJunk(item, designatedSet))
                {
                    if (!thingDictionary.TryGetValue(item.def, out var list))
                    {
                        list = [];
                        thingDictionary[item.def] = list;
                    }

                    list.Add(item);
                }
                else
                {
                    allItems[writeIndex++] = item;
                }
            }

            if (writeIndex < allItems.Count)
            {
                allItems.RemoveRange(writeIndex, allItems.Count - writeIndex);
            }
        }

        /// <summary>
        /// Evaluates enabled <paramref name="rules"/> against <paramref name="allItems"/>, populating matched items
        /// per rule and aggregating sellable items by <see cref="ThingDef"/>.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(R * N) where R is the number of active rules and N is the number of items in <paramref name="allItems"/>.
        /// Uses in-place two-pointer list compaction instead of repeated <see cref="List{T}.Remove"/> calls (previously O(R * N^2)),
        /// and inserts directly into <paramref name="thingDictionary"/> without intermediate <c>GroupBy</c> or <c>ToDictionary</c> allocations.
        /// Space Complexity: O(M) where M is the number of matched items.
        /// </remarks>
        /// <param name="allItems">The list of map items to evaluate and compact in-place as sell rules claim items.</param>
        /// <param name="rules">The trade rules group to evaluate in priority order.</param>
        /// <param name="thingDictionary">Dictionary accumulating sellable items grouped by <see cref="ThingDef"/>.</param>
        /// <param name="ruleDictionary">Dictionary accumulating all matched items per <see cref="TradeRule"/>.</param>
        /// <returns>A dictionary mapping each claimed <see cref="ThingDef"/> to its export threshold.</returns>
        public static Dictionary<ThingDef, int> DoRules(
            this List<Thing> allItems,
            TradeRulesGroup rules,
            ref Dictionary<ThingDef, List<Thing>> thingDictionary,
            ref Dictionary<TradeRule, List<Thing>> ruleDictionary)
        {
            return DoRules(allItems, (IEnumerable<TradeRule>)rules, ref thingDictionary, ref ruleDictionary);
        }

        /// <summary>
        /// Evaluates enabled <paramref name="rules"/> against <paramref name="allItems"/>, populating matched items
        /// per rule and aggregating sellable items by <see cref="ThingDef"/>.
        /// </summary>
        /// <param name="allItems">The list of map items to evaluate and compact in-place as sell rules claim items.</param>
        /// <param name="rules">The sequence of trade rules to evaluate in priority order.</param>
        /// <param name="thingDictionary">Dictionary accumulating sellable items grouped by <see cref="ThingDef"/>.</param>
        /// <param name="ruleDictionary">Dictionary accumulating all matched items per <see cref="TradeRule"/>.</param>
        /// <param name="itemMatcher">Optional predicate to evaluate whether a <see cref="TradeRule"/> matches a <see cref="Thing"/>; defaults to <see cref="QuerySearch.AppliesTo(Thing, Map)"/>.</param>
        /// <param name="isSellable">Optional predicate to evaluate whether a <see cref="ThingDef"/> is player-sellable; defaults to <see cref="TradeUtility.EverPlayerSellable"/>.</param>
        /// <returns>A dictionary mapping each claimed <see cref="ThingDef"/> to its export threshold.</returns>
        public static Dictionary<ThingDef, int> DoRules(
            this List<Thing> allItems,
            IEnumerable<TradeRule> rules,
            ref Dictionary<ThingDef, List<Thing>> thingDictionary,
            ref Dictionary<TradeRule, List<Thing>> ruleDictionary,
            Func<TradeRule, Thing, bool> itemMatcher = null,
            Func<ThingDef, bool> isSellable = null)
        {
            var sellDictionary = new Dictionary<ThingDef, int>();
            if (allItems == null || allItems.Count == 0 || rules == null)
            {
                return sellDictionary;
            }

            thingDictionary ??= new Dictionary<ThingDef, List<Thing>>();
            ruleDictionary ??= new Dictionary<TradeRule, List<Thing>>();
            isSellable ??= TradeUtility.EverPlayerSellable;

            foreach (var rule in rules)
            {
                if (rule is not { Enabled: true })
                {
                    continue;
                }

                if (itemMatcher == null && (rule.search?.Children?.queries == null || rule.search.Children.queries.Count == 0))
                {
                    continue;
                }

                List<Thing> matchedForRule = null;
                bool allowSell = rule.AllowSell;
                int writeIndex = 0;

                for (int readIndex = 0; readIndex < allItems.Count; readIndex++)
                {
                    var item = allItems[readIndex];
                    bool matches = itemMatcher != null ? itemMatcher(rule, item) : rule.search.AppliesTo(item);
                    if (!matches)
                    {
                        if (allowSell)
                        {
                            allItems[writeIndex++] = item;
                        }

                        continue;
                    }

                    matchedForRule ??= [];
                    matchedForRule.Add(item);

                    if (!allowSell)
                    {
                        continue;
                    }

                    if (!isSellable(item.def))
                    {
                        // Non-sellable items remain in allItems and are excluded from the rule's sell list.
                        matchedForRule.RemoveAt(matchedForRule.Count - 1);
                        allItems[writeIndex++] = item;
                        continue;
                    }

                    if (!thingDictionary.TryGetValue(item.def, out var defItems))
                    {
                        defItems = [];
                        thingDictionary[item.def] = defItems;
                    }

                    defItems.Add(item);
                    sellDictionary.TryAdd(item.def, rule.Export);
                }

                if (matchedForRule is { Count: > 0 })
                {
                    ruleDictionary[rule] = matchedForRule;
                }

                if (allowSell && writeIndex < allItems.Count)
                {
                    allItems.RemoveRange(writeIndex, allItems.Count - writeIndex);
                }
            }

            return sellDictionary;
        }

        /// <summary>
        /// Computes the sorted list of <see cref="SellRecord"/> entries from the aggregated <paramref name="thingDictionary"/>
        /// and <paramref name="sellDictionary"/> thresholds.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N + K log K) where N is the total number of sellable item stacks and K is the number of
        /// distinct <see cref="ThingDef"/> keys. Sums price and stack counts in a single pass per <see cref="ThingDef"/>
        /// and sorts the resulting list in-place.
        /// Space Complexity: O(K) for the output list.
        /// </remarks>
        /// <param name="sellDictionary">Mapping of <see cref="ThingDef"/> to export retention threshold.</param>
        /// <param name="socialPawn">The negotiator pawn used to compute trade price improvements, or <c>null</c>.</param>
        /// <param name="thingDictionary">Dictionary of items grouped by <see cref="ThingDef"/>.</param>
        /// <param name="unitPriceEvaluator">Optional delegate to compute the sell unit price of a <see cref="Thing"/>; if <c>null</c>, uses <see cref="TradeUtility.GetPricePlayerSell"/>.</param>
        /// <param name="moneyFormatter">Optional delegate to format currency amounts; if <c>null</c>, uses <see cref="GenText.ToStringMoney"/>.</param>
        /// <returns>A list of <see cref="SellRecord"/> sorted by descending total value.</returns>
        public static List<SellRecord> GetEntries(
            this Dictionary<ThingDef, int> sellDictionary,
            Pawn socialPawn,
            ref Dictionary<ThingDef, List<Thing>> thingDictionary,
            Func<Thing, float> unitPriceEvaluator = null,
            Func<float, string> moneyFormatter = null)
        {
            if (thingDictionary == null || thingDictionary.Count == 0)
            {
                return [];
            }

            float traderPriceType = 1f;
            float playerNegotiator = 0.1f;
            const float settlement = 0f;
            float drugBonusRaw = 0f;
            bool ideologyActive = false;
            float animalProduceBonusRaw = 0f;

            if (unitPriceEvaluator == null)
            {
                traderPriceType = PriceType.Normal.PriceMultiplier();
                playerNegotiator = socialPawn?.GetStatValue(StatDefOf.TradePriceImprovement) ?? 0.1f;
                drugBonusRaw = socialPawn?.GetStatValue(StatDefOf.DrugSellPriceImprovement) ?? 0f;
                ideologyActive = ModsConfig.IdeologyActive;
                animalProduceBonusRaw = ideologyActive && socialPawn != null
                    ? socialPawn.GetStatValue(StatDefOf.AnimalProductsSellImprovement)
                    : 0f;
            }

            moneyFormatter ??= static v => v.ToStringMoney();

            var sellEntries = new List<SellRecord>(thingDictionary.Count);
            var emptyKeys = new List<ThingDef>();

            foreach (var (thingDef, items) in thingDictionary)
            {
                if (items == null || items.Count == 0)
                {
                    emptyKeys.Add(thingDef);
                    continue;
                }

                float priceTotal = 0f;
                int itemsTotal = 0;

                if (unitPriceEvaluator != null)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        int stackCount = item.stackCount;
                        priceTotal += unitPriceEvaluator(item) * stackCount;
                        itemsTotal += stackCount;
                    }
                }
                else
                {
                    float drugBonus = thingDef.IsNonMedicalDrug ? drugBonusRaw : 0f;
                    float animalProduceBonus = (thingDef.IsLeather || thingDef.IsMeat || thingDef.IsWool)
                        ? animalProduceBonusRaw
                        : 0f;
                    float humanPawn = ideologyActive && items[0] is Pawn pawn && pawn.RaceProps.Humanlike
                        ? 0.6f
                        : 1f;

                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        int stackCount = item.stackCount;
                        priceTotal += TradeUtility.GetPricePlayerSell(
                            item,
                            traderPriceType,
                            humanPawn,
                            playerNegotiator,
                            settlement,
                            drugBonus,
                            animalProduceBonus) * stackCount;
                        itemsTotal += stackCount;
                    }
                }

                int sellDown = sellDictionary != null && sellDictionary.TryGetValue(thingDef, out int exportLimit)
                    ? exportLimit
                    : 0;

                if (!OrderCalculator.TryCalculateSellValuation(
                        priceTotal,
                        itemsTotal,
                        sellDown,
                        out int sellableCount,
                        out float roundedTotal,
                        out float unitPrice))
                {
                    continue;
                }

                var total = new ItemAndLabel<float>(roundedTotal, moneyFormatter(roundedTotal));
                var pricePerItem = new ItemAndLabel<float>(unitPrice, moneyFormatter(unitPrice));
                sellEntries.Add(new SellRecord(thingDef, sellableCount, total, pricePerItem));
            }

            for (int i = 0; i < emptyKeys.Count; i++)
            {
                thingDictionary.Remove(emptyKeys[i]);
            }

            sellEntries.Sort(static (a, b) => b.Total.Value.CompareTo(a.Total.Value));
            return sellEntries;
        }

        /// <summary>
        /// Computes rule usage statistics (grouped counts and min/max ranges) for each rule in <paramref name="ruleDictionary"/>.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(M) single pass across all matched items M, avoiding intermediate <c>ToList()</c> per group
        /// and avoiding post-construction removal of empty rules.
        /// Space Complexity: O(R + G) where R is active rules and G is distinct <see cref="ThingDef"/> groups per rule.
        /// </remarks>
        /// <param name="ruleDictionary">Dictionary mapping each <see cref="TradeRule"/> to its matched map items.</param>
        /// <returns>A dictionary mapping each non-empty <see cref="TradeRule"/> to its <see cref="RuleUsage"/> summary.</returns>
        public static Dictionary<TradeRule, RuleUsage> GetRuleCounts(
            this Dictionary<TradeRule, List<Thing>> ruleDictionary)
        {
            if (ruleDictionary == null || ruleDictionary.Count == 0)
            {
                return [];
            }

            if (Mod.Settings is { showQuantityInsteadOfLabel: false, colorRuleCountsOnWork: false })
            {
                return [];
            }

            var result = new Dictionary<TradeRule, RuleUsage>(ruleDictionary.Count);
            var countsByDef = new Dictionary<ThingDef, int>();

            foreach (var (rule, items) in ruleDictionary)
            {
                if (items == null || items.Count == 0)
                {
                    continue;
                }

                countsByDef.Clear();
                int totalCount = 0;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    int stackCount = item.stackCount;
                    totalCount += stackCount;

                    if (countsByDef.TryGetValue(item.def, out int existing))
                    {
                        countsByDef[item.def] = existing + stackCount;
                    }
                    else
                    {
                        countsByDef[item.def] = stackCount;
                    }
                }

                if (totalCount <= 0)
                {
                    continue;
                }

                var grouped = new List<RuleRecord>(countsByDef.Count);
                foreach (var (def, count) in countsByDef)
                {
                    grouped.Add(new RuleRecord(def, count));
                }

                var (min, max) = OrderCalculator.CalculateRuleRange(rule.Aggregation, grouped, totalCount);
                if (max <= 0)
                {
                    continue;
                }

                var range = (new ItemAndLabel<int>(min, "x" + min), new ItemAndLabel<int>(max, "x" + max));
                result[rule] = new RuleUsage(grouped, range);
            }

            return result;
        }

        /// <summary>
        /// Resolves the list of potential items matched by buy-enabled rules that are not already present in <paramref name="sellEntries"/>.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(P + S) where P is the total number of potential items across rules and S is the number
        /// of entries in <paramref name="sellEntries"/>. Uses a <see cref="HashSet{T}"/> of sold <see cref="ThingDef"/>s
        /// instead of an O(P * S) nested <c>RemoveAll(x =&gt; sellEntries.Any(...))</c> scan.
        /// Space Complexity: O(P + S).
        /// </remarks>
        /// <param name="rules">The list of configured trade rules.</param>
        /// <param name="sellEntries">The list of items already queued for sale.</param>
        /// <returns>A deduplicated list of <see cref="PotentialItem"/> entries.</returns>
        public static List<PotentialItem> GetPossibleItemsList(this List<TradeRule> rules, List<SellRecord> sellEntries)
        {
            if (rules == null || rules.Count == 0 || Mod.Settings is { showAllMatchingItems: false })
            {
                return [];
            }

            itemCache ??= GenerateItemCache();

            var soldDefs = new HashSet<ThingDef>(sellEntries?.Count ?? 0);
            if (sellEntries != null)
            {
                for (int i = 0; i < sellEntries.Count; i++)
                {
                    soldDefs.Add(sellEntries[i].Item);
                }
            }

            var seen = new HashSet<PotentialItem>();
            var potentialItems = new List<PotentialItem>();

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule is not { Enabled: true, AllowBuy: true } || rule.search == null)
                {
                    continue;
                }

                var possibleDefs = rule.search.AllItems ??= rule.search.GetPossibleItems();
                if (possibleDefs.Count == 0)
                {
                    continue;
                }

                string formattedRuleName = $"<i>({rule.search.name})</i>";
                for (int j = 0; j < possibleDefs.Count; j++)
                {
                    var def = possibleDefs[j];
                    if (soldDefs.Contains(def))
                    {
                        continue;
                    }

                    var candidate = new PotentialItem(def, formattedRuleName);
                    if (seen.Add(candidate))
                    {
                        potentialItems.Add(candidate);
                    }
                }
            }

            return potentialItems;
        }

        /// <summary>
        /// Evaluates all cached tradeable <see cref="Thing"/> prototypes against <paramref name="search"/> to determine
        /// which <see cref="ThingDef"/>s can match the query.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(C) single pass over <see cref="itemCache"/> with in-place compaction when faulty items are removed.
        /// Space Complexity: O(M) where M is the number of matching <see cref="ThingDef"/>s.
        /// </remarks>
        /// <param name="search">The query search filter to evaluate.</param>
        /// <returns>A list of matching <see cref="ThingDef"/> definitions.</returns>
        public static List<ThingDef> GetPossibleItems(this QuerySearch search)
        {
            itemCache ??= GenerateItemCache();

            if (search?.Children?.queries == null || !search.Children.queries.Any(static x => x.Enabled))
            {
                return [];
            }

            var possibleItems = new List<ThingDef>();
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < itemCache.Count; readIndex++)
            {
                var candidate = itemCache[readIndex];
                try
                {
                    if (search.AppliesTo(candidate))
                    {
                        possibleItems.Add(candidate.def);
                    }

                    itemCache[writeIndex++] = candidate;
                }
                catch
                {
                    Log.Warning(
                        $"An error occured while matching ThingDef {candidate.def.defName} for a rule, removing it from the list.\nIt's possible this item has a complex creation process, hence the error.");
                }
            }

            if (writeIndex < itemCache.Count)
            {
                itemCache.RemoveRange(writeIndex, itemCache.Count - writeIndex);
            }

            return possibleItems;
        }

        /// <summary>
        /// Generates a prototype <see cref="Thing"/> instance for every tradeable <see cref="ThingDef"/> with a valid UI icon.
        /// </summary>
        /// <returns>A list of prototype <see cref="Thing"/> instances used for query evaluation.</returns>
        public static List<Thing> GenerateItemCache()
        {
            var traders = DefDatabase<TraderKindDef>.AllDefsListForReading;
            var allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            var result = new List<Thing>();

            for (int i = 0; i < allDefs.Count; i++)
            {
                var def = allDefs[i];
                if (def.uiIcon == null)
                {
                    continue;
                }

                bool anyTraderWillTrade = false;
                for (int t = 0; t < traders.Count; t++)
                {
                    if (traders[t].WillTrade(def))
                    {
                        anyTraderWillTrade = true;
                        break;
                    }
                }

                if (!anyTraderWillTrade)
                {
                    continue;
                }

                var instance = def.race != null ? TryGenPawn(def) : TryGenThing(def);
                if (instance != null)
                {
                    result.Add(instance);
                }
            }

            return result;
        }

        [CanBeNull]
        private static Pawn TryGenPawn(ThingDef thing)
        {
            try
            {
                if (thing.race?.AnyPawnKind == null)
                {
                    return null;
                }

                return PawnGenerator.GeneratePawn(thing.race.AnyPawnKind);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to PawnGen '{thing.defName}' def\n{ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        [CanBeNull]
        private static Thing TryGenThing(ThingDef thing)
        {
            try
            {
                return ThingMaker.MakeThing(thing, thing.MadeFromStuff ? GenStuff.DefaultStuffFor(thing) : null);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to ThingGen '{thing.defName}' def\n{ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Builds the complete <see cref="ItemsToSell"/> cache for the specified <paramref name="map"/> and
        /// optionally records per-stage benchmark timings.
        /// </summary>
        /// <param name="comp">The game component holding the configured trade rules and trader settings.</param>
        /// <param name="map">The map whose inventory should be scanned.</param>
        /// <param name="benchmark">When this method returns, contains the recorded benchmark timings.</param>
        /// <param name="SellerOverride">An optional pawn overriding automatic negotiator selection.</param>
        /// <param name="withBenchmark">Whether to reset rule caches and record detailed benchmark timestamps.</param>
        /// <returns>The constructed <see cref="ItemsToSell"/> snapshot for <paramref name="map"/>.</returns>
        public static ItemsToSell Cache(
            TradeRulesGameComp comp,
            Map map,
            out BenchmarkResults benchmark,
            Pawn SellerOverride = null,
            bool withBenchmark = false)
        {
            long benchmarkAllItems = 0,
                benchmarkJunk = 0,
                benchmarkSell = 0,
                benchmarkTraders = 0,
                benchmarkSellEntries = 0,
                benchmarkPossibleItems = 0,
                benchmarkSilver = 0,
                benchmarkQuantity = 0,
                benchmarkBuildCache = 0;

            if (withBenchmark)
            {
                comp.tradeRules.ForEach(static x => x.search.AllItems = null);
                itemCache ??= GenerateItemCache();
            }

            long timestamp = Stopwatch.GetTimestamp();

            var haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            var allItems = new List<Thing>(haulables.Count);
            var playerFaction = Faction.OfPlayer;
            var zoneManager = map.zoneManager;
            var fogGrid = map.fogGrid;

            for (int i = 0; i < haulables.Count; i++)
            {
                var item = haulables[i];
                if (item == null)
                {
                    continue;
                }

                var pos = item.Position;
                if ((!item.IsForbidden(playerFaction) || zoneManager.ZoneAt(pos) != null) &&
                    (fogGrid == null || !fogGrid.IsFogged(pos)))
                {
                    allItems.Add(item);
                }
            }

            foreach (var pawn in TradeUtility.AllSellableColonyPawns(map, false))
            {
                if (pawn?.Map != null)
                {
                    allItems.Add(pawn);
                }
            }

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkAllItems);
            }

            var thingDictionary = new Dictionary<ThingDef, List<Thing>>();
            var ruleDictionary = new Dictionary<TradeRule, List<Thing>>();

            allItems.ExtractJunkInto(thingDictionary);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkJunk);
            }

            var sellDictionary = allItems.DoRules(comp.tradeRules, ref thingDictionary, ref ruleDictionary);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkSell);
            }

            var traders = FilterAllowedTraders(TabUtility.GetTraders(false), comp.autoTrade, comp.autoTraderIDs);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkTraders);
            }

            Pawn socialPawn = SellerOverride ?? SelectBestNegotiator(traders);
            float playerNegotiator = socialPawn?.GetStatValue(StatDefOf.TradePriceImprovement) ?? 0.1f;
            bool isLeader = ModsConfig.IdeologyActive && socialPawn != null && socialPawn == Faction.OfPlayer.leader;

            var sellEntries = sellDictionary.GetEntries(socialPawn, ref thingDictionary);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkSellEntries);
            }

            var potentialItems = comp.tradeRules.GetPossibleItemsList(sellEntries);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkPossibleItems);
            }

            float totalSilver = CalculateTotalSilver(sellEntries);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkSilver);
            }

            var ruleCounts = ruleDictionary.GetRuleCounts();

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkQuantity);
            }

            var trader = new TraderRecord(
                socialPawn,
                socialPawn?.LabelShort ?? "No trader",
                () => socialPawn != null
                    ? PortraitsCache.Get(
                        socialPawn,
                        new Vector2(24, 24),
                        Rot4.South,
                        ColonistBarColonistDrawer.PawnTextureCameraOffset,
                        1.28205f)
                    : null,
                playerNegotiator.ToStringPercent(),
                playerNegotiator,
                isLeader);

            var sellCache = new ItemsToSell(
                Items: sellEntries,
                PotentialItems: potentialItems,
                TotalSilver: new ItemAndLabel<float>(totalSilver, totalSilver.ToStringMoney()),
                Trader: trader,
                Rules: ruleCounts);

            if (withBenchmark)
            {
                RecordTime(ref timestamp, ref benchmarkBuildCache);
            }

            static ItemAndLabel<long> BM(long v) => new(v, TimeSpan.FromTicks(v).TotalMilliseconds + "ms");

            long total = benchmarkAllItems + benchmarkJunk + benchmarkSell + benchmarkTraders + benchmarkSellEntries +
                         benchmarkPossibleItems + benchmarkQuantity + benchmarkBuildCache;

            benchmark = new BenchmarkResults(
                BM(benchmarkAllItems),
                BM(benchmarkJunk),
                BM(benchmarkSell),
                BM(benchmarkTraders),
                BM(benchmarkSellEntries),
                BM(benchmarkPossibleItems),
                BM(benchmarkQuantity),
                BM(benchmarkBuildCache),
                BM(total));

            return sellCache;
        }

        /// <summary>
        /// Records the elapsed stopwatch ticks since <paramref name="startTime"/> into <paramref name="duration"/>
        /// and advances <paramref name="startTime"/> to the current timestamp.
        /// </summary>
        /// <param name="startTime">Reference to the start timestamp, updated to the current timestamp on return.</param>
        /// <param name="duration">Output parameter receiving the elapsed ticks.</param>
        public static void RecordTime(ref long startTime, ref long duration)
        {
            long benchmarkNewTime = Stopwatch.GetTimestamp();
            duration = benchmarkNewTime - startTime;
            startTime = benchmarkNewTime;
        }

        /// <summary>
        /// Filters the candidate <paramref name="traders"/> list to those whose pawn ID is contained in
        /// <paramref name="autoTraderIDs"/> when <paramref name="autoTrade"/> is enabled, falling back to the
        /// original list if no allowed traders are present.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(T) where T is the number of traders. Space Complexity: O(T).
        /// </remarks>
        internal static List<TraderRecord> FilterAllowedTraders(
            List<TraderRecord> traders,
            bool autoTrade,
            HashSet<int> autoTraderIDs)
        {
            if (traders == null || traders.Count == 0 || !autoTrade || autoTraderIDs == null || autoTraderIDs.Count == 0)
            {
                return traders ?? [];
            }

            var allowedTraders = new List<TraderRecord>(traders.Count);
            for (int i = 0; i < traders.Count; i++)
            {
                var candidate = traders[i];
                if (candidate?.Pawn != null && autoTraderIDs.Contains(candidate.Pawn.thingIDNumber))
                {
                    allowedTraders.Add(candidate);
                }
            }

            return allowedTraders.Count > 0 ? allowedTraders : traders;
        }

        /// <summary>
        /// Sums the total silver value across all <paramref name="sellEntries"/> and rounds to the nearest whole number.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(S) where S is the number of sell entries. Space Complexity: O(1).
        /// </remarks>
        internal static float CalculateTotalSilver(IReadOnlyList<SellRecord> sellEntries)
        {
            if (sellEntries == null || sellEntries.Count == 0)
            {
                return 0f;
            }

            float rawTotalSilver = 0f;
            for (int i = 0; i < sellEntries.Count; i++)
            {
                rawTotalSilver += sellEntries[i].Total.Value;
            }

            return (float)Math.Round(rawTotalSilver, 0);
        }

        /// <summary>
        /// Selects the negotiator pawn with the highest <see cref="TraderRecord.Improvement"/> from <paramref name="traders"/>.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(T) single pass where T is the number of candidate traders. Space Complexity: O(1).
        /// </remarks>
        internal static Pawn SelectBestNegotiator(IReadOnlyList<TraderRecord> traders)
        {
            if (traders == null || traders.Count == 0)
            {
                return null;
            }

            var best = traders[0];
            for (int i = 1; i < traders.Count; i++)
            {
                if (traders[i].Improvement > best.Improvement)
                {
                    best = traders[i];
                }
            }

            return best.Pawn;
        }

        private static List<Thing> ExtractJunkItemsInPlace(List<Thing> allItems, HashSet<Thing> designatedSet = null)
        {
            designatedSet ??= BuildJunkDesignationSet(allItems);
            var junk = new List<Thing>();
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < allItems.Count; readIndex++)
            {
                var item = allItems[readIndex];
                if (IsDesignatedJunk(item, designatedSet))
                {
                    junk.Add(item);
                }
                else
                {
                    allItems[writeIndex++] = item;
                }
            }

            if (writeIndex < allItems.Count)
            {
                allItems.RemoveRange(writeIndex, allItems.Count - writeIndex);
            }

            return junk;
        }

        internal static HashSet<Thing> BuildJunkDesignationSet(IReadOnlyList<Thing> items)
        {
            var def = MGDesignatorDefOf.MGAutoSell;
            if (def == null || items == null || items.Count == 0)
            {
                return null;
            }

            Map map = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i]?.Map != null)
                {
                    map = items[i].Map;
                    break;
                }
            }

            var designations = map?.designationManager?.SpawnedDesignationsOfDef(def);
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

        internal static bool IsDesignatedJunk(Thing item, HashSet<Thing> precomputedSet)
        {
            if (item == null)
            {
                return false;
            }

            if (precomputedSet != null)
            {
                return precomputedSet.Contains(item);
            }

            return item.Map?.designationManager?.DesignationOn(item, MGDesignatorDefOf.MGAutoSell) != null;
        }
    }
}
