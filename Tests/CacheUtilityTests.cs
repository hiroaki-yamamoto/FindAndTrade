using System;
using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CacheUtility"/> verifying O(N) junk extraction, rule matching and compaction,
    /// sell valuation entry generation, rule count aggregation, negotiator selection, and potential item deduplication.
    /// </summary>
    [TestClass]
    public class CacheUtilityTests
    {
        private sealed class DelegateThingQuery : ThingQuery
        {
            private readonly Func<Thing, bool> _predicate;

            public DelegateThingQuery(Func<Thing, bool> predicate)
            {
                _predicate = predicate;
            }

            public override bool AppliesDirectlyTo(Thing thing) => _predicate(thing);
        }

        [TestMethod]
        public void GetJunk_WithoutDesignations_LeavesItemsIntactAndReturnsEmpty()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var items = new List<Thing>
            {
                TestHelper.CreateThing(steelDef, 50),
                TestHelper.CreateThing(steelDef, 25)
            };

            var junkGroups = items.GetJunk();

            Assert.AreEqual(0, junkGroups.Count);
            Assert.AreEqual(2, items.Count);
        }

        [TestMethod]
        public void GetJunk_WithDesignatedSet_ExtractsAndGroupsJunkAndCompactsListInPlace()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var silverDef = TestHelper.CreateThingDef("Silver");

            var junkSteel1 = TestHelper.CreateThing(steelDef, 30);
            var keepSilver = TestHelper.CreateThing(silverDef, 100);
            var junkSteel2 = TestHelper.CreateThing(steelDef, 20);

            var items = new List<Thing> { junkSteel1, keepSilver, junkSteel2 };
            var designatedSet = new HashSet<Thing> { junkSteel1, junkSteel2 };

            var junkGroups = items.GetJunk(designatedSet);

            Assert.AreEqual(1, junkGroups.Count);
            Assert.AreSame(steelDef, junkGroups[0].Key);
            CollectionAssert.AreEqual(new List<Thing> { keepSilver }, items);
        }

        [TestMethod]
        public void ExtractJunkInto_PopulatesDictionaryAndRemovesJunkFromSourceList()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var goldDef = TestHelper.CreateThingDef("Gold");

            var junkSteel = TestHelper.CreateThing(steelDef, 40);
            var keepGold = TestHelper.CreateThing(goldDef, 15);
            var items = new List<Thing> { junkSteel, keepGold };
            var thingDict = new Dictionary<ThingDef, List<Thing>>();
            var designatedSet = new HashSet<Thing> { junkSteel };

            items.ExtractJunkInto(thingDict, designatedSet);

            Assert.AreEqual(1, items.Count);
            Assert.AreSame(keepGold, items[0]);
            Assert.AreEqual(1, thingDict.Count);
            Assert.AreEqual(1, thingDict[steelDef].Count);
            Assert.AreSame(junkSteel, thingDict[steelDef][0]);
        }

        [TestMethod]
        public void DoRules_EvaluatesPriorityCompactsSoldItemsAndPreservesImportAndUnsellableItems()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var goldDef = TestHelper.CreateThingDef("Gold");
            var questItemDef = TestHelper.CreateThingDef("QuestRelic");

            var steelStack = TestHelper.CreateThing(steelDef, 75);
            var goldStack = TestHelper.CreateThing(goldDef, 30);
            var questStack = TestHelper.CreateThing(questItemDef, 1);

            var allItems = new List<Thing> { steelStack, goldStack, questStack };

            var disabledRule = new TradeRule { Enabled = false, Mode = TradeMode.Export, Export = 0 };
            var importGoldRule = new TradeRule { Enabled = true, Mode = TradeMode.Import, Import = 100 };
            var exportAllRule = new TradeRule { Enabled = true, Mode = TradeMode.Export, Export = 10 };
            var secondaryExportRule = new TradeRule { Enabled = true, Mode = TradeMode.Export, Export = 99 };

            var rules = new List<TradeRule>
            {
                disabledRule,
                importGoldRule,
                exportAllRule,
                secondaryExportRule
            };

            var thingDict = new Dictionary<ThingDef, List<Thing>>();
            var ruleDict = new Dictionary<TradeRule, List<Thing>>();

            var sellDict = allItems.DoRules(
                rules,
                ref thingDict,
                ref ruleDict,
                itemMatcher: (rule, item) =>
                {
                    if (rule == importGoldRule) return item.def == goldDef;
                    return true;
                },
                isSellable: def => def != questItemDef);

            // importGoldRule matched goldStack for counting, but did not remove it from allItems
            Assert.IsTrue(ruleDict.ContainsKey(importGoldRule));
            Assert.AreEqual(1, ruleDict[importGoldRule].Count);

            // exportAllRule claimed steelStack and goldStack, leaving only unsellable questStack in allItems
            Assert.AreEqual(1, allItems.Count);
            Assert.AreSame(questStack, allItems[0]);

            Assert.AreEqual(2, thingDict.Count);
            Assert.AreEqual(10, sellDict[steelDef]);
            Assert.AreEqual(10, sellDict[goldDef]);

            // secondaryExportRule saw no sellable items left because exportAllRule compacted them out
            Assert.IsFalse(ruleDict.ContainsKey(secondaryExportRule));
        }

        [TestMethod]
        public void GetEntries_ComputesValuationsRemovesEmptyKeysAndSortsDescendingByTotal()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var goldDef = TestHelper.CreateThingDef("Gold");
            var emptyDef = TestHelper.CreateThingDef("Empty");
            var belowThresholdDef = TestHelper.CreateThingDef("Wood");

            var thingDict = new Dictionary<ThingDef, List<Thing>>
            {
                [steelDef] = [TestHelper.CreateThing(steelDef, 50)], // 50 * $2 = $100, keep 10 -> 40 sold ($80)
                [goldDef] = [TestHelper.CreateThing(goldDef, 20)],   // 20 * $10 = $200, keep 0 -> 20 sold ($200)
                [emptyDef] = [],
                [belowThresholdDef] = [TestHelper.CreateThing(belowThresholdDef, 5)] // keep 10 -> 0 sold
            };

            var sellDict = new Dictionary<ThingDef, int>
            {
                [steelDef] = 10,
                [goldDef] = 0,
                [belowThresholdDef] = 10
            };

            var entries = sellDict.GetEntries(
                socialPawn: null,
                ref thingDict,
                unitPriceEvaluator: item => item.def == goldDef ? 10f : 2f,
                moneyFormatter: val => $"${val:0}");

            // Empty key removed from thingDict
            Assert.IsFalse(thingDict.ContainsKey(emptyDef));

            // Sorted descending by total value: Gold ($200) then Steel ($80)
            Assert.AreEqual(2, entries.Count);
            Assert.AreSame(goldDef, entries[0].Item);
            Assert.AreEqual(20, entries[0].Count);
            Assert.AreEqual(200f, entries[0].Total.Value);
            Assert.AreEqual("$200", entries[0].Total.Label);

            Assert.AreSame(steelDef, entries[1].Item);
            Assert.AreEqual(40, entries[1].Count);
            Assert.AreEqual(80f, entries[1].Total.Value);
            Assert.AreEqual("$80", entries[1].Total.Label);
        }

        [TestMethod]
        public void GetRuleCounts_AggregatesByThingDefAndComputesMinMaxInSinglePass()
        {
            var prevSettings = Mod.Settings;
            try
            {
                Mod.Settings = new Settings
                {
                    showQuantityInsteadOfLabel = true,
                    colorRuleCountsOnWork = true
                };

                var cornDef = TestHelper.CreateThingDef("RawCorn", "corn");
                var riceDef = TestHelper.CreateThingDef("RawRice", "rice");

                var ruleByDef = new TradeRule { Aggregation = TradeRuleAggregation.ThingDef };
                var ruleByTotal = new TradeRule { Aggregation = TradeRuleAggregation.Rule };

                var ruleDict = new Dictionary<TradeRule, List<Thing>>
                {
                    [ruleByDef] =
                    [
                        TestHelper.CreateThing(cornDef, 20),
                        TestHelper.CreateThing(cornDef, 15),
                        TestHelper.CreateThing(riceDef, 10)
                    ],
                    [ruleByTotal] =
                    [
                        TestHelper.CreateThing(cornDef, 20),
                        TestHelper.CreateThing(riceDef, 10)
                    ]
                };

                var counts = ruleDict.GetRuleCounts();

                Assert.AreEqual(2, counts.Count);

                var defUsage = counts[ruleByDef];
                Assert.AreEqual(10, defUsage.Range.min.Value);
                Assert.AreEqual("x10", defUsage.Range.min.Label);
                Assert.AreEqual(35, defUsage.Range.max.Value);
                Assert.AreEqual("x35", defUsage.Range.max.Label);

                var totalUsage = counts[ruleByTotal];
                Assert.AreEqual(30, totalUsage.Range.min.Value);
                Assert.AreEqual(30, totalUsage.Range.max.Value);
            }
            finally
            {
                Mod.Settings = prevSettings;
            }
        }

        [TestMethod]
        public void GetRuleCounts_WhenSettingsDisabledOrEmpty_ReturnsEmpty()
        {
            var prevSettings = Mod.Settings;
            try
            {
                Mod.Settings = new Settings
                {
                    showQuantityInsteadOfLabel = false,
                    colorRuleCountsOnWork = false
                };

                var def = TestHelper.CreateThingDef("Steel");
                var rule = new TradeRule();
                var dict = new Dictionary<TradeRule, List<Thing>>
                {
                    [rule] = [TestHelper.CreateThing(def, 10)]
                };

                Assert.AreEqual(0, dict.GetRuleCounts().Count);
                Assert.AreEqual(0, ((Dictionary<TradeRule, List<Thing>>)null).GetRuleCounts().Count);
            }
            finally
            {
                Mod.Settings = prevSettings;
            }
        }

        [TestMethod]
        public void GetPossibleItemsList_DeduplicatesAndExcludesItemsInSellEntries()
        {
            var prevSettings = Mod.Settings;
            var prevCache = CacheUtility.itemCache;
            try
            {
                Mod.Settings = new Settings { showAllMatchingItems = true };
                CacheUtility.itemCache = [];

                var steelDef = TestHelper.CreateThingDef("Steel");
                var goldDef = TestHelper.CreateThingDef("Gold");
                var plasteelDef = TestHelper.CreateThingDef("Plasteel");

                var rule = new TradeRule
                {
                    Enabled = true,
                    Mode = TradeMode.Import,
                    Import = 50,
                    search = TestHelper.CreateSearch("Metals", [steelDef, goldDef, goldDef, plasteelDef])
                };

                var sellEntries = new List<SellRecord>
                {
                    new(steelDef, 10, new ItemAndLabel<float>(20f, "$20"), new ItemAndLabel<float>(2f, "$2"))
                };

                var rules = new List<TradeRule> { rule };
                var potentials = rules.GetPossibleItemsList(sellEntries);

                // steelDef is excluded because it's in sellEntries; goldDef is deduplicated
                Assert.AreEqual(2, potentials.Count);
                Assert.AreEqual(goldDef, potentials[0].Item);
                Assert.AreEqual("<i>(Metals)</i>", potentials[0].Rule);
                Assert.AreEqual(plasteelDef, potentials[1].Item);
            }
            finally
            {
                Mod.Settings = prevSettings;
                CacheUtility.itemCache = prevCache;
            }
        }

        [TestMethod]
        public void GetPossibleItems_EvaluatesQueryAndCompactsOutFaultyItems()
        {
            var prevCache = CacheUtility.itemCache;
            try
            {
                var steelDef = TestHelper.CreateThingDef("Steel");
                var faultyDef = TestHelper.CreateThingDef("FaultyItem");
                var goldDef = TestHelper.CreateThingDef("Gold");

                var steelThing = TestHelper.CreateThing(steelDef);
                var faultyThing = TestHelper.CreateThing(faultyDef);
                var goldThing = TestHelper.CreateThing(goldDef);

                CacheUtility.itemCache = [steelThing, faultyThing, goldThing];

                var search = new TradeQuerySearch { name = "TestQuery" };
                search.Children.queries.Add(new DelegateThingQuery(t =>
                {
                    if (t.def == faultyDef)
                    {
                        throw new InvalidOperationException("Simulated complex item failure");
                    }

                    return t.def == goldDef;
                }));

                var matched = search.GetPossibleItems();

                Assert.AreEqual(1, matched.Count);
                Assert.AreSame(goldDef, matched[0]);
                Assert.AreEqual(2, CacheUtility.itemCache.Count);
                Assert.AreSame(steelThing, CacheUtility.itemCache[0]);
                Assert.AreSame(goldThing, CacheUtility.itemCache[1]);
            }
            finally
            {
                CacheUtility.itemCache = prevCache;
            }
        }

        [TestMethod]
        public void FilterAllowedTraders_And_SelectBestNegotiator_WorkCorrectly()
        {
            var pawnA = TestHelper.CreatePawn(101);
            var pawnB = TestHelper.CreatePawn(202);
            var pawnC = TestHelper.CreatePawn(303);

            var traderA = new TraderRecord(pawnA, "Alice", static () => null, "10%", 0.10f, false);
            var traderB = new TraderRecord(pawnB, "Bob", static () => null, "25%", 0.25f, false);
            var traderC = new TraderRecord(pawnC, "Charlie", static () => null, "18%", 0.18f, true);
            var allTraders = new List<TraderRecord> { traderA, traderB, traderC };

            // When autoTrade is false, returns all traders
            var unfiltered = CacheUtility.FilterAllowedTraders(allTraders, autoTrade: false, [101]);
            Assert.AreEqual(3, unfiltered.Count);

            // When autoTrade is true and matching IDs exist, filters to allowed traders
            var filtered = CacheUtility.FilterAllowedTraders(allTraders, autoTrade: true, [101, 303]);
            Assert.AreEqual(2, filtered.Count);
            Assert.AreSame(pawnC, CacheUtility.SelectBestNegotiator(filtered));

            // When autoTrade is true but no IDs match, falls back to all traders
            var fallback = CacheUtility.FilterAllowedTraders(allTraders, autoTrade: true, [999]);
            Assert.AreEqual(3, fallback.Count);
            Assert.AreSame(pawnB, CacheUtility.SelectBestNegotiator(fallback));

            Assert.IsNull(CacheUtility.SelectBestNegotiator(null));
            Assert.IsNull(CacheUtility.SelectBestNegotiator([]));
        }

        [TestMethod]
        public void CalculateTotalSilver_SumsAndRoundsCorrectly()
        {
            var def = TestHelper.CreateThingDef("SilverItem");
            var entries = new List<SellRecord>
            {
                new(def, 1, new ItemAndLabel<float>(12.4f, "$12"), new ItemAndLabel<float>(12.4f, "$12")),
                new(def, 1, new ItemAndLabel<float>(7.4f, "$7"), new ItemAndLabel<float>(7.4f, "$7"))
            };

            Assert.AreEqual(20f, CacheUtility.CalculateTotalSilver(entries));
            Assert.AreEqual(0f, CacheUtility.CalculateTotalSilver(null));
            Assert.AreEqual(0f, CacheUtility.CalculateTotalSilver([]));
        }

        [TestMethod]
        public void RecordTime_UpdatesDurationAndAdvancesStartTime()
        {
            long start = 100L;
            long duration = 0L;

            CacheUtility.RecordTime(ref start, ref duration);

            Assert.IsTrue(start > 100L);
            Assert.IsTrue(duration > 0L);
        }
    }
}
