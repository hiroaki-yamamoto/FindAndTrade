using System.Collections.Generic;
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
        public void RuleUsage_EmptyOrNullItems_ProducesEmptyItemsList()
        {
            var range = (new ItemAndLabel<int>(0, "x0"), new ItemAndLabel<int>(0, "x0"));
            var usage = new RuleUsage([], range);

            Assert.AreEqual(string.Empty, usage.ItemsList);
            usage.Deconstruct(out var items, out var deconstructedRange);
            Assert.AreEqual(0, items.Count);
            Assert.AreEqual(0, deconstructedRange.min.Value);

            var nullUsage = new RuleUsage(null, range);
            Assert.AreEqual(0, nullUsage.Items.Count);
            Assert.AreEqual(string.Empty, nullUsage.ItemsList);
        }

        [TestMethod]
        public void RuleUsage_MultipleItems_FormatsBulletedListWithFallbackToDefName()
        {
            // Empty label uses defName fallback without calling LabelCap translation cache
            var steelDef = TestHelper.CreateThingDef("Steel", "");
            var plasteelDef = TestHelper.CreateThingDef("Plasteel", "");

            var records = new List<RuleRecord>
            {
                new(steelDef, 45),
                new(plasteelDef, 12),
                new(null, 5)
            };

            var range = (new ItemAndLabel<int>(5, "x5"), new ItemAndLabel<int>(45, "x45"));
            var usage = new RuleUsage(records, range);

            Assert.AreEqual("- Steel x45\n- Plasteel x12\n-  x5", usage.ItemsList);
        }
    }
}
