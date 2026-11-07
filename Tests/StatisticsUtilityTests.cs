using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StatisticsUtility"/> and <see cref="MainTabWindow_FindAndTrade.Median"/>.
    /// </summary>
    [TestClass]
    public class StatisticsUtilityTests
    {
        [TestMethod]
        public void Median_NullList_ThrowsInvalidOperationException()
        {
            Assert.ThrowsException<InvalidOperationException>(() => StatisticsUtility.Median(null));
        }

        [TestMethod]
        public void Median_EmptyList_ThrowsInvalidOperationException()
        {
            var empty = new List<long>();
            Assert.ThrowsException<InvalidOperationException>(() => StatisticsUtility.Median(empty));
        }

        [TestMethod]
        public void Median_SingleElement_ReturnsElement()
        {
            var values = new List<long> { 42L };
            Assert.AreEqual(42f, StatisticsUtility.Median(values));
        }

        [TestMethod]
        public void Median_OddElementCount_ReturnsMiddleValueAndDoesNotMutateInput()
        {
            var values = new List<long> { 50L, 10L, 40L, 20L, 30L };
            float result = StatisticsUtility.Median(values);

            Assert.AreEqual(30f, result);
            CollectionAssert.AreEqual(new List<long> { 50L, 10L, 40L, 20L, 30L }, values);
        }

        [TestMethod]
        public void Median_EvenElementCount_ReturnsAverageOfTwoMiddleValues()
        {
            var values = new List<long> { 40L, 10L, 25L, 20L };
            float result = StatisticsUtility.Median(values);

            // Sorted: 10, 20, 25, 40 -> (20 + 25) / 2 = 22.5
            Assert.AreEqual(22.5f, result, 0.0001f);
        }

        [TestMethod]
        public void MainTabWindowMedian_DelegatesToStatisticsUtility()
        {
            var values = new List<long> { 100L, 300L, 200L };
            Assert.AreEqual(200f, MainTabWindow_FindAndTrade.Median(values));
        }
    }
}
