using System.Collections.Generic;

namespace MGAutoSell.Caravans
{
    /// <summary>
    /// Evaluates settlement trade route priorities and selects the optimal route in O(N) time and O(1) space.
    /// </summary>
    /// <example>
    /// <code>
    /// var calculator = new TradeRouteCalculator();
    /// var priority = calculator.EvaluatePriority(sales: 500f, fuelCost: 50f, traderSilver: 1000f);
    /// // priority == SettlementTradePriority.High
    /// </code>
    /// </example>
    public sealed class TradeRouteCalculator : ITradeRouteCalculator
    {
        /// <summary>
        /// Gets the shared singleton instance of <see cref="TradeRouteCalculator"/>.
        /// </summary>
        public static readonly TradeRouteCalculator Instance = new();

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public float CalculateExpectedSales(float traderWillingTotal, float traderSilver, float colonyTotalSilver)
        {
            return traderWillingTotal > traderSilver ? traderSilver : colonyTotalSilver;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1). Space Complexity: O(1).
        /// </remarks>
        public SettlementTradePriority EvaluatePriority(float sales, float fuelCost, float traderSilver)
        {
            if (fuelCost <= 0f)
            {
                return SettlementTradePriority.Backup;
            }

            float ratio = sales / fuelCost;
            return ratio switch
            {
                > 4f when traderSilver > sales => SettlementTradePriority.High,
                >= 4f => SettlementTradePriority.Medium,
                > 2f => SettlementTradePriority.Low,
                _ => SettlementTradePriority.Backup
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(N) single pass over <paramref name="routes"/> instead of O(N log N) sorting.
        /// Space Complexity: O(1) auxiliary memory.
        /// </remarks>
        public TradeRoute SelectBestRoute(IEnumerable<TradeRoute> routes)
        {
            if (routes == null)
            {
                return null;
            }

            TradeRoute best = null;
            foreach (var candidate in routes)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (best == null || IsBetterRoute(candidate, best))
                {
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Determines whether <paramref name="candidate"/> is strictly better than <paramref name="currentBest"/>.
        /// </summary>
        /// <param name="candidate">The candidate route to compare.</param>
        /// <param name="currentBest">The best route found so far.</param>
        /// <returns><c>true</c> if <paramref name="candidate"/> ranks higher than <paramref name="currentBest"/>; otherwise, <c>false</c>.</returns>
        public static bool IsBetterRoute(TradeRoute candidate, TradeRoute currentBest)
        {
            if (currentBest == null)
            {
                return candidate != null;
            }

            if (candidate == null)
            {
                return false;
            }

            int priorityComparison = candidate.Priority.CompareTo(currentBest.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison > 0;
            }

            return candidate.Silver > currentBest.Silver;
        }
    }
}
