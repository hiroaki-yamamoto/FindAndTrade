using System;
using System.Collections.Generic;
using MGAutoSell.Filter;
using MGAutoSell.Records;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Provides deterministic, allocation-free calculations for trade order quantities, sell valuations,
    /// and rule usage statistics.
    /// </summary>
    /// <example>
    /// <code>
    /// var calculator = new TradeOrderCalculator();
    /// int toSell = calculator.CalculateSellQuantity(currentStock: 150, exportThreshold: 100, availableColonyCount: 80); // 50
    /// int toBuy = calculator.CalculateBuyQuantity(currentStock: 20, importTarget: 50, availableTraderCount: 15); // 15
    /// </code>
    /// </example>
    public sealed class TradeOrderCalculator : ITradeOrderCalculator
    {
        /// <summary>
        /// Gets the shared singleton instance of <see cref="TradeOrderCalculator"/>.
        /// </summary>
        public static readonly TradeOrderCalculator Instance = new();

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public int CalculateSellQuantity(int currentStock, int exportThreshold, int availableColonyCount)
        {
            if (availableColonyCount <= 0 || currentStock <= exportThreshold)
            {
                return 0;
            }

            int excess = currentStock - exportThreshold;
            return Math.Min(excess, availableColonyCount);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public int CalculateBuyQuantity(int currentStock, int importTarget, int availableTraderCount)
        {
            if (availableTraderCount <= 0 || currentStock >= importTarget)
            {
                return 0;
            }

            int deficit = importTarget - currentStock;
            return Math.Min(deficit, availableTraderCount);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public bool TryCalculateSellValuation(
            float totalRawPrice,
            int totalItems,
            int exportThreshold,
            out int sellableCount,
            out float roundedTotalPrice,
            out float unitPrice)
        {
            int clampThreshold = Math.Max(0, exportThreshold);
            if (totalItems <= 0 || totalItems <= clampThreshold)
            {
                sellableCount = 0;
                roundedTotalPrice = 0f;
                unitPrice = 0f;
                return false;
            }

            float rawPricePer = totalRawPrice / totalItems;
            float adjustedTotal = totalRawPrice - (rawPricePer * clampThreshold);
            sellableCount = totalItems - clampThreshold;
            roundedTotalPrice = (float)Math.Round(adjustedTotal, 0);
            unitPrice = roundedTotalPrice / sellableCount;
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1) when <paramref name="aggregation"/> is <see cref="TradeRuleAggregation.Rule"/>;
        /// O(G) single pass when <see cref="TradeRuleAggregation.ThingDef"/>, where G is the number of grouped records.
        /// Space Complexity: O(1).
        /// </remarks>
        public (int min, int max) CalculateRuleRange(
            TradeRuleAggregation aggregation,
            IReadOnlyList<RuleRecord> groupedRecords,
            int totalCount)
        {
            if (groupedRecords == null || groupedRecords.Count == 0)
            {
                return (0, 0);
            }

            if (aggregation == TradeRuleAggregation.Rule)
            {
                return (totalCount, totalCount);
            }

            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = 0; i < groupedRecords.Count; i++)
            {
                int count = groupedRecords[i].Count;
                if (count < min)
                {
                    min = count;
                }

                if (count > max)
                {
                    max = count;
                }
            }

            return (min, max);
        }
    }
}
