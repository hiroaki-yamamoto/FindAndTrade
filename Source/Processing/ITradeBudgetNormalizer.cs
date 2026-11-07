using System;
using System.Collections.Generic;
using MGAutoSell.Filter;
using RimWorld;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Defines a service for normalizing over-budget buy and sell orders so trade deals remain within available currency.
    /// </summary>
    public interface ITradeBudgetNormalizer
    {
        /// <summary>
        /// Calculates the adjusted transfer count and reduction delta needed to close a currency gap.
        /// </summary>
        /// <param name="currentTransferCount">The current signed transfer count on the tradeable (positive for buy, negative for sell).</param>
        /// <param name="itemOrderCount">The positive order count tracked in the normalization queue.</param>
        /// <param name="totalCurrencyCost">The total currency cost of the current transfer on the tradeable.</param>
        /// <param name="currencyGap">The positive currency deficit that needs to be resolved.</param>
        /// <param name="newTransferCount">When this method returns, contains the updated transfer count (<c>0</c> if the order is completely removed).</param>
        /// <param name="inventoryAdjustment">When this method returns, contains the signed adjustment to apply to the inventory aggregator.</param>
        /// <returns><c>true</c> if the order was completely removed; <c>false</c> if it was partially reduced.</returns>
        bool CalculateTrimmedOrder(
            int currentTransferCount,
            int itemOrderCount,
            float totalCurrencyCost,
            int currencyGap,
            out int newTransferCount,
            out int inventoryAdjustment);

        /// <summary>
        /// Executes a single normalization step by trimming or removing the lowest-priority order at the end of <paramref name="orders"/>.
        /// </summary>
        /// <param name="orders">The list of active orders ordered from highest priority (index 0) to lowest priority (last index).</param>
        /// <param name="pairings">A mapping of tradeables to the <see cref="TradeRule"/> that claimed them.</param>
        /// <param name="currencyGap">The positive currency deficit to resolve.</param>
        /// <param name="aggregator">The inventory aggregator to update.</param>
        /// <param name="setTradeCount">Callback that sets the <c>CountToTransfer</c> on a <see cref="Tradeable"/>.</param>
        /// <param name="costEvaluator">Optional delegate to evaluate the total currency cost of a <see cref="Tradeable"/>; defaults to the maximum of source and destination currency costs.</param>
        /// <returns><c>true</c> if an order was trimmed or removed; <c>false</c> if no orders remain.</returns>
        bool NormalizeStep(
            List<SellItem> orders,
            Dictionary<Tradeable, TradeRule> pairings,
            int currencyGap,
            ITradeInventoryAggregator aggregator,
            Action<Tradeable, int> setTradeCount,
            Func<Tradeable, float> costEvaluator = null);
    }
}
