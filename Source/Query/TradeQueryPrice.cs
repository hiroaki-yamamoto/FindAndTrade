using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell.Query
{
    /// <summary>
    /// Trade query filter that evaluates whether a tradeable's <see cref="PriceType"/> falls within a configured <see cref="PriceTypeRange"/>.
    /// </summary>
    public class TradeQueryPrice : ThingQueryWithOption<PriceTypeRange>, ITradeQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TradeQueryPrice"/> class defaulting to <see cref="PriceType.Normal"/>.
        /// </summary>
        public TradeQueryPrice()
        {
            sel = new PriceTypeRange(PriceType.Normal, PriceType.Normal);
        }

        /// <inheritdoc />
        public bool AppliesDirectlyTo(TradeContext context)
        {
            if (context?.Tradeable == null)
            {
                return false;
            }

            var priceType = context.Tradeable.PriceTypeFor(context.Action);
            return MatchesPriceType(priceType);
        }

        /// <summary>
        /// Evaluates whether the specified <paramref name="priceType"/> is defined and falls within <see cref="ThingQueryWithOption{T}.sel"/>.
        /// </summary>
        /// <param name="priceType">The price type to test.</param>
        /// <returns><c>true</c> if <paramref name="priceType"/> is within the configured range; otherwise, <c>false</c>.</returns>
        internal bool MatchesPriceType(PriceType priceType)
        {
            return priceType != PriceType.Undefined && sel.Includes(priceType);
        }

        /// <inheritdoc />
        public override bool AppliesDirectlyTo(Thing thing)
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool DrawMain(Rect rect, bool locked, Rect fullRect)
        {
            base.DrawMain(rect, locked, fullRect);
            return PriceTypeRange.Widget(fullRect.RightHalfClamped(row.FinalX), this.id, ref _sel);
        }
    }
}
