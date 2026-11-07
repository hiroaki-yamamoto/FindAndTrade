using System;
using System.Collections.Generic;
using MGAutoSell.Extensions;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Renders individual <see cref="TradeRule"/> rows in the Find And Trade window and Mod Settings preview.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TradeRuleDrawUtility
    {
        private static readonly Color
            Green,
            Blue,
            Yellow,
            Faded,
            Disabled,
            Invalid;

        private const int ArrowSize = 8;
        private const int LabelSize = 60;
        private const int BoxSize = 40;

        /// <summary>
        /// Standard horizontal gap automatically inserted by <see cref="WidgetRow"/> between elements.
        /// </summary>
        public const int AnnoyingUnavoidableGap = 4;

        private static readonly TaggedString
            TagInvalid,
            TagRange,
            TagBuy,
            TagBasically,
            TagExport,
            TagImport,
            TagMaintain,
            TTModeExport,
            TTModeImport,
            TTModeMaintain,
            TTModeMultipleItems,
            DeleteBillTip,
            SuspendBillTip,
            EditTip;

        static TradeRuleDrawUtility()
        {
            ColorUtility.TryParseHtmlString("#60ba68", out Green);
            ColorUtility.TryParseHtmlString("#58a0d6", out Blue);
            ColorUtility.TryParseHtmlString("#d6b14a", out Yellow);
            Faded = new Color(1f, 1f, 1f, 0.4f);
            Disabled = new Color(0.3f, 0f, 0f, 0.4f);
            Invalid = ColoredText.ThreatColor;

            TagInvalid = "MGAutoSell.Invalid".Translate();
            TagRange = "MGAutoSell.InvalidRange".Translate();
            TagBuy = "MGAutoSell.InvalidBuy".Translate();
            TagBasically = "MGAutoSell.Basically".Translate();
            TagExport = "MGAutoSell.Mode.Export".Translate();
            TagImport = "MGAutoSell.Mode.Import".Translate();
            TagMaintain = "MGAutoSell.Mode.Maintain".Translate();

            TTModeExport = TagExport + "\n\n" + "MGAutoSell.Tooltips.Mode.Export".Translate();
            TTModeImport = TagImport + "\n\n" + "MGAutoSell.Tooltips.Mode.Import".Translate();
            TTModeMaintain = TagMaintain + "\n\n" + "MGAutoSell.Tooltips.Mode.Maintain".Translate();
            TTModeMultipleItems = "MGAutoSell.Tooltips.Mode.MultipleItems".Translate();

            DeleteBillTip = "DeleteBillTip".Translate();
            SuspendBillTip = "SuspendBillTip".Translate();
            EditTip = "Edit".Translate();
        }

        /// <summary>
        /// Draws a single <see cref="TradeRule"/> row and returns any user-triggered <see cref="TradeRuleAction"/>.
        /// </summary>
        /// <param name="rowRect">The bounding rectangle of the row.</param>
        /// <param name="item">The trade rule to draw.</param>
        /// <param name="i">The zero-based row index used for alternating row highlights.</param>
        /// <param name="sellCache">The current <see cref="ItemsToSell"/> cache for displaying item counts.</param>
        /// <param name="reorderId">The reorderable widget group ID, or <c>-1</c> if reordering is disabled.</param>
        /// <returns>The <see cref="TradeRuleAction"/> triggered by user interaction during this frame.</returns>
        public static TradeRuleAction DrawRow(Rect rowRect, TradeRule item, int i, ItemsToSell sellCache, int reorderId)
        {
            suspendTex ??= BakeGrayscale(TexButton.Suspend, Color.white);
            bool invalidSell = item.Invalid || !item.AllowSell;
            bool invalidBuy = item.Invalid || !item.AllowBuy;

            var response = TradeRuleAction.None;
            bool ruleDisabled = !item.Enabled;
            var OGColor = GUI.color;
            var OGAnchor = Text.Anchor;
            var OGFieldAlignment = Text.CurTextFieldStyle.alignment;
            Text.CurTextFieldStyle.alignment = TextAnchor.MiddleCenter;
            if (GUI.enabled && i % 2 == 1)
            {
                Widgets.DrawLightHighlight(rowRect);
            }

            if (ruleDisabled)
            {
                Widgets.DrawBoxSolid(rowRect, Disabled);
            }

            var left = new WidgetRow(rowRect.x, rowRect.y + 3f, UIDirection.RightThenDown);
            var right = new WidgetRow(rowRect.xMax, rowRect.y + 3f, UIDirection.LeftThenDown);

            if (GUI.enabled)
            {
                GUI.color = Faded;
                left.Icon(Textures.Drag);
                GUI.color = OGColor;
                Text.Anchor = TextAnchor.MiddleLeft;
                left.LabelFast(item.search.name);
                Text.Anchor = OGAnchor;

                if (right.ButtonIcon(FindTex.Trash, DeleteBillTip))
                {
                    response = TradeRuleAction.Delete;
                }

                if (right.ButtonIcon(FindTex.Edit, EditTip))
                {
                    response = TradeRuleAction.Edit;
                }

                if (right.ButtonIcon(suspendTex, SuspendBillTip))
                {
                    response = TradeRuleAction.Suspend;
                }
            }

            #region Export
            if (item.Mode is TradeMode.Export or TradeMode.Maintain)
            {
                if (invalidSell)
                {
                    GUI.color = Invalid;
                }
                else if (Mod.Settings.colorRuleCountsOnWork &&
                         sellCache?.Rules?.TryGetValue(item, out var count) is true &&
                         count.Range.max.Value > item.Export)
                {
                    GUI.color = ColorFromMode(item.Mode);
                }

                int before = item.Export;
                var tooltip = right.TextFieldInt(ref item.Export, ref item.ExportBuffer, BoxSize);

                if (invalidSell)
                {
                    TooltipHandler.TipRegion(
                        tooltip,
                        () => TagInvalid.Formatted(GetInvalidMessage(item.Import, item.Export, item.Mode)),
                        item.Hash);
                }

                if (string.IsNullOrWhiteSpace(item.ExportBuffer))
                {
                    item.Export = 0;
                    item.ExportBuffer = "";
                }

                int after = item.Export;
                if (before != after)
                {
                    response = TradeRuleAction.Refresh;
                }

                GUI.color = OGColor;
            }
            else
            {
                right.Gap(BoxSize + AnnoyingUnavoidableGap);
            }
            #endregion

            #region Mode Switcher
            if (GUI.enabled)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = ColorFromMode(item.Mode);
                float end = right.FinalX;

                if (item.Mode is TradeMode.Export or TradeMode.Maintain)
                {
                    right.Label("<", ArrowSize);
                }
                else
                {
                    right.Gap(ArrowSize + AnnoyingUnavoidableGap);
                }

                string label = null;
                string multipleItems = null;
                if (Mod.Settings.showQuantityInsteadOfLabel &&
                    sellCache != null &&
                    sellCache.Rules?.TryGetValue(item, out var val) is true)
                {
                    label = val.Range.max.Value > 0
                        ? GetModeString(val.Range.min.Label, val.Range.max.Label, LabelSize)
                        : null;
                    if (val.Range.min.Value != val.Range.max.Value)
                    {
                        multipleItems = val.ItemsList;
                    }
                }

                label ??= StringFromMode(item.Mode);
                right.Label(label, LabelSize);

                if (item.Mode is TradeMode.Import or TradeMode.Maintain)
                {
                    right.Label(">", ArrowSize);
                }
                else
                {
                    right.Gap(ArrowSize + AnnoyingUnavoidableGap);
                }

                float start = right.FinalX;
                var rect = new Rect(rowRect.x + start, rowRect.y, end - start, rowRect.height);
                if (Mouse.IsOver(rect))
                {
                    Widgets.DrawHighlight(rect);
                    TooltipHandler.TipRegion(
                        rect,
                        () =>
                        {
                            var tooltip = item.Mode switch
                            {
                                TradeMode.Export => TTModeExport,
                                TradeMode.Import => TTModeImport,
                                TradeMode.Maintain => TTModeMaintain,
                                _ => throw new NotImplementedException()
                            };
                            if (multipleItems != null)
                            {
                                tooltip += "\n\n" + TTModeMultipleItems.Formatted(multipleItems);
                            }

                            return tooltip;
                        },
                        item.Hash);
                }

                if (DoClickWithoutBlocking(rect, item.Hash))
                {
                    response = TradeRuleAction.Mode;
                }

                GUI.color = OGColor;
            }
            #endregion

            #region Import
            if (item.Mode is TradeMode.Import or TradeMode.Maintain)
            {
                if (invalidBuy)
                {
                    GUI.color = Invalid;
                }
                else if (Mod.Settings.colorRuleCountsOnWork &&
                         item.Import > 0 &&
                         (sellCache?.Rules?.TryGetValue(item, out var count) is not true ||
                          count.Range.min.Value < item.Import))
                {
                    GUI.color = ColorFromMode(item.Mode);
                }

                int before = item.Import;
                var tooltip = right.TextFieldInt(ref item.Import, ref item.ImportBuffer, BoxSize);

                if (invalidBuy)
                {
                    TooltipHandler.TipRegion(
                        tooltip,
                        () => TagInvalid.Formatted(GetInvalidMessage(item.Import, item.Export, item.Mode)),
                        item.Hash);
                }

                if (string.IsNullOrWhiteSpace(item.ImportBuffer))
                {
                    item.Import = 0;
                    item.ImportBuffer = "";
                }

                int after = item.Import;
                if (before != after)
                {
                    response = TradeRuleAction.Refresh;
                }

                GUI.color = OGColor;
            }
            else
            {
                right.Gap(BoxSize + AnnoyingUnavoidableGap);
            }
            #endregion

            GUI.color = OGColor;
            Text.Anchor = OGAnchor;
            Text.CurTextFieldStyle.alignment = OGFieldAlignment;

            if (reorderId != -1)
            {
                ReorderableWidget.Reorderable(reorderId, rowRect);
            }

            return response;
        }

        private static string GetModeString(string min, string max, float width)
        {
            if (!Mod.Settings.showMinMaxLabelWhereApplicable || min == max)
            {
                return max;
            }

            if (!Mod.Settings.LabelSizeCache.TryGetValue(min, out var minSize))
            {
                minSize = Text.CalcSize(min);
                Mod.Settings.LabelSizeCache[min] = minSize;
            }

            if (!Mod.Settings.LabelSizeCache.TryGetValue(max, out var maxSize))
            {
                maxSize = Text.CalcSize(max);
                Mod.Settings.LabelSizeCache[max] = maxSize;
            }

            if (!Mod.Settings.LabelSizeCache.TryGetValue("~", out var connectSize))
            {
                connectSize = Text.CalcSize("~");
                Mod.Settings.LabelSizeCache["~"] = connectSize;
            }

            return width < minSize.x + maxSize.x + connectSize.x ? max : min + "~" + max;
        }

        private static string GetInvalidMessage(int import, int export, TradeMode mode)
        {
            switch (mode)
            {
                case TradeMode.Maintain when import == 0:
                    return TagBasically.Formatted(TagExport, TagImport);
                case TradeMode.Maintain when export == 0:
                    return TagBasically.Formatted(TagImport, TagExport);
                case TradeMode.Maintain when import > export:
                    return TagRange.Formatted(import, export);
                case TradeMode.Import when import < 1:
                    return TagBuy.Formatted(import);
                default:
                    Log.WarningOnce(
                        $"Attempted to fetch a tooltip message for an invalid row, however none are defined for this case.\nImport: {import}\nExport: {export}\nMode: {mode}",
                        54987);
                    return "*shrugs*";
            }
        }

        private static Color ColorFromMode(TradeMode mode)
        {
            return mode switch
            {
                TradeMode.Export => Green,
                TradeMode.Import => Blue,
                TradeMode.Maintain => Yellow,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private static string StringFromMode(TradeMode mode)
        {
            return mode switch
            {
                TradeMode.Export => TagExport,
                TradeMode.Import => TagImport,
                TradeMode.Maintain => TagMaintain,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
            };
        }

        private static readonly HashSet<int> mouseEvents = [];

        private static bool DoClickWithoutBlocking(Rect rect, int id)
        {
            var current = Event.current;
            switch (current.type)
            {
                case EventType.MouseDown:
                    if (Mouse.IsOver(rect))
                    {
                        mouseEvents.Add(id);
                    }

                    break;
                case EventType.MouseUp:
                    if (!mouseEvents.Contains(id))
                    {
                        break;
                    }

                    return Mouse.IsOver(rect) && mouseEvents.Remove(id);

                case EventType.MouseMove:
                case EventType.MouseDrag:
                    if (!mouseEvents.Contains(id))
                    {
                        break;
                    }

                    if (!Mouse.IsOver(rect))
                    {
                        mouseEvents.Remove(id);
                    }

                    break;
            }

            return false;
        }

        private static Texture2D suspendTex;

        private static Texture2D BakeGrayscale(Texture2D source, Color tint)
        {
            var mat = MaterialPool.MatFrom(source, ShaderDatabase.GrayscaleGUI, tint);
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt, mat);
            RenderTexture.active = rt;
            var baked = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            baked.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            baked.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return baked;
        }
    }

    /// <summary>
    /// Represents a user interaction event triggered on a <see cref="TradeRule"/> row.
    /// </summary>
    public enum TradeRuleAction
    {
        /// <summary>No action occurred.</summary>
        None,

        /// <summary>The delete button was clicked.</summary>
        Delete,

        /// <summary>The edit button was clicked.</summary>
        Edit,

        /// <summary>The suspend/enable toggle button was clicked.</summary>
        Suspend,

        /// <summary>The trade mode selector was clicked.</summary>
        Mode,

        /// <summary>An import or export threshold value was modified.</summary>
        Refresh
    }
}
