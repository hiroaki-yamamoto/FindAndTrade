using System;
using System.Collections.Generic;
using MGAutoSell.Filter;
using Verse;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Default implementation of <see cref="ITradeInventoryAggregator"/> using hash-based dictionaries for O(1)
    /// stock lookups and updates across <see cref="ThingDef"/> and <see cref="TradeRule"/> aggregation scopes.
    /// </summary>
    /// <example>
    /// <code>
    /// var aggregator = new TradeInventoryAggregator();
    /// var rule = new TradeRule("BulkRule") { Aggregation = TradeRuleAggregation.Rule };
    /// aggregator.AddCount(rule, null, 50);
    /// int current = aggregator.GetCount(rule, null); // Returns 50
    /// </code>
    /// </example>
    public sealed class TradeInventoryAggregator : ITradeInventoryAggregator
    {
        private readonly Dictionary<ThingDef, int> _thingDefAggregations = new();
        private readonly Dictionary<TradeRule, int> _tradeRuleAggregations = new();

        /// <inheritdoc />
        public bool IsGroupedSessionActive { get; private set; }

        /// <inheritdoc />
        public bool StartGroupedSession()
        {
            if (IsGroupedSessionActive)
            {
                return false;
            }

            Clear();
            IsGroupedSessionActive = true;
            return true;
        }

        /// <inheritdoc />
        public bool EndGroupedSession()
        {
            if (!IsGroupedSessionActive)
            {
                return false;
            }

            Clear();
            IsGroupedSessionActive = false;
            return true;
        }

        /// <inheritdoc />
        public void ClearIfNotGrouped()
        {
            if (!IsGroupedSessionActive)
            {
                Clear();
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            _thingDefAggregations.Clear();
            _tradeRuleAggregations.Clear();
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1) average. Space Complexity: O(1).
        /// </remarks>
        public bool TryAddInitialCount(ThingDef def, int count)
        {
            if (def == null)
            {
                return false;
            }

            return _thingDefAggregations.TryAdd(def, count);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1) average. Space Complexity: O(1).
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="TradeRule.Aggregation"/> has an unknown value.</exception>
        public int GetCount(TradeRule rule, ThingDef def)
        {
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            return rule.Aggregation switch
            {
                TradeRuleAggregation.ThingDef => def != null ? _thingDefAggregations.GetValueOrDefault(def, 0) : 0,
                TradeRuleAggregation.Rule => _tradeRuleAggregations.GetValueOrDefault(rule, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Aggregation, "Unsupported trade rule aggregation mode.")
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// Time Complexity: O(1) average. Space Complexity: O(1).
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="TradeRule.Aggregation"/> has an unknown value.</exception>
        public void AddCount(TradeRule rule, ThingDef def, int amount)
        {
            var aggregation = rule?.Aggregation ?? TradeRuleAggregation.ThingDef;
            switch (aggregation)
            {
                case TradeRuleAggregation.ThingDef:
                    if (def == null)
                    {
                        return;
                    }

                    if (!_thingDefAggregations.TryAdd(def, amount))
                    {
                        _thingDefAggregations[def] += amount;
                    }
                    break;

                case TradeRuleAggregation.Rule:
                    if (rule == null)
                    {
                        return;
                    }

                    if (!_tradeRuleAggregations.TryAdd(rule, amount))
                    {
                        _tradeRuleAggregations[rule] += amount;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(rule), aggregation, "Unsupported trade rule aggregation mode.");
            }
        }
    }
}
