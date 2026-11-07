using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Records;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Defines pure calculation methods for computing trade order quantities, sell valuations, and rule quantity ranges.
    /// </summary>
    public interface ITradeOrderCalculator
    {
        /// <summary>
        /// Calculates the quantity of items to sell for an export or maintain rule.
        /// </summary>
        /// <param name="currentStock">The currently tracked colony stock for the item or rule.</param>
        /// <param name="exportThreshold">The minimum stock threshold to keep in the colony.</param>
        /// <param name="availableColonyCount">The number of tradeable items available to sell in the tradeable entry.</param>
        /// <returns>The positive number of items to sell, or <c>0</c> if no items should be sold.</returns>
        int CalculateSellQuantity(int currentStock, int exportThreshold, int availableColonyCount);

        /// <summary>
        /// Calculates the quantity of items to buy for an import or maintain rule.
        /// </summary>
        /// <param name="currentStock">The currently tracked colony stock for the item or rule.</param>
        /// <param name="importTarget">The desired target stock quantity in the colony.</param>
        /// <param name="availableTraderCount">The number of items held by the trader.</param>
        /// <returns>The positive number of items to buy, or <c>0</c> if no items should be bought.</returns>
        int CalculateBuyQuantity(int currentStock, int importTarget, int availableTraderCount);

        /// <summary>
        /// Calculates the sellable item count, rounded total silver value, and unit price after applying the export threshold.
        /// </summary>
        /// <param name="totalRawPrice">The total unadjusted sell value of all stacks of the item.</param>
        /// <param name="totalItems">The total stack count of the item.</param>
        /// <param name="exportThreshold">The number of items to retain (not sell).</param>
        /// <param name="sellableCount">When this method returns <c>true</c>, contains the quantity to be sold.</param>
        /// <param name="roundedTotalPrice">When this method returns <c>true</c>, contains the rounded total sell price.</param>
        /// <param name="unitPrice">When this method returns <c>true</c>, contains the effective price per item.</param>
        /// <returns><c>true</c> if there are items exceeding <paramref name="exportThreshold"/> to sell; otherwise, <c>false</c>.</returns>
        bool TryCalculateSellValuation(
            float totalRawPrice,
            int totalItems,
            int exportThreshold,
            out int sellableCount,
            out float roundedTotalPrice,
            out float unitPrice);

        /// <summary>
        /// Calculates the minimum and maximum item counts for a rule based on its <see cref="TradeRuleAggregation"/>.
        /// </summary>
        /// <param name="aggregation">The rule's aggregation mode.</param>
        /// <param name="groupedRecords">The per-<c>ThingDef</c> aggregated counts.</param>
        /// <param name="totalCount">The total sum of counts across all matched items.</param>
        /// <returns>A tuple containing the minimum and maximum counts across the rule's matched items.</returns>
        (int min, int max) CalculateRuleRange(
            TradeRuleAggregation aggregation,
            IReadOnlyList<RuleRecord> groupedRecords,
            int totalCount);
    }
}
