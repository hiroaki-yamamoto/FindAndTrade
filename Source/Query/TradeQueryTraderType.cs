using System.Collections.Generic;
using System.Linq;
using RimWorld;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Query
{
    /// <summary>
    /// Trade query filter that matches when the active trader's <see cref="TraderKindDef"/> label matches the selected option.
    /// </summary>
    internal class TradeQueryTraderType : ThingQueryDropDown<TraderKindDef>, ITradeQuery
    {
        /// <inheritdoc />
        public override bool AppliesDirectlyTo(Thing thing) => true;

        /// <inheritdoc />
        public bool AppliesDirectlyTo(TradeContext context)
        {
            var traderKind = TradeSession.trader?.TraderKind;
            return traderKind != null && sel != null && traderKind.label == sel.label;
        }

        /// <inheritdoc />
        public override string TipForSel() => null;

        private List<TraderKindDef> allOptions;

        /// <inheritdoc />
        public override IEnumerable<TraderKindDef> AllOptions() =>
            allOptions ??= FilterDistinctOptions(base.AllOptions());

        /// <summary>
        /// Filters out null or unlabeled <see cref="TraderKindDef"/> entries and deduplicates by <see cref="Def.label"/> in O(N) time.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N) single pass using a <see cref="HashSet{T}"/> of labels instead of LINQ <c>GroupBy</c>.
        /// Space Complexity: O(U) where U is the number of unique labels.
        /// </remarks>
        /// <param name="options">The source sequence of <see cref="TraderKindDef"/> definitions.</param>
        /// <returns>A deduplicated list of <see cref="TraderKindDef"/> definitions with non-empty labels.</returns>
        internal static List<TraderKindDef> FilterDistinctOptions(IEnumerable<TraderKindDef> options)
        {
            if (options == null)
            {
                return [];
            }

            var seenLabels = new HashSet<string>();
            var result = new List<TraderKindDef>();
            foreach (var option in options)
            {
                if (option != null && !string.IsNullOrWhiteSpace(option.label) && seenLabels.Add(option.label))
                {
                    result.Add(option);
                }
            }

            return result;
        }
    }
}
