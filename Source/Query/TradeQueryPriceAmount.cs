using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Query
{
    /// <summary>
    /// Trade query filter that evaluates whether a tradeable's unit currency price falls within a configured <see cref="FloatRange"/>.
    /// </summary>
    internal class TradeQueryPriceAmount : ThingQueryFloatRange, ITradeQuery
    {
        /// <inheritdoc />
        public override bool AppliesDirectlyTo(Thing thing)
        {
            return true;
        }

        /// <inheritdoc />
        public bool AppliesDirectlyTo(TradeContext context)
        {
            if (context?.Tradeable == null)
            {
                return false;
            }

            return MatchesPriceAmount(context.Tradeable.GetPriceFor(context.Action));
        }

        /// <summary>
        /// Evaluates whether the specified unit <paramref name="price"/> falls within the configured <see cref="ThingQueryFloatRange.sel"/> range.
        /// </summary>
        /// <param name="price">The unit price to test.</param>
        /// <returns><c>true</c> if <paramref name="price"/> is within the range; otherwise, <c>false</c>.</returns>
        internal bool MatchesPriceAmount(float price)
        {
            return price >= sel.min && price <= sel.max;
        }
    }
}
