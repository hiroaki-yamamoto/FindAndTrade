using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MGAutoSell.Filter;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell.Presets
{
    /// <summary>
    /// Metadata header for a saved trade rule preset file.
    /// </summary>
    public class TradeRulePresetHeader
    {
        public string Name { get; }
        public string FilePath { get; }
        public DateTime LastWriteTime { get; }

        public TradeRulePresetHeader(string filePath)
        {
            FilePath = filePath;
            Name = Path.GetFileNameWithoutExtension(filePath);
            try
            {
                LastWriteTime = File.GetLastWriteTime(filePath);
            }
            catch
            {
                LastWriteTime = DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// Handles persistent storage, file management, and clipboard operations for trade rule presets.
    /// Presets are stored under the RimWorld user save data directory so they are shared across all saves.
    /// </summary>
    public static class TradeRulePresetStorage
    {
        public const string FolderName = "TradeRulePresets";

        /// <summary>
        /// Absolute path to the trade rule presets directory under user's save data.
        /// </summary>
        public static string PresetDirectory
        {
            get
            {
                var dir = Path.Combine(GenFilePaths.SaveDataFolderPath, FolderName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        /// <summary>
        /// Retrieves all saved preset headers, sorted by last modified date descending.
        /// Time complexity: O(N) where N is number of preset files.
        /// </summary>
        public static List<TradeRulePresetHeader> GetPresetHeaders()
        {
            var list = new List<TradeRulePresetHeader>();
            try
            {
                var dir = PresetDirectory;
                if (!Directory.Exists(dir))
                    return list;

                var files = Directory.GetFiles(dir, "*.xml");
                foreach (var file in files)
                {
                    list.Add(new TradeRulePresetHeader(file));
                }

                list.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            }
            catch (Exception ex)
            {
                Log.Error($"[FindAndTrade] Failed to get preset headers: {ex}");
            }
            return list;
        }

        /// <summary>
        /// Checks if a preset with the specified name already exists.
        /// </summary>
        public static bool PresetExists(string name)
        {
            var sanitized = GenFile.SanitizedFileName(name);
            if (sanitized.NullOrEmpty()) return false;
            var path = Path.Combine(PresetDirectory, sanitized + ".xml");
            return File.Exists(path);
        }

        /// <summary>
        /// Saves trade rules to a preset file using RimWorld's standard SafeSaver / Scribe format.
        /// </summary>
        public static bool SavePreset(string name, IEnumerable<TradeRule> rules, out string error)
        {
            error = null;
            if (name.NullOrEmpty())
            {
                error = "Preset name cannot be empty.";
                return false;
            }

            var sanitized = GenFile.SanitizedFileName(name);
            if (sanitized.NullOrEmpty())
            {
                error = "Invalid preset name.";
                return false;
            }

            try
            {
                var filePath = Path.Combine(PresetDirectory, sanitized + ".xml");
                var preset = new TradeRulePreset(name, rules);

                SafeSaver.Save(filePath, "TradeRulePreset", () =>
                {
                    Scribe_Deep.Look(ref preset, "preset");
                });

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error($"[FindAndTrade] Failed to save preset '{name}': {ex}");
                return false;
            }
        }

        /// <summary>
        /// Loads a trade rule preset from the specified XML file path.
        /// </summary>
        public static TradeRulePreset LoadPreset(string filePath, out string error)
        {
            error = null;
            if (!File.Exists(filePath))
            {
                error = "File does not exist.";
                return null;
            }

            TradeRulePreset preset = null;
            try
            {
                Scribe.loader.InitLoading(filePath);
                try
                {
                    Scribe_Deep.Look(ref preset, "preset");
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }

                if (preset == null)
                {
                    error = "Failed to load preset data.";
                    return null;
                }

                InitPresetRules(preset);
                return preset;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error($"[FindAndTrade] Failed to load preset from '{filePath}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Deletes the specified preset file.
        /// </summary>
        public static bool DeletePreset(string filePath, out string error)
        {
            error = null;
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                error = "File does not exist.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error($"[FindAndTrade] Failed to delete preset '{filePath}': {ex}");
                return false;
            }
        }

        /// <summary>
        /// Copies trade rules to the system clipboard in Scribe XML format.
        /// </summary>
        public static bool CopyToClipboard(string name, IEnumerable<TradeRule> rules)
        {
            try
            {
                var preset = new TradeRulePreset(name, rules);
                var xml = ScribeXmlFromString.SaveAsString(preset);
                GUIUtility.systemCopyBuffer = xml;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[FindAndTrade] Failed to copy preset to clipboard: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks whether the system clipboard currently contains valid TradeRulePreset XML data.
        /// </summary>
        public static bool CanPasteFromClipboard()
        {
            var text = GUIUtility.systemCopyBuffer;
            return !text.NullOrEmpty() && ScribeXmlFromString.IsValid<TradeRulePreset>(text);
        }

        /// <summary>
        /// Deserializes a TradeRulePreset from XML text in the system clipboard.
        /// </summary>
        public static TradeRulePreset PasteFromClipboard(out string error)
        {
            error = null;
            var text = GUIUtility.systemCopyBuffer;
            if (text.NullOrEmpty() || !ScribeXmlFromString.IsValid<TradeRulePreset>(text))
            {
                error = "Clipboard does not contain valid trade rule preset data.";
                return null;
            }

            try
            {
                var preset = ScribeXmlFromString.LoadFromString<TradeRulePreset>(text);
                if (preset == null)
                {
                    error = "Failed to deserialize trade rule preset from clipboard.";
                    return null;
                }

                InitPresetRules(preset);
                return preset;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error($"[FindAndTrade] Failed to paste preset from clipboard: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Applies the given preset to the game's trade rules component.
        /// When overwrite is true, existing rules are replaced. Otherwise, rules are appended.
        /// Does NOT modify or touch assigned traders.
        /// </summary>
        public static void ApplyPreset(TradeRulesGameComp comp, TradeRulePreset preset, bool overwrite)
        {
            if (comp == null || preset == null) return;

            if (overwrite)
            {
                while (comp.tradeRules.Count > 0)
                {
                    comp.tradeRules.RemoveAt(comp.tradeRules.Count - 1);
                }
            }

            foreach (var rule in preset.rules)
            {
                if (rule != null)
                {
                    var cloned = rule.Clone();
                    InitRule(cloned);
                    comp.tradeRules.Add(cloned);
                }
            }

            if (Current.Game != null && Find.CurrentMap != null)
            {
                comp.Fetch(Find.CurrentMap, true);
            }
        }

        private static void InitPresetRules(TradeRulePreset preset)
        {
            if (preset?.rules == null) return;
            foreach (var rule in preset.rules)
            {
                InitRule(rule);
            }
        }

        private static void InitRule(TradeRule rule)
        {
            if (rule == null) return;
            rule.search?.Changed();
            rule.ExportBuffer = rule.Export.ToString();
            rule.ImportBuffer = rule.Import.ToString();
        }
    }
}
