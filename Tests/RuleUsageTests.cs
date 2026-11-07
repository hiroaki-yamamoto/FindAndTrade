using System;
using System.IO;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RuleUsage"/> formatting without triggering RimWorld translation caches.
    /// </summary>
    [TestClass]
    public class RuleUsageTests
    {
        [TestMethod]
        public void RuleUsage_EmptyItems_ProducesEmptyItemsList()
        {
            var range = (new ItemAndLabel<int>(0, "x0"), new ItemAndLabel<int>(0, "x0"));
            var usage = new RuleUsage([], range);

            Assert.AreEqual(string.Empty, usage.ItemsList);
            usage.Deconstruct(out var items, out var deconstructedRange);
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(0, deconstructedRange.min.Value);
        }
    }
}
