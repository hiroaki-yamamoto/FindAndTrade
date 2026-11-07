using System.Collections.Generic;
using MGAutoSell.Caravans;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeRouteCalculator"/> verifying route priority classification and O(N) best-route selection.
    /// </summary>
    [TestClass]
    public class TradeRouteCalculatorTests
    {
        private readonly TradeRouteCalculator _calculator = TradeRouteCalculator.Instance;

        [TestMethod]
        public void CalculateExpectedSales_CapsAtTraderSilverWhenTraderWillingExceedsSilver()
        {
            Assert.AreEqual(500f, _calculator.CalculateExpectedSales(traderWillingTotal: 800f, traderSilver: 500f, colonyTotalSilver: 1000f));
            Assert.AreEqual(1000f, _calculator.CalculateExpectedSales(traderWillingTotal: 300f, traderSilver: 500f, colonyTotalSilver: 1000f));
        }

        [TestMethod]
        public void EvaluatePriority_ClassifiesRatioAndTraderReservesCorrectly()
        {
            // ratio = 500 / 100 = 5 (> 4) and traderSilver (1000) > sales (500) -> High
            Assert.AreEqual(
                SettlementTradePriority.High,
                _calculator.EvaluatePriority(sales: 500f, fuelCost: 100f, traderSilver: 1000f));

            // ratio = 500 / 100 = 5 (>= 4) but traderSilver (500) == sales (500) -> Medium
            Assert.AreEqual(
                SettlementTradePriority.Medium,
                _calculator.EvaluatePriority(sales: 500f, fuelCost: 100f, traderSilver: 500f));

            // ratio = 400 / 100 = 4 (>= 4) -> Medium
            Assert.AreEqual(
                SettlementTradePriority.Medium,
                _calculator.EvaluatePriority(sales: 400f, fuelCost: 100f, traderSilver: 1000f));

            // ratio = 300 / 100 = 3 (> 2) -> Low
            Assert.AreEqual(
                SettlementTradePriority.Low,
                _calculator.EvaluatePriority(sales: 300f, fuelCost: 100f, traderSilver: 1000f));

            // ratio = 150 / 100 = 1.5 (<= 2) -> Backup
            Assert.AreEqual(
                SettlementTradePriority.Backup,
                _calculator.EvaluatePriority(sales: 150f, fuelCost: 100f, traderSilver: 1000f));

            // zero fuel safety guard -> Backup
            Assert.AreEqual(
                SettlementTradePriority.Backup,
                _calculator.EvaluatePriority(sales: 500f, fuelCost: 0f, traderSilver: 1000f));
        }

        [TestMethod]
        public void SelectBestRoute_OrdersByPriorityDescendingThenSilverDescending()
        {
            var routeBackupHighSilver = new TradeRoute(null, 10f, 5000f, 50f, SettlementTradePriority.Backup);
            var routeMediumLowSilver = new TradeRoute(null, 20f, 400f, 50f, SettlementTradePriority.Medium);
            var routeMediumHighSilver = new TradeRoute(null, 25f, 950f, 50f, SettlementTradePriority.Medium);
            var routeLow = new TradeRoute(null, 15f, 1200f, 50f, SettlementTradePriority.Low);

            var routes = new List<TradeRoute>
            {
                routeBackupHighSilver,
                routeMediumLowSilver,
                routeMediumHighSilver,
                routeLow
            };

            var best = _calculator.SelectBestRoute(routes);

            Assert.AreSame(routeMediumHighSilver, best);
        }

        [TestMethod]
        public void SelectBestRoute_NullOrEmptyInput_ReturnsNull()
        {
            Assert.IsNull(_calculator.SelectBestRoute(null));
            Assert.IsNull(_calculator.SelectBestRoute([]));
        }
    }
}
