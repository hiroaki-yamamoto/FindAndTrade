using System;
using System.Collections.Generic;
using System.Globalization;
using MGAutoSell.Filter;
using Verse;

namespace MGAutoSell.Presets
{
    /// <summary>
    /// Represents a serializable preset of trade rules that can be exported, imported, or shared via clipboard.
    /// </summary>
    public class TradeRulePreset : IExposable
    {
        /// <summary>
        /// Display name of the preset.
        /// </summary>
        public string name = "";

        /// <summary>
        /// Creation timestamp (UTC) of the preset.
        /// </summary>
        public DateTime createdAt = DateTime.UtcNow;

        /// <summary>
        /// List of configured trade rules in this preset.
        /// </summary>
        public List<TradeRule> rules = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRulePreset"/> class for deserialization.
        /// </summary>
        public TradeRulePreset()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRulePreset"/> class with a preset name and an enumerable of trade rules.
        /// Deep copies the provided trade rules so that modifying the preset does not mutate the source rules.
        /// </summary>
        /// <param name="name">The name of the preset. If null, defaults to an empty string.</param>
        /// <param name="rules">The trade rules to include in the preset. Null rules in the collection are ignored.</param>
        public TradeRulePreset(string name, IEnumerable<TradeRule> rules)
        {
            this.name = name ?? "";
            this.createdAt = DateTime.UtcNow;
            this.rules = new List<TradeRule>();
            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    if (rule != null)
                    {
                        this.rules.Add(rule.Clone());
                    }
                }
            }
        }

        /// <summary>
        /// Serializes or deserializes preset state using RimWorld's Scribe system.
        /// <para>
        /// Note: RimWorld's <see cref="ParseHelper"/> does not support <see cref="DateTime"/> directly.
        /// To avoid <see cref="ArgumentException"/> during loading, <see cref="createdAt"/> is serialized
        /// as an ISO 8601 string and parsed safely with fallback strategies for legacy locale-dependent timestamps.
        /// </para>
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref name, nameof(name), "");

            string createdAtStr = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Serialize as ISO 8601 round-trip string using InvariantCulture
                createdAtStr = createdAt.ToString("o", CultureInfo.InvariantCulture);
            }

            Scribe_Values.Look(ref createdAtStr, nameof(createdAt));

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (!createdAtStr.NullOrEmpty())
                {
                    // Attempt ISO 8601 (roundtrip), invariant culture, current culture, and general parse
                    if (DateTime.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate) ||
                        DateTime.TryParse(createdAtStr, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDate) ||
                        DateTime.TryParse(createdAtStr, out parsedDate))
                    {
                        createdAt = parsedDate;
                    }
                    else
                    {
                        createdAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    createdAt = DateTime.UtcNow;
                }
            }

            Scribe_Collections.Look(ref rules, nameof(rules), LookMode.Deep);
            rules ??= new List<TradeRule>();
        }
    }
}
