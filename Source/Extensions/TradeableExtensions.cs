using RimWorld;
using Verse;

namespace MGAutoSell.Extensions
{
    /// <summary>
    /// Extension methods for inspecting <see cref="Tradeable"/> instances during automated trade evaluation.
    /// </summary>
    public static class TradeableExtensions
    {
        /// <summary>
        /// Attempts to find any <see cref="Thing"/> in <paramref name="tradeable"/> that is not designated with
        /// <see cref="MGDesignatorDefOf.MGAutoSell"/> (junk).
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(1) in the common case when <see cref="Tradeable.AnyThing"/> or <see cref="Tradeable.FirstThingTrader"/>
        /// is not designated as junk; O(C) in the worst case where C is the number of colony stacks.
        /// Space Complexity: O(1) (no closure or enumerator allocations).
        /// </remarks>
        /// <param name="tradeable">The tradeable row to inspect.</param>
        /// <param name="item">When this method returns <c>true</c>, contains the first non-junk <see cref="Thing"/> found.</param>
        /// <returns><c>true</c> if a non-junk item exists in <paramref name="tradeable"/>; otherwise, <c>false</c>.</returns>
        public static bool AnyThingNotJunk(this Tradeable tradeable, out Thing item)
        {
            item = tradeable?.AnyThing;
            if (item == null)
            {
                return false;
            }

            var designator = MGDesignatorDefOf.MGAutoSell;

            // Traders don't have a map, and therefore can't designate junk
            if (item.Map?.designationManager?.DesignationOn(item, designator) == null)
            {
                return true;
            }

            item = tradeable.FirstThingTrader;
            if (item != null)
            {
                return true;
            }

            var colonyThings = tradeable.thingsColony;
            if (colonyThings != null)
            {
                for (int i = 0; i < colonyThings.Count; i++)
                {
                    var candidate = colonyThings[i];
                    if (candidate != null && candidate.Map?.designationManager?.DesignationOn(candidate, designator) == null)
                    {
                        item = candidate;
                        return true;
                    }
                }
            }

            item = null;
            return false;
        }
    }
}
