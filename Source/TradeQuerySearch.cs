using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MGAutoSell.Query;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Extends <see cref="QuerySearch"/> with cached <see cref="ITradeQuery"/> evaluation for trade-specific conditions
    /// such as price type, price amount, trader kind, and orbital trader status.
    /// </summary>
    public class TradeQuerySearch : QuerySearch
    {
        /// <summary>
        /// Cached list of all <see cref="ThingDef"/> definitions that can match this query search.
        /// </summary>
        public List<ThingDef> AllItems;

        [CanBeNull]
        private ITradeQuery[] _tradeQueries;

        /// <summary>
        /// Gets the cached array of <see cref="ITradeQuery"/> children configured in this search.
        /// </summary>
        public ITradeQuery[] TradeQueries => _tradeQueries ??= children?.queries?
            .OfType<ITradeQuery>()
            .ToArray() ?? [];

        /// <summary>
        /// Initializes a new instance of the <see cref="TradeQuerySearch"/> class.
        /// </summary>
        public TradeQuerySearch()
        {
        }

        /// <summary>
        /// Refreshes the cached <see cref="AllItems"/> and <see cref="TradeQueries"/> when the search configuration changes.
        /// </summary>
        public override void Changed()
        {
            base.Changed();
            AllItems = this.GetPossibleItems();
            _tradeQueries = children?.queries?
                .OfType<ITradeQuery>()
                .ToArray() ?? [];
        }

        /// <summary>
        /// Evaluates whether all or any enabled <see cref="ITradeQuery"/> filters in this search match the given <paramref name="context"/>.
        /// </summary>
        /// <param name="context">The trade deal context to evaluate.</param>
        /// <returns><c>true</c> if the trade context satisfies the configured queries; otherwise, <c>false</c>.</returns>
        public bool AppliesTo(TradeContext context)
        {
            _tradeQueries ??= children?.queries?
                .OfType<ITradeQuery>()
                .ToArray() ?? [];

            return !MatchAllQueries
                ? TradeQueries.AnyX(
                    x => (x as ThingQuery)?.Enabled is not false && x.AppliesDirectlyTo(context),
                    Children?.anyMin ?? 1)
                : TradeQueries.All(
                    x => x is ThingQuery { Enabled: false } || x.AppliesDirectlyTo(context));
        }
    }
}
