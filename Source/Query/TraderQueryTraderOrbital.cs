using RimWorld;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Query
{
    /// <summary>
    /// Trade query filter that matches when the active trader is an orbital trade ship (<see cref="TraderKindDef.orbital"/>).
    /// </summary>
    internal class TradeQueryTraderOrbital : ThingQuery, ITradeQuery
    {
        /// <inheritdoc />
        public override bool AppliesDirectlyTo(Thing thing) => true;

        /// <inheritdoc />
        public bool AppliesDirectlyTo(TradeContext context) =>
            TradeSession.trader?.TraderKind is { orbital: true };
    }
}
