using System;
using System.Collections.Generic;
using System.Linq;
using MGAutoSell.Extensions;
using MGAutoSell.Records;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;
using Waylith.Input;

namespace MGAutoSell
{
    /// <summary>
    /// Provides UI rendering and colonist negotiator discovery utilities for the Find And Trade tab and settings preview.
    /// </summary>
    internal static class TabUtility
    {
        private static readonly Color _fadedColor = new(1f, 1f, 1f, 0.4f);
        private static string _sellHeader;
        private static string SellHeader => _sellHeader ??= "MGAutoSell.SellHeader".Translate();

        /// <summary>
        /// Renders the right-hand sell preview panel displaying queued sell items, potential buy items,
        /// expected total silver, and the active negotiator.
        /// </summary>
        /// <param name="toSellRect">The bounding rectangle for the sell panel.</param>
        /// <param name="sellCache">The cached <see cref="ItemsToSell"/> data to render.</param>
        /// <param name="sellScroll">Reference to the current scroll position vector.</param>
        /// <param name="currentTab">The active <see cref="WindowTab"/> in the parent window.</param>
        /// <param name="sellerOverride">The manually selected negotiator override pawn, or <c>null</c> for auto.</param>
        /// <param name="invalidateCache">Optional callback invoked when the sell cache needs to be refreshed.</param>
        /// <param name="onSellerOverrideChanged">Optional callback invoked when the user selects a new negotiator override from the context menu.</param>
        public static void DrawSellPanel(
            Rect toSellRect,
            ItemsToSell sellCache,
            ref Vector2 sellScroll,
            WindowTab currentTab = WindowTab.Rules,
            Pawn sellerOverride = null,
            Action invalidateCache = null,
            Action<Pawn> onSellerOverrideChanged = null)
        {
            toSellRect.SplitHorizontally(Text.LineHeight, out var itemHeader, out toSellRect);

            Widgets.DrawLightHighlight(itemHeader);
            Widgets.Label(itemHeader.RightPartPixels(itemHeader.width - Text.LineHeight - 10f), SellHeader);
            GUI.DrawTexture(itemHeader.RightPartPixels(24f), ThingDefOf.Silver.uiIcon);

            toSellRect.SplitHorizontally(4f, out var gapHeader, out toSellRect);
            Widgets.DrawLineHorizontal(gapHeader.x, gapHeader.y, gapHeader.width, _fadedColor);

            int i = -1;
            var anchor = Text.Anchor;
            int totalItems = sellCache.Items.Count + (Mod.Settings.showAllMatchingItems ? sellCache.PotentialItems.Count : 0);
            var viewRect = sellCache.Trader != null ? toSellRect.TopPartPixels(toSellRect.height - Text.LineHeight) : toSellRect;
            float totalHeight = totalItems * Text.LineHeight;
            bool shouldScroll = totalHeight > viewRect.height;
            bool areWeInSettingsMenu = invalidateCache == null && onSellerOverrideChanged == null;
            var row = new Rect(0f, 0f, 0f, 0f);
            var listing = new Listing_StandardIndent();
            if (shouldScroll)
            {
                if (!areWeInSettingsMenu)
                {
                    viewRect.width += 16f;
                }

                listing.BeginScrollView(
                    viewRect,
                    ref sellScroll,
                    viewRect.LeftPartPixels(viewRect.width - 16f).TopPartPixels(totalHeight).AtZero());
            }

            double minRenderIndex = shouldScroll ? Math.Floor(sellScroll.y / Text.LineHeight) : 0;
            double maxRenderIndex = shouldScroll ? Math.Ceiling(viewRect.height / Text.LineHeight) + minRenderIndex : 0;
            foreach (var (thingDef, count, (_, totalLabel), (_, pricePerLabel)) in sellCache.Items)
            {
                i++;
                if (shouldScroll)
                {
                    if (i < minRenderIndex || i > maxRenderIndex)
                    {
                        listing.Gap(Text.LineHeight);
                        continue;
                    }

                    row = listing.GetRect(Text.LineHeight);
                }
                else
                {
                    viewRect.SplitHorizontally(Text.LineHeight, out row, out viewRect);
                }

                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(row);
                }

                var color = GUI.color;
                GUI.color = thingDef.uiIconColor;
                GUI.DrawTexture(row.LeftPartPixels(row.height), thingDef.uiIcon);
                GUI.color = color;

                row.x += row.height + 10f;
                Widgets.Label(row, thingDef.GetLabel() + $" x{count}");
                row.x -= row.height + 10f;

                if (currentTab != WindowTab.Edit)
                {
                    var middle = row.MiddlePartPixels(50f, row.height);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(middle, pricePerLabel);
                    Text.Anchor = anchor;
                }

                if (!Mod.Settings.LabelSizeCache.TryGetValue(totalLabel, out var size))
                {
                    size = Text.CalcSize(totalLabel);
                    Mod.Settings.LabelSizeCache[totalLabel] = size;
                }

                Widgets.Label(row.RightPartPixels(size.x + 4f), totalLabel);
            }

            if (Mod.Settings.showAllMatchingItems)
            {
                foreach (var potentialItem in sellCache.PotentialItems)
                {
                    i++;
                    if (shouldScroll)
                    {
                        if (i < minRenderIndex || i > maxRenderIndex)
                        {
                            listing.Gap(Text.LineHeight);
                            continue;
                        }

                        row = listing.GetRect(Text.LineHeight);
                    }
                    else
                    {
                        viewRect.SplitHorizontally(Text.LineHeight, out row, out viewRect);
                    }

                    if (i % 2 == 1)
                    {
                        Widgets.DrawLightHighlight(row);
                    }

                    var color = GUI.color;
                    GUI.color = potentialItem.Item.uiIconColor;
                    GUI.DrawTexture(row.LeftPartPixels(row.height), potentialItem.Item.uiIcon);
                    GUI.color = _fadedColor;

                    row.x += row.height + 10f;
                    Widgets.Label(row, potentialItem.Item.GetLabel());
                    row.x -= row.height + 10f;

                    if (!Mod.Settings.LabelSizeCache.TryGetValue(potentialItem.Rule, out var size))
                    {
                        size = Text.CalcSize(potentialItem.Rule);
                        Mod.Settings.LabelSizeCache[potentialItem.Rule] = size;
                    }

                    Widgets.Label(row.RightPartPixels(size.x), potentialItem.Rule);
                    GUI.color = color;
                }
            }

            if (shouldScroll)
            {
                float height = 0f;
                listing.EndScrollView(ref height);
            }

            if (sellCache.Trader == null)
            {
                return;
            }

            var footer = toSellRect.BottomPartPixels(Text.LineHeight);
            Widgets.DrawLightHighlight(footer);

            var iconRect = footer.LeftPartPixels(Text.LineHeight);
            iconRect.y -= 4f;
            GUI.DrawTexture(iconRect, sellCache.Trader.Icon.Invoke());

            string sellerLabel = sellCache.Trader.Name + $" ({sellCache.Trader.ImprovementLabel})";
            if (sellerOverride != null)
            {
                sellerLabel = $"<i>{sellerLabel}</i>";
            }

            var sellerLabelWidth = Text.CalcSize(sellerLabel);
            Widgets.Label(footer.RightPartPixels(footer.width - Text.LineHeight), sellerLabel);

            var sellerOverrideRect = footer.LeftPartPixels(iconRect.width + sellerLabelWidth.x + 8f);
            Widgets.DrawHighlightIfMouseover(sellerOverrideRect);
            if (Widgets.ButtonInvisible(sellerOverrideRect) && Event.current.button == (int)MouseButton.Right)
            {
                var pawns = GetTraders().Select(x => new FloatMenuOption(
                    x.Name + $" ({x.ImprovementLabel})",
                    () =>
                    {
                        if (onSellerOverrideChanged != null)
                        {
                            onSellerOverrideChanged(x.Pawn);
                        }
                        else
                        {
                            invalidateCache?.Invoke();
                        }
                    },
                    x.Pawn,
                    Color.white)).ToList();

                if (sellerOverride != null)
                {
                    pawns.Add(new FloatMenuOption(
                        "Auto",
                        () =>
                        {
                            if (onSellerOverrideChanged != null)
                            {
                                onSellerOverrideChanged(null);
                            }
                            else
                            {
                                invalidateCache?.Invoke();
                            }
                        }));
                }

                Find.WindowStack.Add(new FloatMenu(pawns));
            }

            var footerRow = new WidgetRow(footer.xMax - 4f, footer.y, UIDirection.LeftThenDown);
            footerRow.LabelFast(sellCache.TotalSilver.Label);
            footerRow.Icon(ThingDefOf.Silver.uiIcon);
            footerRow.LabelFast("Total:");
        }

        /// <summary>
        /// Collects all free humanlike colonists on the current map capable of social trade negotiation,
        /// sorted with the ideology leader first and descending <see cref="StatDefOf.TradePriceImprovement"/> second.
        /// </summary>
        /// <remarks>
        /// Time Complexity: O(C log C) where C is the number of free colonists on the current map.
        /// Space Complexity: O(C).
        /// </remarks>
        /// <param name="generatePictures">Whether to bind portrait texture getters for UI rendering.</param>
        /// <returns>A sorted list of <see cref="TraderRecord"/> entries.</returns>
        public static List<TraderRecord> GetTraders(bool generatePictures = true)
        {
            var stat = StatDefOf.TradePriceImprovement;
            var freeColonists = Find.CurrentMap?.mapPawns?.FreeColonists ?? [];
            bool isIdeologyActive = ModsConfig.IdeologyActive;
            var leader = isIdeologyActive ? Faction.OfPlayer.leader : null;
            var traders = new List<TraderRecord>(freeColonists.Count);

            foreach (var pawn in freeColonists)
            {
                if (!pawn.RaceProps.Humanlike || stat.Worker.IsDisabledFor(pawn))
                {
                    continue;
                }

                float improvement = pawn.GetStatValue(stat);
                bool isLeader = pawn == leader;
                Func<Texture> iconGetter = generatePictures
                    ? () => PortraitsCache.Get(
                        pawn,
                        new Vector2(24, 24),
                        Rot4.South,
                        ColonistBarColonistDrawer.PawnTextureCameraOffset,
                        1.28205f)
                    : static () => null;

                traders.Add(new TraderRecord(
                    pawn,
                    pawn.LabelShort,
                    iconGetter,
                    improvement.ToStringPercent(),
                    improvement,
                    isLeader));
            }

            traders.Sort(CompareTraders);

            return traders;
        }

        /// <summary>
        /// Compares two <see cref="TraderRecord"/> instances, ordering ideology leaders first and higher
        /// <see cref="TraderRecord.Improvement"/> values second.
        /// </summary>
        /// <param name="a">The first trader record.</param>
        /// <param name="b">The second trader record.</param>
        /// <returns>A signed integer indicating the relative sort order of <paramref name="a"/> and <paramref name="b"/>.</returns>
        internal static int CompareTraders(TraderRecord a, TraderRecord b)
        {
            int leaderCompare = b.IsLeader.CompareTo(a.IsLeader);
            return leaderCompare != 0 ? leaderCompare : b.Improvement.CompareTo(a.Improvement);
        }
    }
}
