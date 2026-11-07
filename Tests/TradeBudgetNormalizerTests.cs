using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeBudgetNormalizer"/> verifying O(1) tail order trimming,
    /// partial vs. complete order removal, and inventory aggregator synchronization.
    /// </summary>
    [TestClass]
    public class TradeBudgetNormalizerTests
    {
        private readonly TradeBudgetNormalizer _normalizer = TradeBudgetNormalizer.Instance;

        [TestMethod]
        public void CalculateTrimmedOrder_BuyOrderPartialReduction_ReducesTransferCountByCeiling()
        {
            // Buying 10 items at $10 each ($100 total), currency deficit is $25 -> need to cut ceil(25 / 10) = 3 items -> 7 remaining
            bool removed = _normalizer.CalculateTrimmedOrder(
                currentTransferCount: 10,
                itemOrderCount: 10,
                totalCurrencyCost: 100f,
                currencyGap: 25,
                out int newTransferCount,
                out int inventoryAdjustment);

            Assert.IsFalse(removed);
            Assert.AreEqual(7, newTransferCount);
            Assert.AreEqual(-3, inventoryAdjustment);
        }

        [TestMethod]
        public void CalculateTrimmedOrder_SellOrderPartialReduction_ReducesTransferCountTowardZero()
        {
            // Selling 10 items (currentTransferCount = -10) for $100 total, trader lacks $25 ->
            // costPer = 100 / -10 = -10, reduction = floor(25 / -10) = -3 -> newTransferCount = -10 - (-3) = -7
            bool removed = _normalizer.CalculateTrimmedOrder(
                currentTransferCount: -10,
                itemOrderCount: 10,
                totalCurrencyCost: 100f,
                currencyGap: 25,
                out int newTransferCount,
                out int inventoryAdjustment);

            Assert.IsFalse(removed);
            Assert.AreEqual(-7, newTransferCount);
            Assert.AreEqual(3, inventoryAdjustment);
        }

        [TestMethod]
        public void CalculateTrimmedOrder_CostLessThanGapOrSingleItem_RemovesEntireOrder()
        {
            bool removedByGap = _normalizer.CalculateTrimmedOrder(
                currentTransferCount: 5,
                itemOrderCount: 5,
                totalCurrencyCost: 50f,
                currencyGap: 80,
                out int newTransferCount1,
                out int inventoryAdjustment1);

            Assert.IsTrue(removedByGap);
            Assert.AreEqual(0, newTransferCount1);
            Assert.AreEqual(-1, inventoryAdjustment1);

            bool removedBySingleCount = _normalizer.CalculateTrimmedOrder(
                currentTransferCount: 1,
                itemOrderCount: 1,
                totalCurrencyCost: 50f,
                currencyGap: 10,
                out int newTransferCount2,
                out int inventoryAdjustment2);

            Assert.IsTrue(removedBySingleCount);
            Assert.AreEqual(0, newTransferCount2);
            Assert.AreEqual(-1, inventoryAdjustment2);
        }

        [TestMethod]
        public void CalculateTrimmedOrder_WhenReductionEqualsFullCount_RemovesEntireOrder()
        {
            // Buying 5 items at $10 each ($50 total), gap is $45 -> ceil(45 / 10) = 5 -> newTransferCount = 0 -> removed
            bool removed = _normalizer.CalculateTrimmedOrder(
                currentTransferCount: 5,
                itemOrderCount: 5,
                totalCurrencyCost: 50f,
                currencyGap: 45,
                out int newTransferCount,
                out int inventoryAdjustment);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, newTransferCount);
            Assert.AreEqual(-1, inventoryAdjustment);
        }

        [TestMethod]
        public void NormalizeStep_EmptyOrNullOrderList_ReturnsFalseWithoutThrowing()
        {
            var orders = new List<SellItem>();
            var aggregator = new TradeInventoryAggregator();

            Assert.IsFalse(_normalizer.NormalizeStep(orders, new Dictionary<Tradeable, TradeRule>(), 50, aggregator, static (_, _) => { }));
            Assert.IsFalse(_normalizer.NormalizeStep(null, new Dictionary<Tradeable, TradeRule>(), 50, aggregator, static (_, _) => { }));
        }

        [TestMethod]
        public void NormalizeStep_PartialSellOrderReduction_TrimsTailOrderAndUpdatesAggregatorAndPreservesPositiveItemCount()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var tradeable = TestHelper.CreateTradeable(steelDef, colonyCount: 10, countToTransfer: -10);
            var rule = new TradeRule { Aggregation = TradeRuleAggregation.ThingDef };

            var aggregator = new TradeInventoryAggregator();
            aggregator.TryAddInitialCount(steelDef, 0); // 10 items were queued to sell, leaving 0

            var orders = new List<SellItem> { new(tradeable, 10) };
            var pairings = new Dictionary<Tradeable, TradeRule> { [tradeable] = rule };

            int recordedTransfer = 999;
            bool stepResult = _normalizer.NormalizeStep(
                orders,
                pairings,
                currencyGap: 25,
                aggregator,
                setTradeCount: (t, count) =>
                {
                    TradeDealProcessor.SetTradeCount(t, count);
                    recordedTransfer = count;
                },
                costEvaluator: static _ => 100f);

            Assert.IsTrue(stepResult);
            Assert.AreEqual(1, orders.Count);
            Assert.AreEqual(7, orders[0].Count); // Positive remaining order count preserved
            Assert.AreEqual(-7, recordedTransfer);
            Assert.AreEqual(3, aggregator.GetCount(rule, steelDef)); // 3 unsold items returned to colony inventory count
        }

        [TestMethod]
        public void NormalizeStep_CompleteOrderRemoval_RemovesTailOrderInO1Time()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var goldDef = TestHelper.CreateThingDef("Gold");

            var highPriorityTradeable = TestHelper.CreateTradeable(steelDef, traderCount: 10, countToTransfer: 10);
            var lowPriorityTradeable = TestHelper.CreateTradeable(goldDef, traderCount: 2, countToTransfer: 2);

            var orders = new List<SellItem>
            {
                new(highPriorityTradeable, 10),
                new(lowPriorityTradeable, 2)
            };

            var aggregator = new TradeInventoryAggregator();
            var pairings = new Dictionary<Tradeable, TradeRule>();

            bool stepResult = _normalizer.NormalizeStep(
                orders,
                pairings,
                currencyGap: 500,
                aggregator,
                setTradeCount: TradeDealProcessor.SetTradeCount,
                costEvaluator: static _ => 100f);

            Assert.IsTrue(stepResult);
            Assert.AreEqual(1, orders.Count);
            Assert.AreSame(highPriorityTradeable, orders[0].Tradeable);
            Assert.AreEqual(0, lowPriorityTradeable.CountToTransfer);
        }

        [TestMethod]
        public void NormalizeStep_NullTradeableAtTail_RemovesEntryGracefully()
        {
            var orders = new List<SellItem> { new(null, 5) };
            var aggregator = new TradeInventoryAggregator();

            bool result = _normalizer.NormalizeStep(orders, null, 10, aggregator, static (_, _) => { });

            Assert.IsFalse(result);
            Assert.AreEqual(0, orders.Count);
        }
    }
}
