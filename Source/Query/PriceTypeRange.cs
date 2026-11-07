using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using static RimWorld.PriceType;

namespace MGAutoSell.Query
{
    /// <summary>
    /// Represents an inclusive range of <see cref="PriceType"/> values from <see cref="min"/> to <see cref="max"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var range = PriceTypeRange.FromString("Cheap~Expensive");
    /// bool includesNormal = range.Includes(PriceType.Normal); // true
    /// bool includesVeryCheap = range.Includes(PriceType.VeryCheap); // false
    /// </code>
    /// </example>
    public struct PriceTypeRange : IEquatable<PriceTypeRange>
    {
        private enum RangeEnd : byte
        {
            None,
            Min,
            Max,
        }

        /// <summary>
        /// Total number of selectable <see cref="PriceType"/> values (excluding <see cref="PriceType.Undefined"/>).
        /// Cached as a compile-time constant to avoid per-frame <see cref="Enum.GetNames(Type)"/> allocations in <see cref="Widget"/>.
        /// </summary>
        private const int SelectableCount = 5;

        private static int draggingId = 0;
        private static readonly Color RangeControlTextColor = new(0.6f, 0.6f, 0.6f);
        private static RangeEnd curDragEnd = RangeEnd.None;

        /// <summary>
        /// The lower bound of the price type range (inclusive).
        /// </summary>
        public PriceType min;

        /// <summary>
        /// The upper bound of the price type range (inclusive).
        /// </summary>
        public PriceType max;

        private static Dictionary<PriceType, string> labelCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="PriceTypeRange"/> struct.
        /// Normalizes <see cref="PriceType.Undefined"/> bounds to <see cref="PriceType.VeryCheap"/> and <see cref="PriceType.Exorbitant"/>.
        /// </summary>
        /// <param name="min">The minimum <see cref="PriceType"/>.</param>
        /// <param name="max">The maximum <see cref="PriceType"/>.</param>
        public PriceTypeRange(PriceType min, PriceType max)
        {
            if (min == Undefined)
            {
                min = VeryCheap;
            }

            if (max == Undefined)
            {
                max = Exorbitant;
            }

            this.min = min;
            this.max = max;
        }

        /// <summary>
        /// Gets a <see cref="PriceTypeRange"/> spanning all valid price types (<see cref="PriceType.VeryCheap"/> through <see cref="PriceType.Exorbitant"/>).
        /// </summary>
        public static PriceTypeRange All => new(VeryCheap, Exorbitant);

        /// <summary>
        /// Determines whether this range equals another <see cref="PriceTypeRange"/>.
        /// </summary>
        /// <param name="other">The range to compare with.</param>
        /// <returns><c>true</c> if both <see cref="min"/> and <see cref="max"/> are equal; otherwise, <c>false</c>.</returns>
        public readonly bool Equals(PriceTypeRange other) => other.min == min && other.max == max;

        /// <summary>
        /// Formats the range as <c>"{min}~{max}"</c>.
        /// </summary>
        /// <returns>A string representation of the range.</returns>
        public readonly override string ToString() => $"{min}~{max}";

        /// <inheritdoc />
        public readonly override bool Equals(object obj) => obj is PriceTypeRange other && Equals(other);

        /// <inheritdoc />
        public readonly override int GetHashCode() => HashCode.Combine(min, max);

        /// <summary>
        /// Equality operator for two <see cref="PriceTypeRange"/> values.
        /// </summary>
        public static bool operator ==(PriceTypeRange a, PriceTypeRange b) => a.min == b.min && a.max == b.max;

        /// <summary>
        /// Inequality operator for two <see cref="PriceTypeRange"/> values.
        /// </summary>
        public static bool operator !=(PriceTypeRange a, PriceTypeRange b) => !(a == b);

        /// <summary>
        /// Determines whether the specified <paramref name="p"/> falls within [<see cref="min"/>, <see cref="max"/>] (inclusive).
        /// </summary>
        /// <param name="p">The <see cref="PriceType"/> to test.</param>
        /// <returns><c>true</c> if <paramref name="p"/> is between <see cref="min"/> and <see cref="max"/> inclusive; otherwise, <c>false</c>.</returns>
        public readonly bool Includes(PriceType p) => p != Undefined && p >= min && p <= max;

        /// <summary>
        /// Parses a <see cref="PriceTypeRange"/> from a tilde-separated string (<c>"Min~Max"</c>).
        /// </summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>The parsed <see cref="PriceTypeRange"/>.</returns>
        /// <exception cref="FormatException">Thrown when <paramref name="s"/> is null, empty, or does not contain two valid <see cref="PriceType"/> tokens.</exception>
        public static PriceTypeRange FromString(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                throw new FormatException("PriceTypeRange string cannot be null or whitespace.");
            }

            int separatorIndex = s.IndexOf('~');
            if (separatorIndex <= 0 || separatorIndex >= s.Length - 1)
            {
                throw new FormatException($"Invalid PriceTypeRange format '{s}'. Expected 'Min~Max'.");
            }

            var minSpan = s.AsSpan(0, separatorIndex).Trim();
            var maxSpan = s.AsSpan(separatorIndex + 1).Trim();

            var minVal = ParsePriceType(minSpan, s);
            var maxVal = ParsePriceType(maxSpan, s);
            return new PriceTypeRange(minVal, maxVal);
        }

        private static PriceType ParsePriceType(ReadOnlySpan<char> token, string original)
        {
            if (Enum.TryParse<PriceType>(token, ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(PriceType), parsed))
            {
                return parsed;
            }

            return ParseHelper.FromString<PriceType>(token.ToString());
        }

        /// <summary>
        /// Draws an interactive dual-thumb slider widget for editing a <see cref="PriceTypeRange"/>.
        /// </summary>
        /// <param name="rect">The bounding rectangle for the slider.</param>
        /// <param name="id">Unique control identifier for drag tracking.</param>
        /// <param name="range">Reference to the <see cref="PriceTypeRange"/> being edited.</param>
        /// <returns><c>true</c> if the range was modified during this event; otherwise, <c>false</c>.</returns>
        public static bool Widget(Rect rect, int id, ref PriceTypeRange range)
        {
            labelCache ??= new Dictionary<PriceType, string>
            {
                { VeryCheap, GenText.ToTitleCaseSmart(("PriceType" + VeryCheap).Translate()) },
                { Cheap, GenText.ToTitleCaseSmart(("PriceType" + Cheap).Translate()) },
                { Normal, GenText.ToTitleCaseSmart(("PriceType" + Normal).Translate()) },
                { Expensive, GenText.ToTitleCaseSmart(("PriceType" + Expensive).Translate()) },
                { Exorbitant, GenText.ToTitleCaseSmart(("PriceType" + Exorbitant).Translate()) },
            };
            if (range.min == Undefined)
            {
                range.min = VeryCheap;
            }

            if (range.max == Undefined)
            {
                range.max = Exorbitant;
            }

            bool changed = false;

            var sliderRect = rect;
            sliderRect.xMin += 8f;
            sliderRect.xMax -= 8f;
            GUI.color = RangeControlTextColor;
            string label = !(range == All)
                ? (range.max != range.min
                    ? labelCache[range.min] + " - " + labelCache[range.max]
                    : "OnlyQuality".Translate(labelCache[range.min]))
                : "TD.AnyValue".Translate();
            int previousFont = (int)Text.Font;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            var labelRect = sliderRect;
            labelRect.yMin -= 2f;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            var trackRect = new Rect(sliderRect.x, sliderRect.yMax - 9f, sliderRect.width, 2f);
            GUI.DrawTexture(trackRect, BaseContent.WhiteTex);
            float stepWidth = sliderRect.width / (SelectableCount - 1);
            float minHandleX = sliderRect.x + stepWidth * (range.min - VeryCheap);
            float maxHandleX = sliderRect.x + stepWidth * (range.max - VeryCheap);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(minHandleX, sliderRect.yMax - 10f, maxHandleX - minHandleX, 4f), BaseContent.WhiteTex);
            var minThumbRect = new Rect(minHandleX - 16f, trackRect.center.y - 8f, 16f, 16f);
            GUI.DrawTexture(minThumbRect, Textures.FloatRangeSliderTex);
            var maxThumbRect = new Rect(maxHandleX + 16f, trackRect.center.y - 8f, -16f, 16f);
            GUI.DrawTexture(maxThumbRect, Textures.FloatRangeSliderTex);
            if (curDragEnd != RangeEnd.None &&
                (Event.current.type == EventType.MouseUp || Event.current.type == EventType.MouseDown))
            {
                draggingId = 0;
                curDragEnd = RangeEnd.None;
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }

            bool justStartedDrag = false;
            if (Mouse.IsOver(rect) || id == draggingId)
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && id != draggingId)
                {
                    draggingId = id;
                    float mouseX = Event.current.mousePosition.x;
                    curDragEnd = mouseX >= (double)minThumbRect.xMax
                        ? (mouseX <= (double)maxThumbRect.xMin
                            ? (Mathf.Abs(mouseX - minThumbRect.xMax) < (double)Mathf.Abs(mouseX - (maxThumbRect.x - 16f))
                                ? RangeEnd.Min
                                : RangeEnd.Max)
                            : RangeEnd.Max)
                        : RangeEnd.Min;
                    justStartedDrag = true;
                    Event.current.Use();
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();
                }

                if (justStartedDrag || (curDragEnd != RangeEnd.None && UnityGUIBugsFixer.MouseDrag()))
                {
                    int snappedIndex = Mathf.Clamp(
                        Mathf.RoundToInt((Event.current.mousePosition.x - sliderRect.x) / sliderRect.width * (SelectableCount - 1)),
                        0,
                        SelectableCount - 1);
                    var snappedValue = (PriceType)(snappedIndex + (int)VeryCheap);
                    switch (curDragEnd)
                    {
                        case RangeEnd.Min:
                            if (range.min != snappedValue)
                            {
                                range.min = snappedValue;
                                if (range.max < range.min)
                                {
                                    range.max = range.min;
                                }

                                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                                changed = true;
                            }

                            break;
                        case RangeEnd.Max:
                            if (range.max != snappedValue)
                            {
                                range.max = snappedValue;
                                if (range.min > range.max)
                                {
                                    range.min = range.max;
                                }

                                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                                changed = true;
                            }

                            break;
                    }

                    if (Event.current.type == EventType.MouseDrag)
                    {
                        Event.current.Use();
                    }
                }
            }

            Text.Font = (GameFont)previousFont;
            return changed;
        }
    }
}
