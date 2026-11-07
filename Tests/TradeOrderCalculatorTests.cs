using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Processing;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeOrderCalculator"/> verifying sell/buy quantity calculations,
    /// sell valuations, and rule count range aggregations.
    /// </summary>
    [TestClass]
    public class TradeOrderCalculatorTests
    {
        private readonly TradeOrderCalculator _calculator = TradeOrderCalculator.Instance;

        [TestMethod]
        public void CalculateSellQuantity_StockExceedsExport_ReturnsMinOfExcessAndAvailable()
        {
            Assert.AreEqual(50, _calculator.CalculateSellQuantity(currentStock: 150, exportThreshold: 100, availableColonyCount: 80));
            Assert.AreEqual(30, _calculator.CalculateSellQuantity(currentStock: 150, exportThreshold: 100, availableColonyCount: 30));
        }

        [TestMethod]
        public void CalculateSellQuantity_StockAtOrBelowExportOrZeroAvailable_ReturnsZero()
        {
            Assert.AreEqual(0, _calculator.CalculateSellQuantity(currentStock: 100, exportThreshold: 100, availableColonyCount: 50));
            Assert.AreEqual(0, _calculator.CalculateSellQuantity(currentStock: 80, exportThreshold: 100, availableColonyCount: 50));
            Assert.AreEqual(0, _calculator.CalculateSellQuantity(currentStock: 150, exportThreshold: 100, availableColonyCount: 0));
        }

        [TestMethod]
        public void CalculateBuyQuantity_StockBelowImport_ReturnsMinOfDeficitAndTraderStock()
        {
            Assert.AreEqual(30, _calculator.CalculateBuyQuantity(currentStock: 20, importTarget: 50, availableTraderCount: 100));
            Assert.AreEqual(12, _calculator.CalculateBuyQuantity(currentStock: 20, importTarget: 50, availableTraderCount: 12));
        }

        [TestMethod]
        public void CalculateBuyQuantity_StockAtOrAboveImportOrZeroTraderStock_ReturnsZero()
        {
            Assert.AreEqual(0, _calculator.CalculateBuyQuantity(currentStock: 50, importTarget: 50, availableTraderCount: 100));
            Assert.AreEqual(0, _calculator.CalculateBuyQuantity(currentStock: 75, importTarget: 50, availableTraderCount: 100));
            Assert.AreEqual(0, _calculator.CalculateBuyQuantity(currentStock: 10, importTarget: 50, availableTraderCount: 0));
        }

        [TestMethod]
        public void TryCalculateSellValuation_ItemsAboveThreshold_ComputesRoundedTotalAndUnitPrice()
        {
            // 10 items worth 100 total (10 each), keeping 3 -> selling 7 items worth 70 total
            bool result = _calculator.TryCalculateSellValuation(
                totalRawPrice: 100f,
                totalItems: 10,
                exportThreshold: 3,
                out int sellableCount,
                out float roundedTotal,
                out float unitPrice);

            Assert.IsTrue(result);
            Assert.AreEqual(7, sellableCount);
            Assert.AreEqual(70f, roundedTotal, 0.001f);
            Assert.AreEqual(10f, unitPrice, 0.001f);
        }

        [TestMethod]
        public void TryCalculateSellValuation_ItemsAtOrBelowThreshold_ReturnsFalse()
        {
            bool result = _calculator.TryCalculateSellValuation(
                totalRawPrice: 100f,
                totalItems: 10,
                exportThreshold: 10,
                out int sellableCount,
                out float roundedTotal,
                out float unitPrice);

            Assert.IsFalse(result);
            Assert.AreEqual(0, sellableCount);
            Assert.AreEqual(0f, roundedTotal);
            Assert.AreEqual(0f, unitPrice);
        }

        [TestMethod]
        public void CalculateRuleRange_ThingDefAggregation_ReturnsMinAndMaxAcrossGroups()
        {
            var records = new List<RuleRecord>
            {
                new(TestHelper.CreateThingDef("Corn"), 45),
                new(TestHelper.CreateThingDef("Rice"), 12),
                new(TestHelper.CreateThingDef("Potatoes"), 88)
            };

            var (min, max) = _calculator.CalculateRuleRange(TradeRuleAggregation.ThingDef, records, totalCount: 145);

            Assert.AreEqual(12, min);
            Assert.AreEqual(88, max);
        }

        [TestMethod]
        public void CalculateRuleRange_RuleAggregation_ReturnsTotalCountForBothMinAndMax()
        {
            var records = new List<RuleRecord>
            {
                new(TestHelper.CreateThingDef("Beer"), 20),
                new(TestHelper.CreateThingDef("Yayo"), 35)
            };

            var (min, max) = _calculator.CalculateRuleRange(TradeRuleAggregation.Rule, records, totalCount: 55);

            Assert.AreEqual(55, min);
            Assert.AreEqual(55, max);
        }

        [TestMethod]
        public void CalculateRuleRange_EmptyRecords_ReturnsZeros()
        {
            var (min, max) = _calculator.CalculateRuleRange(TradeRuleAggregation.ThingDef, [], totalCount: 0);

            Assert.AreEqual(0, min);
            Assert.AreEqual(0, max);
        }
    }
}
