using System;
using System.Collections.Generic;

namespace MGAutoSell
{
    /// <summary>
    /// Provides statistical helper functions for performance benchmarking and UI render time analysis.
    /// </summary>
    /// <remarks>
    /// All methods in this class are designed to minimize heap allocations and avoid LINQ iterator overhead.
    /// </remarks>
    public static class StatisticsUtility
    {
        /// <summary>
        /// Computes the statistical median of a non-empty list of 64-bit integer values.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(N log N) where N is the number of elements in <paramref name="values"/>, using
        /// primitive array sorting (<see cref="Array.Sort(Array)"/>) instead of LINQ <c>OrderBy</c>.
        /// Space Complexity: O(N) for a single contiguous <see cref="long"/> array copy so the input list is not mutated.
        /// </remarks>
        /// <param name="values">The list of sample values to evaluate. Must not be null or empty.</param>
        /// <returns>
        /// The middle value if <paramref name="values"/> has an odd count, or the arithmetic mean of the two middle values
        /// if <paramref name="values"/> has an even count.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <paramref name="values"/> is <c>null</c> or contains no elements.
        /// </exception>
        /// <example>
        /// <code>
        /// var oddSamples = new List&lt;long&gt; { 30L, 10L, 20L };
        /// float oddMedian = StatisticsUtility.Median(oddSamples); // Returns 20f
        ///
        /// var evenSamples = new List&lt;long&gt; { 40L, 10L, 20L, 30L };
        /// float evenMedian = StatisticsUtility.Median(evenSamples); // Returns 25f
        /// </code>
        /// </example>
        public static float Median(IReadOnlyList<long> values)
        {
            if (values == null || values.Count == 0)
            {
                throw new InvalidOperationException("Cannot compute median for empty list.");
            }

            int count = values.Count;
            if (count == 1)
            {
                return values[0];
            }

            var buffer = new long[count];
            for (int i = 0; i < count; i++)
            {
                buffer[i] = values[i];
            }

            Array.Sort(buffer);

            int mid = count >> 1;
            if ((count & 1) == 1)
            {
                return buffer[mid];
            }

            return (buffer[mid - 1] + buffer[mid]) / 2f;
        }
    }
}
