using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.HarmonyPatches;
using MGAutoSell.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeDealProcessor"/> verifying stack partitioning, stack-splitting tradeables,
    /// first-item collection, rule order processing, map stock inclusion, extra label cleanup, junk-first sorting,
    /// and executed transfer extraction.
    /// </summary>
    [TestClass]
    public class TradeDealProcessorTests
    {
        [TestInitialize]
        public void SetUp()
        {
            AddRuleLabelsToTradeUI.ExtraLabels.Clear();
            TradeDealProcessor.InventoryAggregator.Clear();
            if (TradeDealProcessor.InventoryAggregator.IsGroupedSessionActive)
            {
                TradeDealProcessor.EndGroupedTrading();
            }
        }

        [TestCleanup]
        public void TearDown()
        {
            AddRuleLabelsToTradeUI.ExtraLabels.Clear();
            TradeDealProcessor.InventoryAggregator.Clear();
            if (TradeDealProcessor.InventoryAggregator.IsGroupedSessionActive)
            {
                TradeDealProcessor.EndGroupedTrading();
            }
        }

        [TestMethod]
        public void StartAndEndGroupedTrading_TransitionsAggregatorSessionState()
        {
            Assert.IsFalse(TradeDealProcessor.InventoryAggregator.IsGroupedSessionActive);

            TradeDealProcessor.StartGroupedTrading();
            Assert.IsTrue(TradeDealProcessor.InventoryAggregator.IsGroupedSessionActive);

            TradeDealProcessor.EndGroupedTrading();
            Assert.IsFalse(TradeDealProcessor.InventoryAggregator.IsGroupedSessionActive);
        }

        [TestMethod]
        public void PartitionStacks_AllocatesListsLazilyAndPartitionsCorrectly()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var stack10 = TestHelper.CreateThing(steelDef, 10);
            var stack50 = TestHelper.CreateThing(steelDef, 50);
            var stack75 = TestHelper.CreateThing(steelDef, 75);

            // Empty/null source leaves both null
            TradeDealProcessor.PartitionStacks(null, static _ => true, out var nullMatched, out var nullUnmatched);
            Assert.IsNull(nullMatched);
            Assert.IsNull(nullUnmatched);

            // All matched -> unmatched remains null
            TradeDealProcessor.PartitionStacks([stack10, stack50], static _ => true, out var allMatched, out var noUnmatched);
            Assert.IsNotNull(allMatched);
            Assert.AreEqual(2, allMatched.Count);
            Assert.IsNull(noUnmatched);

            // All unmatched -> matched remains null
            TradeDealProcessor.PartitionStacks([stack10, stack50], static _ => false, out var noMatched, out var allUnmatched);
            Assert.IsNull(noMatched);
            Assert.IsNotNull(allUnmatched);
            Assert.AreEqual(2, allUnmatched.Count);

            // Mixed -> both lists populated in order
            TradeDealProcessor.PartitionStacks(
                [stack10, stack50, stack75],
                static s => s.stackCount >= 50,
                out var matched,
                out var unmatched);

            CollectionAssert.AreEqual(new List<Thing> { stack50, stack75 }, matched);
            CollectionAssert.AreEqual(new List<Thing> { stack10 }, unmatched);
        }

        [TestMethod]
        public void CollectEntriesByScanningStacks_FullMatch_ClaimsEntireTradeableAndAddsExtraLabel()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var tradeable = TestHelper.CreateTradeable(steelDef, colonyCount: 40, traderCount: 25);
            var allTradeables = new List<Tradeable> { tradeable };
            var itemCache = new List<Tradeable> { tradeable };

            var rule = new TradeRule
            {
                Mode = TradeMode.Maintain,
                Import = 10,
                Export = 30,
                search = TestHelper.CreateSearch("Metals")
            };

            var entries = TradeDealProcessor.CollectEntriesByScanningStacks(
                allTradeables,
                itemCache,
                rule,
                stackMatcher: static _ => true);

            Assert.AreEqual(1, entries.Count);
            Assert.AreSame(tradeable, entries[0].Tradeable);
            Assert.AreEqual(40, entries[0].ColonyCount);
            Assert.AreEqual(25, entries[0].TraderCount);
            Assert.AreEqual(1, allTradeables.Count);
            Assert.IsTrue(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(tradeable));
            StringAssert.Contains(AddRuleLabelsToTradeUI.ExtraLabels[tradeable], "Metals");
        }

        [TestMethod]
        public void CollectEntriesByScanningStacks_PartialMatch_SplitsTradeableIntoClaimedAndUnmatched()
        {
            var apparelDef = TestHelper.CreateThingDef("Apparel_Parka");
            var wornStack = TestHelper.CreateThing(apparelDef, 1);
            var freshStack = TestHelper.CreateThing(apparelDef, 3);

            var tradeable = new Tradeable
            {
                thingsColony = [wornStack, freshStack],
                thingsTrader = []
            };

            var allTradeables = new List<Tradeable> { tradeable };
            var itemCache = new List<Tradeable> { tradeable };
            var rule = new TradeRule
            {
                Mode = TradeMode.Export,
                Export = 0,
                search = TestHelper.CreateSearch("TaintedOrWorn")
            };

            // Only wornStack (stackCount == 1) matches the rule
            var entries = TradeDealProcessor.CollectEntriesByScanningStacks(
                allTradeables,
                itemCache,
                rule,
                stackMatcher: stack => stack == wornStack);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(2, allTradeables.Count);

            var claimed = entries[0].Tradeable;
            Assert.AreNotSame(tradeable, claimed);
            Assert.AreEqual(1, entries[0].ColonyCount);
            CollectionAssert.AreEqual(new List<Thing> { wornStack }, claimed.thingsColony);
            CollectionAssert.AreEqual(new List<Thing> { freshStack }, tradeable.thingsColony);
            Assert.IsTrue(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(claimed));
            Assert.IsFalse(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(tradeable));
        }

        [TestMethod]
        public void CollectEntriesByFirstItem_SkipsAllJunkTradeablesAndMatchesNonJunk()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var goldDef = TestHelper.CreateThingDef("Gold");

            var junkSteel = TestHelper.CreateThing(steelDef, 30);
            var validGold = TestHelper.CreateThing(goldDef, 20);

            var junkTradeable = new Tradeable { thingsColony = [junkSteel], thingsTrader = [] };
            var goldTradeable = new Tradeable { thingsColony = [validGold], thingsTrader = [] };

            var junkSet = new HashSet<Thing> { junkSteel };
            var rule = new TradeRule { search = TestHelper.CreateSearch("All") };

            var entries = TradeDealProcessor.CollectEntriesByFirstItem(
                [junkTradeable, goldTradeable],
                rule,
                junkSet,
                itemMatcher: static _ => true);

            Assert.AreEqual(1, entries.Count);
            Assert.AreSame(goldTradeable, entries[0].Tradeable);
            Assert.AreEqual(20, entries[0].ColonyCount);
        }

        [TestMethod]
        public void ProcessRuleEntries_ExportAndImportWithMapStock_CalculatesOrdersAndCleansUnusedLabels()
        {
            var medicineDef = TestHelper.CreateThingDef("MedicineIndustrial");
            var beerDef = TestHelper.CreateThingDef("Beer");
            var neutroDef = TestHelper.CreateThingDef("Neutroamine");

            // Colony has 10 medicine in tradeable, trader has 50. Hospital on map has 30 more medicine!
            var medTradeable = TestHelper.CreateTradeable(medicineDef, colonyCount: 10, traderCount: 50);
            var hospitalMedicine = TestHelper.CreateThing(medicineDef, 30);

            // Colony has 80 beer, export threshold is 50 -> should sell 30
            var beerTradeable = TestHelper.CreateTradeable(beerDef, colonyCount: 80, traderCount: 0);

            // Colony has 45 neutroamine (between Import=40 and Export=50) -> no buy or sell -> label should be cleaned up
            var neutroTradeable = TestHelper.CreateTradeable(neutroDef, colonyCount: 45, traderCount: 20);

            AddRuleLabelsToTradeUI.ExtraLabels[medTradeable] = "RuleTag";
            AddRuleLabelsToTradeUI.ExtraLabels[beerTradeable] = "RuleTag";
            AddRuleLabelsToTradeUI.ExtraLabels[neutroTradeable] = "RuleTag";

            var aggregator = new TradeInventoryAggregator();
            aggregator.TryAddInitialCount(medicineDef, 10);
            aggregator.TryAddInitialCount(beerDef, 80);
            aggregator.TryAddInitialCount(neutroDef, 45);

            var rule = new TradeRule
            {
                Enabled = true,
                Mode = TradeMode.Maintain,
                Import = 45,
                Export = 50,
                Aggregation = TradeRuleAggregation.ThingDef
            };

            var entries = new List<TradeEntry>
            {
                new(medTradeable, medicineDef, 10, 50),
                new(beerTradeable, beerDef, 80, 0),
                new(neutroTradeable, neutroDef, 45, 20)
            };

            var itemCache = new List<Tradeable> { medTradeable, beerTradeable, neutroTradeable };
            var sellDict = new Dictionary<Tradeable, int>();
            var buyDict = new Dictionary<Tradeable, int>();
            var pairings = new Dictionary<Tradeable, TradeRule>();

            TradeDealProcessor.ProcessRuleEntries(
                rule,
                entries,
                itemCache,
                itemsOnMap: [hospitalMedicine],
                doBuy: true,
                aggregator,
                TradeOrderCalculator.Instance,
                sellDict,
                buyDict,
                pairings,
                mapItemMatcher: (r, m) => m.def == medicineDef);

            // Beer: 80 > 50 -> sells 30
            Assert.AreEqual(30, sellDict[beerTradeable]);
            Assert.AreSame(rule, pairings[beerTradeable]);

            // Medicine: 10 in tradeable + 30 on map = 40 stock -> needs 45 - 40 = 5 bought (not 35!)
            Assert.AreEqual(5, buyDict[medTradeable]);

            // Neutroamine: neither bought nor sold -> stays in itemCache and has its ExtraLabel removed
            Assert.IsFalse(sellDict.ContainsKey(neutroTradeable));
            Assert.IsFalse(buyDict.ContainsKey(neutroTradeable));
            Assert.IsFalse(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(neutroTradeable));
            Assert.IsTrue(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(medTradeable));
            Assert.IsTrue(AddRuleLabelsToTradeUI.ExtraLabels.ContainsKey(beerTradeable));
            CollectionAssert.AreEqual(new List<Tradeable> { neutroTradeable }, itemCache);
        }

        [TestMethod]
        public void ProcessRuleEntries_RuleAggregationMode_SumsAcrossMultipleThingDefs()
        {
            var cornDef = TestHelper.CreateThingDef("RawCorn");
            var riceDef = TestHelper.CreateThingDef("RawRice");

            var cornTradeable = TestHelper.CreateTradeable(cornDef, colonyCount: 60, traderCount: 0);
            var riceTradeable = TestHelper.CreateTradeable(riceDef, colonyCount: 50, traderCount: 0);

            var aggregator = new TradeInventoryAggregator();
            var rule = new TradeRule
            {
                Enabled = true,
                Mode = TradeMode.Export,
                Export = 80,
                Aggregation = TradeRuleAggregation.Rule
            };

            var entries = new List<TradeEntry>
            {
                new(cornTradeable, cornDef, 60, 0),
                new(riceTradeable, riceDef, 50, 0)
            };

            var sellDict = new Dictionary<Tradeable, int>();
            var buyDict = new Dictionary<Tradeable, int>();
            var pairings = new Dictionary<Tradeable, TradeRule>();

            TradeDealProcessor.ProcessRuleEntries(
                rule,
                entries,
                itemCache: [cornTradeable, riceTradeable],
                itemsOnMap: null,
                doBuy: false,
                aggregator,
                TradeOrderCalculator.Instance,
                sellDict,
                buyDict,
                pairings);

            // Combined stock = 60 + 50 = 110, Export = 80 -> excess = 30 sold from first entry (corn), leaving 80 total
            Assert.AreEqual(30, sellDict[cornTradeable]);
            Assert.IsFalse(sellDict.ContainsKey(riceTradeable));
            Assert.AreEqual(80, aggregator.GetCount(rule, cornDef));
        }

        [TestMethod]
        public void SortColonyThingsJunkFirst_MovesJunkItemsToFront()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var normalStack1 = TestHelper.CreateThing(steelDef, 20);
            var junkStack = TestHelper.CreateThing(steelDef, 15);
            var normalStack2 = TestHelper.CreateThing(steelDef, 30);

            var colonyList = new List<Thing> { normalStack1, junkStack, normalStack2 };
            var junkSet = new HashSet<Thing> { junkStack };

            TradeDealProcessor.SortColonyThingsJunkFirst(colonyList, junkSet);

            Assert.AreSame(junkStack, colonyList[0]);
        }

        [TestMethod]
        public void ExtractExecutedTransfers_PartitionsPositiveToBuyAndNegativeToSell()
        {
            var steelDef = TestHelper.CreateThingDef("Steel", "steel");
            var goldDef = TestHelper.CreateThingDef("Gold", "gold");
            var woodDef = TestHelper.CreateThingDef("WoodLog", "wood");

            var buySteel = TestHelper.CreateTradeable(steelDef, traderCount: 50, countToTransfer: 25);
            var sellGold = TestHelper.CreateTradeable(goldDef, colonyCount: 40, countToTransfer: -15);
            var untouchedWood = TestHelper.CreateTradeable(woodDef, colonyCount: 10, countToTransfer: 0);

            TradeDealProcessor.ExtractExecutedTransfers(
                [buySteel, sellGold, untouchedWood, null],
                out var buy,
                out var sell);

            Assert.AreEqual(1, buy.Count);
            Assert.AreEqual(("steel", 25), buy[0]);

            Assert.AreEqual(1, sell.Count);
            Assert.AreEqual(("gold", -15), sell[0]);
        }
    }
}
