using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Processing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeBudgetNormalizer"/> verifying O(1) tail order trimming and currency deficit resolution.
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
        public void NormalizeStep_EmptyOrderList_ReturnsFalseWithoutThrowing()
        {
            var orders = new List<SellItem>();
            var aggregator = new TradeInventoryAggregator();
            bool result = _normalizer.NormalizeStep(orders, new Dictionary<Tradeable, TradeRule>(), 50, aggregator, static (_, _) => { });

            Assert.IsFalse(result);
        }
    }
}
