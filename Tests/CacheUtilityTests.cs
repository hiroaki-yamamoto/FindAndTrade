using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CacheUtility"/> and <see cref="RuleUsage"/> verifying O(N) list compaction,
    /// rule count aggregation, and potential item deduplication.
    /// </summary>
    [TestClass]
    public class CacheUtilityTests
    {
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
