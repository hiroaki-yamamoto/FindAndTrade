using System;
using System.Collections.Generic;
using MGAutoSell.Filter;
using RimWorld;
using Verse;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Normalizes over-budget buy and sell orders in O(1) time per step by trimming from the end of the order list.
    /// </summary>
    /// <remarks>
    /// Unlike the legacy implementation which reversed the list and called <c>List.Remove(item)</c> at index 0 (O(K) per removal),
    /// <see cref="TradeBudgetNormalizer"/> inspects and removes from the tail (<c>orders.Count - 1</c>) in O(1) time.
    /// </remarks>
    /// <example>
    /// <code>
    /// var normalizer = new TradeBudgetNormalizer();
    /// bool removedAll = normalizer.CalculateTrimmedOrder(
    ///     currentTransferCount: 10,
    ///     itemOrderCount: 10,
    ///     totalCurrencyCost: 100f,
    ///     currencyGap: 25,
    ///     out int newTransferCount,
    ///     out int inventoryAdjustment);
    /// // removedAll == false, newTransferCount == 7, inventoryAdjustment == -3
    /// </code>
    /// </example>
    public sealed class TradeBudgetNormalizer : ITradeBudgetNormalizer
    {
        /// <summary>
        /// Gets the shared singleton instance of <see cref="TradeBudgetNormalizer"/>.
        /// </summary>
        public static readonly TradeBudgetNormalizer Instance = new();

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public bool CalculateTrimmedOrder(
            int currentTransferCount,
            int itemOrderCount,
            float totalCurrencyCost,
            int currencyGap,
            out int newTransferCount,
            out int inventoryAdjustment)
        {
            if (itemOrderCount <= 1 || currentTransferCount == 0 || totalCurrencyCost <= 0f || totalCurrencyCost < currencyGap)
            {
                newTransferCount = 0;
                inventoryAdjustment = -1;
                return true;
            }

            float costPer = totalCurrencyCost / currentTransferCount;
            int reduction = (int)(currentTransferCount < 0
                ? Math.Floor(currencyGap / costPer)
                : Math.Ceiling(currencyGap / costPer));

            if (reduction == 0)
            {
                reduction = currentTransferCount < 0 ? -1 : 1;
            }

            newTransferCount = currentTransferCount - reduction;

            // If reduction would flip the sign of the transfer or reduce it to zero, remove the order entirely.
            if ((currentTransferCount > 0 && newTransferCount <= 0) ||
                (currentTransferCount < 0 && newTransferCount >= 0))
            {
                newTransferCount = 0;
                inventoryAdjustment = -1;
                return true;
            }

            inventoryAdjustment = -reduction;
            return false;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1) amortized (removes from the end of <paramref name="orders"/> via <see cref="List{T}.RemoveAt"/>).
        /// Space Complexity: O(1).
        /// </remarks>
        public bool NormalizeStep(
            List<SellItem> orders,
            Dictionary<Tradeable, TradeRule> pairings,
            int currencyGap,
            ITradeInventoryAggregator aggregator,
            Action<Tradeable, int> setTradeCount)
        {
            if (orders == null || orders.Count == 0 || setTradeCount == null)
            {
                return false;
            }

            int lastIndex = orders.Count - 1;
            var item = orders[lastIndex];
            if (item?.Tradeable == null)
            {
                orders.RemoveAt(lastIndex);
                return orders.Count > 0;
            }

            float cost = Math.Max(
                item.Tradeable.CurTotalCurrencyCostForSource,
                item.Tradeable.CurTotalCurrencyCostForDestination);

            TradeRule matchedRule = null;
            pairings?.TryGetValue(item.Tradeable, out matchedRule);

            bool shouldRemove = CalculateTrimmedOrder(
                item.Tradeable.CountToTransfer,
                item.Count,
                cost,
                currencyGap,
                out int newTransferCount,
                out int inventoryAdjustment);

            aggregator?.AddCount(matchedRule, item.Tradeable.ThingDef, inventoryAdjustment);
            setTradeCount(item.Tradeable, newTransferCount);

            if (shouldRemove)
            {
                orders.RemoveAt(lastIndex);
            }
            else
            {
                item.Count = newTransferCount;
            }

            return true;
        }
    }
}
