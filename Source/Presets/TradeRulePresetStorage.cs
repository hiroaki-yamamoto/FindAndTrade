using System;
using System.Collections.Generic;
using System.IO;
using MGAutoSell.Filter;
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
        /// <summary>
        /// Gets the display name of the preset (file name without extension).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the full file system path to the preset XML file.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Gets the last modified timestamp of the preset file, or <see cref="DateTime.MinValue"/> if unavailable.
        /// </summary>
        public DateTime LastWriteTime { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRulePresetHeader"/> class from a file path.
        /// </summary>
        /// <param name="filePath">The path to the preset XML file.</param>
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
        /// <summary>
        /// Default subfolder name under RimWorld's SaveData directory for storing presets.
        /// </summary>
        public const string FolderName = "TradeRulePresets";

        /// <summary>
        /// Gets or sets an optional override directory path for storing presets (used for testing).
        /// When <c>null</c>, defaults to <c>Path.Combine(GenFilePaths.SaveDataFolderPath, FolderName)</c>.
        /// </summary>
        public static string CustomPresetDirectory { get; set; }

        /// <summary>
        /// Gets or sets the delegate used to read text from the system clipboard.
        /// </summary>
        public static Func<string> ClipboardGetter { get; set; } = static () => GUIUtility.systemCopyBuffer;

        /// <summary>
        /// Gets or sets the delegate used to write text to the system clipboard.
        /// </summary>
        public static Action<string> ClipboardSetter { get; set; } = static value => GUIUtility.systemCopyBuffer = value;

        /// <summary>
        /// Absolute path to the trade rule presets directory under user's save data.
        /// </summary>
        public static string PresetDirectory
        {
            get
            {
                string dir = CustomPresetDirectory ?? Path.Combine(GenFilePaths.SaveDataFolderPath, FolderName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return dir;
            }
        }

        /// <summary>
        /// Retrieves all saved preset headers, sorted by last modified date descending.
        /// Time complexity: O(N log N) where N is number of preset files.
        /// </summary>
        /// <returns>A list of <see cref="TradeRulePresetHeader"/> objects sorted newest first.</returns>
        public static List<TradeRulePresetHeader> GetPresetHeaders()
        {
            var list = new List<TradeRulePresetHeader>();
            try
            {
                string dir = PresetDirectory;
                if (!Directory.Exists(dir))
                {
                    return list;
                }

                var files = Directory.GetFiles(dir, "*.xml");
                for (int i = 0; i < files.Length; i++)
                {
                    list.Add(new TradeRulePresetHeader(files[i]));
                }

                list.Sort(static (a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
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
        /// <param name="name">The preset name to check.</param>
        /// <returns><c>true</c> if the corresponding XML file exists; otherwise, <c>false</c>.</returns>
        public static bool PresetExists(string name)
        {
            string sanitized = SanitizePresetFileName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                return false;
            }

            string path = Path.Combine(PresetDirectory, sanitized + ".xml");
            return File.Exists(path);
        }

        /// <summary>
        /// Saves trade rules to a preset file using RimWorld's standard SafeSaver / Scribe format.
        /// </summary>
        /// <param name="name">The name of the preset.</param>
        /// <param name="rules">The trade rules to save.</param>
        /// <param name="error">When this method returns <c>false</c>, contains the error message.</param>
        /// <returns><c>true</c> if the preset was saved successfully; otherwise, <c>false</c>.</returns>
        public static bool SavePreset(string name, IEnumerable<TradeRule> rules, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name))
            {
                error = "Preset name cannot be empty.";
                return false;
            }

            string sanitized = SanitizePresetFileName(name);
            if (string.IsNullOrEmpty(sanitized))
            {
                error = "Invalid preset name.";
                return false;
            }

            try
            {
                string filePath = Path.Combine(PresetDirectory, sanitized + ".xml");
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
        /// <param name="filePath">The full path to the preset XML file.</param>
        /// <param name="error">When this method returns <c>null</c>, contains the error message.</param>
        /// <returns>The loaded <see cref="TradeRulePreset"/>, or <c>null</c> if loading failed.</returns>
        public static TradeRulePreset LoadPreset(string filePath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
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
        /// <param name="filePath">The full path to the preset file to delete.</param>
        /// <param name="error">When this method returns <c>false</c>, contains the error message.</param>
        /// <returns><c>true</c> if the file was deleted; otherwise, <c>false</c>.</returns>
        public static bool DeletePreset(string filePath, out string error)
        {
            error = null;
            try
            {
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
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
        /// <param name="name">The name to assign to the clipboard preset.</param>
        /// <param name="rules">The trade rules to serialize.</param>
        /// <returns><c>true</c> if the rules were copied successfully; otherwise, <c>false</c>.</returns>
        public static bool CopyToClipboard(string name, IEnumerable<TradeRule> rules)
        {
            try
            {
                var preset = new TradeRulePreset(name, rules);
                string xml = ScribeXmlFromString.SaveAsString(preset);
                ClipboardSetter(xml);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[FindAndTrade] Failed to copy preset to clipboard: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Checks whether the system clipboard currently contains valid <see cref="TradeRulePreset"/> XML data.
        /// </summary>
        /// <returns><c>true</c> if valid preset XML is on the clipboard; otherwise, <c>false</c>.</returns>
        public static bool CanPasteFromClipboard()
        {
            string text = ClipboardGetter();
            return !string.IsNullOrEmpty(text) && ScribeXmlFromString.IsValid<TradeRulePreset>(text);
        }

        /// <summary>
        /// Deserializes a <see cref="TradeRulePreset"/> from XML text in the system clipboard.
        /// </summary>
        /// <param name="error">When this method returns <c>null</c>, contains the error message.</param>
        /// <returns>The deserialized <see cref="TradeRulePreset"/>, or <c>null</c> if clipboard contents are invalid.</returns>
        public static TradeRulePreset PasteFromClipboard(out string error)
        {
            error = null;
            string text = ClipboardGetter();
            if (string.IsNullOrEmpty(text) || !ScribeXmlFromString.IsValid<TradeRulePreset>(text))
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
        /// When <paramref name="overwrite"/> is <c>true</c>, existing rules are replaced. Otherwise, rules are appended.
        /// Does NOT modify or touch assigned traders.
        /// </summary>
        /// <param name="comp">The target <see cref="TradeRulesGameComp"/>.</param>
        /// <param name="preset">The preset whose rules should be applied.</param>
        /// <param name="overwrite">Whether to clear existing rules before applying the preset.</param>
        public static void ApplyPreset(TradeRulesGameComp comp, TradeRulePreset preset, bool overwrite)
        {
            if (comp == null || preset == null)
            {
                return;
            }

            ApplyPresetToCollection(comp.tradeRules, preset, overwrite);

            if (Current.Game != null && Find.CurrentMap != null)
            {
                comp.Fetch(Find.CurrentMap, true);
            }
        }

        /// <summary>
        /// Applies the rules from <paramref name="preset"/> to the target <paramref name="targetRules"/> list,
        /// replacing or appending according to <paramref name="overwrite"/>.
        /// </summary>
        /// <param name="targetRules">The target list of <see cref="TradeRule"/>s to modify.</param>
        /// <param name="preset">The source preset.</param>
        /// <param name="overwrite">If <c>true</c>, removes all existing rules from <paramref name="targetRules"/> first.</param>
        public static void ApplyPresetToCollection(IList<TradeRule> targetRules, TradeRulePreset preset, bool overwrite)
        {
            if (targetRules == null || preset?.rules == null)
            {
                return;
            }

            if (overwrite)
            {
                while (targetRules.Count > 0)
                {
                    targetRules.RemoveAt(targetRules.Count - 1);
                }
            }

            for (int i = 0; i < preset.rules.Count; i++)
            {
                var rule = preset.rules[i];
                if (rule != null)
                {
                    var cloned = rule.Clone();
                    InitRule(cloned);
                    targetRules.Add(cloned);
                }
            }
        }

        /// <summary>
        /// Sanitizes a preset name into a safe file name without extension.
        /// </summary>
        /// <param name="name">The raw user-supplied preset name.</param>
        /// <returns>A sanitized file name, or an empty string if <paramref name="name"/> contains no valid characters.</returns>
        public static string SanitizePresetFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            try
            {
                return GenFile.SanitizedFileName(name);
            }
            catch
            {
                var invalidChars = Path.GetInvalidFileNameChars();
                var chars = name.Trim().ToCharArray();
                int writeIdx = 0;
                for (int i = 0; i < chars.Length; i++)
                {
                    if (Array.IndexOf(invalidChars, chars[i]) < 0)
                    {
                        chars[writeIdx++] = chars[i];
                    }
                }

                return new string(chars, 0, writeIdx);
            }
        }

        private static void InitPresetRules(TradeRulePreset preset)
        {
            if (preset?.rules == null)
            {
                return;
            }

            for (int i = 0; i < preset.rules.Count; i++)
            {
                InitRule(preset.rules[i]);
            }
        }

        private static void InitRule(TradeRule rule)
        {
            if (rule == null)
            {
                return;
            }

            rule.search?.Changed();
            rule.ExportBuffer = rule.Export.ToString();
            rule.ImportBuffer = rule.Import.ToString();
        }
    }
}
