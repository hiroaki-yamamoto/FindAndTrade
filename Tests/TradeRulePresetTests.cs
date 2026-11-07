using System;
using System.Collections.Generic;
using System.IO;
using MGAutoSell.Filter;
using MGAutoSell.Presets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeRulePreset"/> and <see cref="TradeRulePresetStorage"/>.
    /// </summary>
    [TestClass]
    public class TradeRulePresetTests
    {
        private string _tempDir;
        private Func<string> _prevClipboardGetter;
        private Action<string> _prevClipboardSetter;

        [TestInitialize]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FindAndTradeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            TradeRulePresetStorage.CustomPresetDirectory = _tempDir;
            _prevClipboardGetter = TradeRulePresetStorage.ClipboardGetter;
            _prevClipboardSetter = TradeRulePresetStorage.ClipboardSetter;
        }

        [TestCleanup]
        public void TearDown()
        {
            TradeRulePresetStorage.CustomPresetDirectory = null;
            TradeRulePresetStorage.ClipboardGetter = _prevClipboardGetter;
            TradeRulePresetStorage.ClipboardSetter = _prevClipboardSetter;
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [TestMethod]
        public void Constructor_DeepCopiesRulesAndIgnoresNullElements()
        {
            var originalRule = new TradeRule
            {
                Import = 10,
                Export = 40,
                Mode = TradeMode.Maintain,
                Aggregation = TradeRuleAggregation.Rule
            };

            var preset = new TradeRulePreset("MyPreset", [originalRule, null]);

            Assert.AreEqual("MyPreset", preset.name);
            Assert.AreEqual(1, preset.rules.Count);
            Assert.AreNotSame(originalRule, preset.rules[0]);
            Assert.AreEqual(10, preset.rules[0].Import);
            Assert.AreEqual(40, preset.rules[0].Export);

            // Mutating original does not affect preset
            originalRule.Import = 999;
            Assert.AreEqual(10, preset.rules[0].Import);

            var defaultPreset = new TradeRulePreset();
            Assert.AreEqual("", defaultPreset.name);
            Assert.IsNotNull(defaultPreset.rules);
        }

        [TestMethod]
        public void ApplyPreset_And_ApplyPresetToCollection_OverwriteAndAppendModes_WorkCorrectly()
        {
            var existingRule = new TradeRule { Import = 5, Mode = TradeMode.Import };
            var targetList = new List<TradeRule> { existingRule };

            var presetRule1 = new TradeRule { Export = 20, Mode = TradeMode.Export };
            var presetRule2 = new TradeRule { Import = 10, Export = 30, Mode = TradeMode.Maintain };
            var preset = new TradeRulePreset("Test", [presetRule1, presetRule2]);

            // Append mode keeps existing rule and adds 2 cloned rules
            TradeRulePresetStorage.ApplyPresetToCollection(targetList, preset, overwrite: false);
            Assert.AreEqual(3, targetList.Count);
            Assert.AreSame(existingRule, targetList[0]);

            // Overwrite mode replaces all rules with the 2 preset rules
            TradeRulePresetStorage.ApplyPresetToCollection(targetList, preset, overwrite: true);
            Assert.AreEqual(2, targetList.Count);
            Assert.AreEqual(20, targetList[0].Export);
            Assert.AreEqual(30, targetList[1].Export);

            // ApplyPreset on TradeRulesGameComp preserves autoTraderIDs and replaces rules
            var comp = TestHelper.CreateGameComp();
            comp.autoTraderIDs.Add(777);
            TradeRulePresetStorage.ApplyPreset(comp, preset, overwrite: true);
            Assert.AreEqual(2, comp.tradeRules.Count);
            Assert.IsTrue(comp.autoTraderIDs.Contains(777));
        }

        [TestMethod]
        public void SanitizePresetFileName_And_ValidationGuards_HandleInvalidInputs()
        {
            Assert.AreEqual(string.Empty, TradeRulePresetStorage.SanitizePresetFileName(null));
            Assert.AreEqual(string.Empty, TradeRulePresetStorage.SanitizePresetFileName("   "));
            Assert.AreEqual("MyPreset", TradeRulePresetStorage.SanitizePresetFileName("  MyPreset  "));

            Assert.IsFalse(TradeRulePresetStorage.SavePreset("", [], out string emptyNameErr));
            Assert.IsNotNull(emptyNameErr);

            Assert.IsNull(TradeRulePresetStorage.LoadPreset(null, out string nullPathErr));
            Assert.IsNotNull(nullPathErr);

            Assert.IsNull(TradeRulePresetStorage.LoadPreset(Path.Combine(_tempDir, "Missing.xml"), out string missingFileErr));
            Assert.IsNotNull(missingFileErr);

            TradeRulePresetStorage.ClipboardGetter = static () => "";
            Assert.IsFalse(TradeRulePresetStorage.CanPasteFromClipboard());
            Assert.IsNull(TradeRulePresetStorage.PasteFromClipboard(out string clipboardErr));
            Assert.IsNotNull(clipboardErr);
        }

        [TestMethod]
        public void PresetDirectory_And_FileOperations_ManageFilesCorrectly()
        {
            string fileA = Path.Combine(_tempDir, "Alpha.xml");
            string fileB = Path.Combine(_tempDir, "Beta.xml");

            File.WriteAllText(fileA, "<TradeRulePreset />");
            File.SetLastWriteTimeUtc(fileA, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            File.WriteAllText(fileB, "<TradeRulePreset />");
            File.SetLastWriteTimeUtc(fileB, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.IsTrue(TradeRulePresetStorage.PresetExists("Alpha"));
            Assert.IsFalse(TradeRulePresetStorage.PresetExists("NonExistent"));
            Assert.IsFalse(TradeRulePresetStorage.PresetExists("   "));

            var headers = TradeRulePresetStorage.GetPresetHeaders();
            Assert.AreEqual(2, headers.Count);
            // Sorted newest first
            Assert.AreEqual("Beta", headers[0].Name);
            Assert.AreEqual("Alpha", headers[1].Name);

            Assert.IsTrue(TradeRulePresetStorage.DeletePreset(fileA, out string deleteError));
            Assert.IsNull(deleteError);
            Assert.IsFalse(TradeRulePresetStorage.PresetExists("Alpha"));

            Assert.IsFalse(TradeRulePresetStorage.DeletePreset(fileA, out string missingError));
            Assert.IsNotNull(missingError);
        }
    }
}
