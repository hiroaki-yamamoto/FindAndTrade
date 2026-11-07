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
            allOptions ??= base.AllOptions()
                .Where(static x => !string.IsNullOrWhiteSpace(x.label))
                .GroupBy(static x => x.label)
                .Select(static x => x.FirstOrDefault())
                .ToList();
    }
}
