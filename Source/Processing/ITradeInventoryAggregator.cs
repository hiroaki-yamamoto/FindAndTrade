using MGAutoSell.Filter;
using Verse;

namespace MGAutoSell.Processing
{
    /// <summary>
    /// Defines an abstraction for tracking and aggregating item quantities across <see cref="ThingDef"/>
    /// and <see cref="TradeRule"/> scopes during single or grouped trading sessions.
    /// </summary>
    /// <remarks>
    /// Follows the Single Responsibility and Dependency Inversion principles by isolating mutable aggregation state
    /// from trade execution orchestration.
    /// </remarks>
    public interface ITradeInventoryAggregator
    {
        /// <summary>
        /// Gets a value indicating whether a grouped multi-trader session is currently active.
        /// </summary>
        bool IsGroupedSessionActive { get; }

        /// <summary>
        /// Starts a grouped trading session and resets all tracked aggregations.
        /// </summary>
        /// <returns><c>true</c> if the session was started; <c>false</c> if a grouped session was already active.</returns>
        bool StartGroupedSession();

        /// <summary>
        /// Ends an active grouped trading session and clears all tracked aggregations.
        /// </summary>
        /// <returns><c>true</c> if the session was ended; <c>false</c> if no grouped session was active.</returns>
        bool EndGroupedSession();

        /// <summary>
        /// Clears all tracked aggregations if a grouped session is not currently active.
        /// </summary>
        void ClearIfNotGrouped();

        /// <summary>
        /// Unconditionally clears all tracked <see cref="ThingDef"/> and <see cref="TradeRule"/> aggregations.
        /// </summary>
        void Clear();

        /// <summary>
        /// Registers the initial colony stock count for a <see cref="ThingDef"/> if it has not already been recorded
        /// in the current session.
        /// </summary>
        /// <param name="def">The item definition to register.</param>
        /// <param name="count">The initial quantity held by the colony.</param>
        /// <returns><c>true</c> if the count was recorded; <c>false</c> if an entry for <paramref name="def"/> already existed.</returns>
        bool TryAddInitialCount(ThingDef def, int count);

        /// <summary>
        /// Retrieves the currently tracked stock quantity for the given <paramref name="rule"/> and <paramref name="def"/>
        /// based on the rule's <see cref="TradeRule.Aggregation"/> mode.
        /// </summary>
        /// <param name="rule">The trade rule being evaluated.</param>
        /// <param name="def">The item definition being evaluated.</param>
        /// <returns>The tracked quantity, or <c>0</c> if no quantity has been recorded.</returns>
        int GetCount(TradeRule rule, ThingDef def);

        /// <summary>
        /// Adds a signed delta <paramref name="amount"/> to the tracked stock quantity for the given
        /// <paramref name="rule"/> and <paramref name="def"/>.
        /// </summary>
        /// <param name="rule">The trade rule being evaluated, or <c>null</c> to default to <see cref="TradeRuleAggregation.ThingDef"/>.</param>
        /// <param name="def">The item definition being updated.</param>
        /// <param name="amount">The positive (bought/found) or negative (sold) delta to apply.</param>
        void AddCount(TradeRule rule, ThingDef def, int amount);
    }
}
