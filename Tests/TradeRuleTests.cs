using MGAutoSell.Filter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeRule"/> validating mode permissions, threshold validation, hash stability, and deep cloning.
    /// </summary>
    [TestClass]
    public class TradeRuleTests
    {
        [TestMethod]
        public void ExportMode_DefaultZeroThreshold_AllowsSellAndDeniesBuy()
        {
            var rule = new TradeRule
            {
                Mode = TradeMode.Export,
                Export = 0,
                Import = 0
            };

            Assert.IsTrue(rule.NoConfig);
            Assert.IsTrue(rule.AllowSell);
            Assert.IsFalse(rule.AllowBuy);
            Assert.IsFalse(rule.Invalid);
        }

        [TestMethod]
        public void ImportMode_PositiveImport_AllowsBuyAndDeniesSell()
        {
            var rule = new TradeRule
            {
                Mode = TradeMode.Import,
                Import = 50,
                Export = 0
            };

            Assert.IsFalse(rule.NoConfig);
            Assert.IsFalse(rule.AllowSell);
            Assert.IsTrue(rule.AllowBuy);
            Assert.IsFalse(rule.Invalid);
        }

        [TestMethod]
        public void MaintainMode_ValidRangeAndZeroThresholds_EvaluatesPermissionsCorrectly()
        {
            var rule = new TradeRule
            {
                Mode = TradeMode.Maintain,
                Import = 20,
                Export = 100
            };

            Assert.IsTrue(rule.AllowBuy);
            Assert.IsTrue(rule.AllowSell);
            Assert.IsFalse(rule.Invalid);

            // When both Import and Export are 0 in Maintain mode, NoConfig is true -> AllowSell is true, AllowBuy is false
            var zeroMaintain = new TradeRule
            {
                Mode = TradeMode.Maintain,
                Import = 0,
                Export = 0
            };
            Assert.IsTrue(zeroMaintain.NoConfig);
            Assert.IsTrue(zeroMaintain.AllowSell);
            Assert.IsFalse(zeroMaintain.AllowBuy);
        }

        [TestMethod]
        public void Invalid_WhenImportExceedsExportOrNegative_ReturnsTrue()
        {
            var invertedRange = new TradeRule
            {
                Mode = TradeMode.Maintain,
                Import = 100,
                Export = 50
            };
            Assert.IsTrue(invertedRange.Invalid);

            var negativeImport = new TradeRule
            {
                Mode = TradeMode.Import,
                Import = -5,
                Export = 0
            };
            Assert.IsTrue(negativeImport.Invalid);

            var negativeExport = new TradeRule
            {
                Mode = TradeMode.Export,
                Import = 0,
                Export = -1
            };
            Assert.IsTrue(negativeExport.Invalid);
        }

        [TestMethod]
        public void Clone_CopiesAllPropertiesDeepCopiesSearchAllItemsAndCachesHash()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var source = new TradeRule
            {
                Import = 15,
                Export = 60,
                Enabled = false,
                Aggregation = TradeRuleAggregation.Rule,
                Mode = TradeMode.Maintain,
                search = TestHelper.CreateSearch("Metals", [steelDef])
            };

            Assert.AreSame(source.search, source.Search);
            Assert.AreEqual(source.Hash, source.Hash);

            var clone = source.Clone();

            Assert.IsNotNull(clone);
            Assert.AreNotSame(source, clone);
            Assert.AreEqual(15, clone.Import);
            Assert.AreEqual(60, clone.Export);
            Assert.IsFalse(clone.Enabled);
            Assert.AreEqual(TradeRuleAggregation.Rule, clone.Aggregation);
            Assert.AreEqual(TradeMode.Maintain, clone.Mode);
            Assert.AreEqual("15", clone.ImportBuffer);
            Assert.AreEqual("60", clone.ExportBuffer);
            Assert.IsNotNull(clone.search);
            Assert.AreNotSame(source.search, clone.search);
            Assert.AreEqual("Metals", clone.search.name);
            Assert.AreEqual(1, clone.search.AllItems.Count);
            Assert.AreNotSame(source.search.AllItems, clone.search.AllItems);
        }
    }
}
