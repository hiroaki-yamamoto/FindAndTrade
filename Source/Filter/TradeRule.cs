using System;
using System.Runtime.CompilerServices;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Filter
{
    /// <summary>
    /// Represents a configurable automated trade rule combining a <see cref="TradeQuerySearch"/> filter
    /// with import/export thresholds, operational <see cref="TradeMode"/>, and <see cref="TradeRuleAggregation"/> scope.
    /// </summary>
    /// <example>
    /// <code>
    /// var rule = new TradeRule
    /// {
    ///     Mode = TradeMode.Maintain,
    ///     Import = 20,
    ///     Export = 50,
    ///     Aggregation = TradeRuleAggregation.ThingDef
    /// };
    /// bool canBuy = rule.AllowBuy;   // true
    /// bool canSell = rule.AllowSell; // true
    /// bool invalid = rule.Invalid;   // false
    /// </code>
    /// </example>
    public class TradeRule : IExposable, IQuerySearch
    {
        /// <summary>
        /// Target minimum quantity to purchase up to when <see cref="Mode"/> is <see cref="TradeMode.Import"/> or <see cref="TradeMode.Maintain"/>.
        /// </summary>
        public int Import;

        /// <summary>
        /// Target maximum quantity to sell down to when <see cref="Mode"/> is <see cref="TradeMode.Export"/> or <see cref="TradeMode.Maintain"/>.
        /// </summary>
        public int Export;

        /// <summary>
        /// Indicates whether this rule is active and evaluated during trading.
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// Specifies whether item quantities are counted per <see cref="ThingDef"/> or across the entire <see cref="TradeRule"/>.
        /// </summary>
        public TradeRuleAggregation Aggregation = TradeRuleAggregation.ThingDef;

        /// <summary>
        /// The underlying query search filter defining which items match this rule.
        /// </summary>
        public TradeQuerySearch search;

        /// <inheritdoc />
        public QuerySearch Search => search;

        /// <summary>
        /// The operational mode (<see cref="TradeMode.Export"/>, <see cref="TradeMode.Import"/>, or <see cref="TradeMode.Maintain"/>).
        /// </summary>
        public TradeMode Mode = TradeMode.Export;

        private int? _hash;

        /// <summary>
        /// Gets a cached hash code for this rule instance, used as a stable tooltip and UI control identifier.
        /// </summary>
        public int Hash => _hash ??= GetHashCode();

        /// <summary>
        /// Text buffer for the export threshold numeric input box.
        /// </summary>
        public string ExportBuffer;

        /// <summary>
        /// Text buffer for the import target numeric input box.
        /// </summary>
        public string ImportBuffer;

        /// <summary>
        /// Gets a value indicating whether this rule is permitted to sell matching items.
        /// </summary>
        public bool AllowSell => (Export > 0 || NoConfig) && Mode is TradeMode.Export or TradeMode.Maintain;

        /// <summary>
        /// Gets a value indicating whether this rule is permitted to buy matching items.
        /// </summary>
        public bool AllowBuy => Import > 0 && Mode is TradeMode.Import or TradeMode.Maintain;

        /// <summary>
        /// Gets a value indicating whether the rule has no explicit export threshold configured.
        /// </summary>
        public bool NoConfig => Export == 0 && (Mode == TradeMode.Export || Import == 0);

        /// <summary>
        /// Gets a value indicating whether the rule's import and export thresholds are in an invalid state
        /// (e.g., negative thresholds or <see cref="Import"/> exceeding <see cref="Export"/> when both are positive).
        /// </summary>
        public bool Invalid => (Export > 0 && Import > 0 && Import > Export) || Import < 0 || Export < 0;

        /// <summary>
        /// Serializes and deserializes rule fields via RimWorld's Scribe system.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Deep.Look(ref search, nameof(search));
            Scribe_Values.Look(ref Import, nameof(Import));
            Scribe_Values.Look(ref Export, nameof(Export));
            Scribe_Values.Look(ref Enabled, nameof(Enabled));
            Scribe_Values.Look(ref Aggregation, nameof(Aggregation));
            Scribe_Values.Look(ref Mode, nameof(Mode));

            ExportBuffer = Export.ToString();
            ImportBuffer = Import.ToString();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeRule"/> class with the specified display <paramref name="name"/>.
        /// </summary>
        /// <param name="name">Display name for the rule's query search.</param>
        public TradeRule(string name)
        {
            search = new TradeQuerySearch
            {
                name = name,
            };
            ExportBuffer = Export.ToString();
            ImportBuffer = Import.ToString();
        }

        /// <summary>
        /// Creates a deep copy of this <see cref="TradeRule"/>.
        /// Uses <see cref="ScribeXmlFromString"/> when a full query tree is present in a running RimWorld session,
        /// and falls back to direct member copying otherwise.
        /// </summary>
        /// <returns>A new <see cref="TradeRule"/> instance with identical configuration.</returns>
        public TradeRule Clone()
        {
            if (search?.Children?.queries != null)
            {
                try
                {
                    var xml = ScribeXmlFromString.SaveAsString(this);
                    var copy = ScribeXmlFromString.LoadFromString<TradeRule>(xml);
                    if (copy != null)
                    {
                        copy.search?.Changed();
                        copy.ExportBuffer = copy.Export.ToString();
                        copy.ImportBuffer = copy.Import.ToString();
                        return copy;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        Log.Warning($"[FindAndTrade] TradeRule.Clone via XML failed: {ex}");
                    }
                    catch
                    {
                        // Ignore logging failures when running outside the RimWorld runtime in unit tests.
                    }
                }
            }

            TradeQuerySearch clonedSearch = null;
            if (this.search != null)
            {
                clonedSearch = (TradeQuerySearch)RuntimeHelpers.GetUninitializedObject(typeof(TradeQuerySearch));
                clonedSearch.name = this.search.name;
                if (this.search.AllItems != null)
                {
                    clonedSearch.AllItems = [.. this.search.AllItems];
                }
            }

            return new TradeRule
            {
                search = clonedSearch,
                Import = this.Import,
                Export = this.Export,
                Enabled = this.Enabled,
                Aggregation = this.Aggregation,
                Mode = this.Mode,
                ExportBuffer = this.Export.ToString(),
                ImportBuffer = this.Import.ToString()
            };
        }

        /// <summary>
        /// Initializes a new empty instance of the <see cref="TradeRule"/> class for Scribe deserialization or unit testing.
        /// </summary>
        public TradeRule()
        {
            ExportBuffer = Export.ToString();
            ImportBuffer = Import.ToString();
        }
    }

    /// <summary>
    /// Defines the trading direction for a <see cref="TradeRule"/>.
    /// </summary>
    public enum TradeMode
    {
        /// <summary>Sell excess colony items down to <see cref="TradeRule.Export"/>.</summary>
        Export,

        /// <summary>Buy items from traders up to <see cref="TradeRule.Import"/>.</summary>
        Import,

        /// <summary>Maintain colony inventory between <see cref="TradeRule.Import"/> and <see cref="TradeRule.Export"/>.</summary>
        Maintain
    }
}
