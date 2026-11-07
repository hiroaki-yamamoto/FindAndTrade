using System;
using MGAutoSell.Filter;
using MGAutoSell.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeInventoryAggregator"/> verifying stock tracking across
    /// <see cref="TradeRuleAggregation.ThingDef"/> and <see cref="TradeRuleAggregation.Rule"/> scopes.
    /// </summary>
    [TestClass]
    public class TradeInventoryAggregatorTests
    {
        [TestMethod]
        public void GroupedSession_StartAndEnd_TransitionsStateAndClearsCounts()
        {
            var aggregator = new TradeInventoryAggregator();
            var def = TestHelper.CreateThingDef("Steel");
            var rule = new TradeRule { Aggregation = TradeRuleAggregation.ThingDef };

            Assert.IsFalse(aggregator.IsGroupedSessionActive);
            Assert.IsTrue(aggregator.StartGroupedSession());
            Assert.IsTrue(aggregator.IsGroupedSessionActive);

            // Second start while active returns false
            Assert.IsFalse(aggregator.StartGroupedSession());

            aggregator.AddCount(rule, def, 120);
            Assert.AreEqual(120, aggregator.GetCount(rule, def));

            // ClearIfNotGrouped should preserve state while grouped session is active
            aggregator.ClearIfNotGrouped();
            Assert.AreEqual(120, aggregator.GetCount(rule, def));

            Assert.IsTrue(aggregator.EndGroupedSession());
            Assert.IsFalse(aggregator.IsGroupedSessionActive);
            Assert.AreEqual(0, aggregator.GetCount(rule, def));

            // Second end when not active returns false
            Assert.IsFalse(aggregator.EndGroupedSession());
        }

        [TestMethod]
        public void ThingDefAggregation_TracksCountsPerThingDefIndependentlyAndHandlesNullDef()
        {
            var aggregator = new TradeInventoryAggregator();
            var steelDef = TestHelper.CreateThingDef("Steel");
            var plasteelDef = TestHelper.CreateThingDef("Plasteel");
            var rule = new TradeRule { Aggregation = TradeRuleAggregation.ThingDef };

            Assert.IsFalse(aggregator.TryAddInitialCount(null, 50));
            Assert.IsTrue(aggregator.TryAddInitialCount(steelDef, 100));
            Assert.IsFalse(aggregator.TryAddInitialCount(steelDef, 999)); // Should not overwrite existing initial count
            Assert.IsTrue(aggregator.TryAddInitialCount(plasteelDef, 40));

            Assert.AreEqual(0, aggregator.GetCount(rule, null));
            Assert.AreEqual(100, aggregator.GetCount(rule, steelDef));
            Assert.AreEqual(40, aggregator.GetCount(rule, plasteelDef));

            aggregator.AddCount(rule, null, -10); // No-op when def is null
            aggregator.AddCount(null, steelDef, -35); // Null rule defaults to ThingDef aggregation
            Assert.AreEqual(65, aggregator.GetCount(rule, steelDef));
            Assert.AreEqual(40, aggregator.GetCount(rule, plasteelDef));
        }

        [TestMethod]
        public void RuleAggregation_TracksCombinedCountsPerRuleAcrossDefs()
        {
            var aggregator = new TradeInventoryAggregator();
            var beerDef = TestHelper.CreateThingDef("Beer");
            var yayoDef = TestHelper.CreateThingDef("Yayo");
            var ruleA = new TradeRule { Aggregation = TradeRuleAggregation.Rule };
            var ruleB = new TradeRule { Aggregation = TradeRuleAggregation.Rule };

            aggregator.AddCount(ruleA, beerDef, 25);
            aggregator.AddCount(ruleA, yayoDef, 15);
            aggregator.AddCount(ruleB, beerDef, 10);

            Assert.AreEqual(40, aggregator.GetCount(ruleA, beerDef));
            Assert.AreEqual(40, aggregator.GetCount(ruleA, yayoDef));
            Assert.AreEqual(10, aggregator.GetCount(ruleB, beerDef));
        }

        [TestMethod]
        public void GetCount_And_AddCount_InvalidInputs_ThrowExpectedExceptions()
        {
            var aggregator = new TradeInventoryAggregator();
            var def = TestHelper.CreateThingDef("Silver");

            Assert.ThrowsException<ArgumentNullException>(() => aggregator.GetCount(null, def));

            var invalidRule = new TradeRule { Aggregation = (TradeRuleAggregation)999 };
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => aggregator.GetCount(invalidRule, def));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => aggregator.AddCount(invalidRule, def, 10));
        }

        [TestMethod]
        public void ClearIfNotGrouped_WhenNotGrouped_ClearsAllTrackedCounts()
        {
            var aggregator = new TradeInventoryAggregator();
            var def = TestHelper.CreateThingDef("Gold");
            var rule = new TradeRule { Aggregation = TradeRuleAggregation.Rule };

            aggregator.TryAddInitialCount(def, 50);
            aggregator.AddCount(rule, def, 30);

            aggregator.ClearIfNotGrouped();

            var defRule = new TradeRule { Aggregation = TradeRuleAggregation.ThingDef };
            Assert.AreEqual(0, aggregator.GetCount(defRule, def));
            Assert.AreEqual(0, aggregator.GetCount(rule, def));
        }
    }
}
