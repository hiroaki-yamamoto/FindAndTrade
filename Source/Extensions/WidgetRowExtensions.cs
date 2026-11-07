using System.Globalization;
using UnityEngine;
using Verse;

namespace MGAutoSell.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="WidgetRow"/> providing cached label sizing and integer text fields.
    /// </summary>
    public static class WidgetRowExtensions
    {
        /// <summary>
        /// Draws a numeric integer input box in <paramref name="row"/>, clamping parsed values to <c>[0, 1,000,000,000]</c>.
        /// </summary>
        /// <param name="row">The <see cref="WidgetRow"/> to draw into.</param>
        /// <param name="val">Reference to the integer value being edited.</param>
        /// <param name="buffer">Reference to the string edit buffer.</param>
        /// <param name="width">Width of the text field in pixels.</param>
        /// <returns>The <see cref="Rect"/> occupied by the text field.</returns>
        public static Rect TextFieldInt(this WidgetRow row, ref int val, ref string buffer, int width)
        {
            row.Gap(width + TradeRuleDrawUtility.AnnoyingUnavoidableGap);
            var rect = new Rect(row.FinalX + 2f, row.FinalY, width, 24f);

            string str = Widgets.TextField(rect, buffer);
            if (str == buffer)
            {
                return rect;
            }

            if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                val = Mathf.Clamp(parsed, 0, (int)1E+09);
                buffer = val.ToString(CultureInfo.InvariantCulture);
            }
            else if (str.Length == 0 || str == "-")
            {
                buffer = str;
            }

            return rect;
        }

        /// <summary>
        /// Draws a text label in <paramref name="row"/> using <see cref="Settings.LabelSizeCache"/> to avoid repeated
        /// <see cref="Text.CalcSize"/> calls on every frame.
        /// </summary>
        /// <param name="row">The <see cref="WidgetRow"/> to draw into.</param>
        /// <param name="text">The text string to render.</param>
        /// <param name="tooltip">Optional tooltip text.</param>
        /// <param name="height">Optional explicit row height, or <c>-1f</c> for default.</param>
        /// <returns>The <see cref="Rect"/> occupied by the label.</returns>
        public static Rect LabelFast(this WidgetRow row, string text, string tooltip = null, float height = -1f)
        {
            if (Mod.Settings.LabelSizeCache.TryGetValue(text, out var size))
            {
                return row.Label(text, size.x, tooltip, height);
            }

            size = Text.CalcSize(text);
            Mod.Settings.LabelSizeCache.Add(text, size);

            return row.Label(text, size.x, tooltip, height);
        }
    }
}
