using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Filter
{
    public class TradeRule : IExposable, IQuerySearch
    {
        public int Import;
        public int Export;
        public bool Enabled = true;
        public TradeRuleAggregation Aggregation = TradeRuleAggregation.ThingDef;
        public TradeQuerySearch search;
        public QuerySearch Search => search;
        public TradeMode Mode = TradeMode.Export;

        private int? _hash;
        public int Hash
        {
            get
            {
                _hash ??= GetHashCode();
                return _hash.Value;
            }
        }

        public string ExportBuffer;
        public string ImportBuffer;

        public bool AllowSell => (Export > 0 || NoConfig) && Mode is TradeMode.Export or TradeMode.Maintain;
        public bool AllowBuy => Import > 0 && Mode is TradeMode.Import or TradeMode.Maintain;

        public bool NoConfig => Export == 0 && (Mode == TradeMode.Export || Import == 0);
        public bool Invalid => Export > 0 && Import > 0 && Import > Export || Import < 0 || Export < 0;

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

        public TradeRule(string name)
        {
            search = new TradeQuerySearch()
            {
                name = name,
            };
            ExportBuffer = Export.ToString();
            ImportBuffer = Import.ToString();
        }

        public TradeRule Clone()
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
            catch (System.Exception ex)
            {
                Verse.Log.Warning($"[FindAndTrade] TradeRule.Clone via XML failed: {ex}");
            }

            var fallback = new TradeRule(this.search?.name ?? "")
            {
                Import = this.Import,
                Export = this.Export,
                Enabled = this.Enabled,
                Aggregation = this.Aggregation,
                Mode = this.Mode,
                ExportBuffer = this.Export.ToString(),
                ImportBuffer = this.Import.ToString()
            };
            return fallback;
        }

        public TradeRule()
        {

        }
    }

    public enum TradeMode
    {
        Export,
        Import,
        Maintain
    }
}
