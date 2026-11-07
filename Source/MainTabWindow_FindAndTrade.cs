using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using MGAutoSell.Extensions;
using MGAutoSell.Filter;
using MGAutoSell.Presets;
using MGAutoSell.Records;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using UnityEngine.UIElements;
using Verse;
using Waylith.Input;
using static MGAutoSell.TabUtility;

namespace MGAutoSell
{


    public class MainTabWindow_FindAndTrade : MainTabWindow
    {
        private ItemsToSell sellCache;
        private List<TraderRecord> tradersCache;

        private TradeRulesGameComp comp;
        private TradeRuleEditor editor;

        private Pawn SellerOverride;

        private long nextCache = 0;
        private long nextQuickCache = 0;

        Vector2 listerScroll = Vector2.zero;
        Vector2 settingScroll = Vector2.zero;
        Vector2 sellScroll = Vector2.zero;
        private string previousRenderTime;

        private string title = $"<i>{"MGAutoSell.Title".Translate()}</i>";
        private string tradeAutomaticallyLabel = "MGAutoSell.AutoSellToggle".Translate();
        private string sellHeader = "MGAutoSell.SellHeader".Translate();

#if DEBUG
        List<long> ticks = new List<long>();
        private long nextPerformance = 0;
#endif

        public override Vector2 RequestedTabSize => new(1010f, 300f);
        protected override float Margin => 8f;
        public bool SellListDirty => editor != null;
        private int reorderID;

        private WindowTab currentTab = WindowTab.Rules;

        private Color _fadedColor = new(1, 1, 1, 0.4f);

        public MainTabWindow_FindAndTrade()
        {
            preventCameraMotion = false;
            doCloseX = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            comp = Current.Game.GetComponent<TradeRulesGameComp>();
            comp.Fetch(Find.CurrentMap, true);
        }

        public override void PostClose()
        {
            editor?.PostClose();
            editor = null;
            SelectedTradeRule = null;
            currentTab = WindowTab.Rules;
            tradersCache = null;
            nextCache = 0;
            base.PostClose();
        }


        public override void DoWindowContents(Rect inRect)
        {
            var map = Find.CurrentMap;
            sellCache = comp.Fetch(map);
#if DEBUG
            if (nextPerformance == 0)
                nextPerformance = DateTimeOffset.Now.AddSeconds(1).ToUnixTimeSeconds();
            if (DateTimeOffset.Now.ToUnixTimeSeconds() > nextPerformance && ticks.Any())
            {
                previousRenderTime = $"{ticks.Min()}~{ticks.Max()} | {Median(ticks)}";
                ticks.Clear();
                nextPerformance = DateTimeOffset.Now.AddSeconds(1).ToUnixTimeSeconds();
            }

            var timestamp = Stopwatch.GetTimestamp();
#endif
            var color = GUI.color;
            var font = Text.Font;
            Text.Font = GameFont.Small;

            var width = currentTab == WindowTab.Edit ? 600f : 450f;
            var leftPanel = inRect.LeftPartPixels(width);

            switch (currentTab)
            {
                case WindowTab.Edit:
                    DrawEditTab(leftPanel);
                    break;
                case WindowTab.Settings:
                    {
                        leftPanel.SplitHorizontally(30, out var header, out var body);
                        DrawTitle(header);
                        DrawSettingsTab(body);
                    }
                    break;
                case WindowTab.Rules:
                default:
                    {
                        leftPanel.SplitHorizontally(30, out var header, out var body);
                        DrawTitle(header, true);
                        DrawRulesTab(body);
                    }
                    break;
            }

            TryCacheItemsToSell();

            var rightPanel = inRect.RightPartPixels(inRect.width - leftPanel.width - 12 - 16);
            rightPanel.x -= 16;

            GUI.color = _fadedColor;
            Widgets.DrawLineVertical(leftPanel.width + 6, 0, 300f);
            GUI.color = color;

            TabUtility.DrawSellPanel(rightPanel, sellCache, ref sellScroll, currentTab, SellerOverride,
                () => nextCache = 0);

            Text.Font = font;
#if DEBUG
            ticks.Add(Stopwatch.GetTimestamp() - timestamp);
#endif
        }

        private void DrawTitle(Rect headerRect, bool showSettingsIcon = false)
        {
            var color = GUI.color;
            headerRect.y -= 4;
            Text.Font = GameFont.Medium;
            GUI.color = _fadedColor;
            Widgets.Label(headerRect, title);
            Text.Font = GameFont.Small;
            GUI.color = color;
            headerRect.y += 4;

            GUI.color = _fadedColor;
            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax - 2, headerRect.width);
            GUI.color = color;

            if (showSettingsIcon)
            {
                var iconSize = Text.LineHeight;
                var settingsRect = headerRect.TopPartPixels(iconSize).RightPartPixels(iconSize);
                var presetsRect = headerRect.TopPartPixels(iconSize).RightPartPixels(iconSize * 2 + 4f).LeftPartPixels(iconSize);

                if (Widgets.ButtonImage(presetsRect, FindTex.Book, _fadedColor))
                {
                    Find.WindowStack.Add(new Dialog_ManagePresets(comp, () =>
                    {
                        sellCache = comp.Fetch(Find.CurrentMap, true);
                    }));
                }
                TooltipHandler.TipRegion(presetsRect, "MGAutoSell.PresetsTooltip".Translate());

                if (Widgets.ButtonImage(settingsRect, Textures.OptionsGeneral, _fadedColor))
                {
                    tradersCache = GetTraders();
                    currentTab = WindowTab.Settings;
                }
            }
        }

        private void DrawEditTab(Rect panel)
        {
            panel.SplitHorizontally(panel.height - 30f, out var top, out var bottom);
            var buttonRect = bottom.LeftPartPixels(30f);
            editor!.DoWindowContents(top);
            if (!Widgets.ButtonImage(buttonRect, TexButton.Banish))
                return;

            currentTab = WindowTab.Rules;
            editor?.PostClose();
            editor = null;
            SelectedTradeRule = null;
        }

        private void DrawSettingsTab(Rect panel)
        {
            panel.SplitHorizontally(panel.height - 30, out var body, out var footer);
            var buttonRect = footer.LeftPartPixels(30f);
            if (Widgets.ButtonImage(buttonRect, TexButton.Banish))
            {
                tradersCache = null;
                currentTab = WindowTab.Rules;
                return;
            }

            body.SplitHorizontally(Text.LineHeight, out var autoSellRect, out body);
            body.SplitHorizontally(4f, out var gap, out var drawer);

            Widgets.CheckboxLabeled(autoSellRect, tradeAutomaticallyLabel, ref comp.autoTrade);

            var color = GUI.color;
            GUI.color = _fadedColor;
            Widgets.DrawLineHorizontal(gap.x + 5, gap.y + 2, gap.width - 10);
            GUI.color = color;

            var spacePerRow = 24;
            var totalHeight = tradersCache.Count * spacePerRow;
            var shouldScroll = totalHeight > drawer.height;
            var listing = new Listing_StandardIndent();
            if (shouldScroll)
                listing.BeginScrollView(drawer, ref settingScroll, drawer.LeftPartPixels(drawer.width - 16).TopPartPixels(totalHeight).AtZero());
            else
                listing.Begin(drawer);

            for (var i = 0; i < tradersCache.Count; i++)
            {
                var trader = tradersCache[i];
                var enabled = comp.autoTraderIDs.Contains(trader.Pawn.thingIDNumber);
                var prev = enabled;
                var rect = listing.GetRect(spacePerRow);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(rect);

                rect.SplitVertically(spacePerRow, out var iconRect, out rect);
                rect.SplitVertically(rect.width / 2, out var labelRect, out rect);

                iconRect.y -= 4;
                GUI.DrawTexture(iconRect, trader.Icon.Invoke());

                Widgets.Label(labelRect, $"{trader.Name} ({trader.ImprovementLabel})");

                rect.SplitVertically(rect.width - spacePerRow, out rect, out var checkboxRect);
                Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref enabled, spacePerRow, paintable: true);
                if (enabled != prev)
                {
                    if (enabled)
                        comp.autoTraderIDs.Add(trader.Pawn.thingIDNumber);
                    else
                        comp.autoTraderIDs.Remove(trader.Pawn.thingIDNumber);
                }

                if (trader.IsLeader)
                {
                    var role = trader.Pawn.Ideo.GetRole(trader.Pawn);
                    if (role != null)
                    {
                        GUI.color = trader.Pawn.ideo.Ideo.Color;
                        rect.SplitVertically(rect.width - spacePerRow, out rect, out var leaderIconRect);
                        GUI.DrawTexture(leaderIconRect, role.Icon);
                        TooltipHandler.TipRegion(leaderIconRect, role.TipLabel);
                        GUI.color = color;
                    }
                }


            }

            var height = 0f;
            if (shouldScroll)
                listing.EndScrollView(ref height);
            else
                listing.End();
        }

        private void DrawRulesTab(Rect panel)
        {
            var height = 300f;
            panel.SplitHorizontally(panel.height - 30f, out var body, out var bottom);
            var buttonRect = bottom.LeftPartPixels(30f);

            var drawerListing = new Listing_StandardIndent();
            drawerListing.BeginScrollView(body, ref listerScroll,
                body.LeftPartPixels(body.width - 16).TopPartPixels(comp.tradeRules.Count * 30).AtZero());

            if (Event.current.type == EventType.Repaint)
                reorderID = ReorderableWidget.NewGroup(DoReorderSearch, ReorderableDirection.Vertical,
                    new Rect(0.0f, -30, drawerListing.ColumnWidth, height + 30), -1f,
                    (index, _) =>
                        DrawMouseAttachedQuerySearch(comp.tradeRules[index].Search, drawerListing.ColumnWidth));
            var minRenderIndex = Math.Floor(listerScroll.y / 30);
            var maxRenderIndex = Math.Ceiling(panel.height / 30) + minRenderIndex;
            for (var index = 0; index < comp.tradeRules.Count; index++)
            {
                var tradeRule = comp.tradeRules[index];
                if (index < minRenderIndex || index > maxRenderIndex)
                    GUI.enabled = false;
                var action = TradeRuleDrawUtility.DrawRow(drawerListing.GetRect(30), tradeRule, index, sellCache,
                    reorderID);
                GUI.enabled = true;
                switch (action)
                {
                    case TradeRuleAction.None:
                        break;
                    case TradeRuleAction.Delete:
                        comp.tradeRules.RemoveAt(index);
                        index--;
                        sellCache = comp.Fetch(Find.CurrentMap, true);
                        break;
                    case TradeRuleAction.Edit:
                        DoEdit(tradeRule);
                        break;
                    case TradeRuleAction.Suspend:
                        tradeRule.Enabled = !tradeRule.Enabled;
                        sellCache = comp.Fetch(Find.CurrentMap, true);
                        break;
                    case TradeRuleAction.Mode:
                        tradeRule.Mode = tradeRule.Mode.Next();
                        sellCache = comp.Fetch(Find.CurrentMap, true);
                        break;
                    case TradeRuleAction.Refresh:
                        sellCache = comp.Fetch(Find.CurrentMap, true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            }

            drawerListing.EndScrollView(ref height);

            if (Widgets.ButtonImage(buttonRect, FindTex.GreyPlus))
                CreateRule();
            TooltipHandler.TipRegion(buttonRect, "MGAutoSell.NewTradeRule".Translate());
#if DEBUG
            var color = GUI.color;
            GUI.color = _fadedColor;
            var controls = new WidgetRow(bottom.xMax, bottom.yMax - Text.LineHeight, UIDirection.LeftThenDown);
            controls.Label($"<i> Render: {previousRenderTime}</i>");
            GUI.color = color;
#endif
        }

        private void DrawSellPanel(Rect toSellRect)
        {
            toSellRect.SplitHorizontally(Text.LineHeight, out var itemHeader, out toSellRect);

            Widgets.DrawLightHighlight(itemHeader);
            Widgets.Label(itemHeader.RightPartPixels(itemHeader.width - Text.LineHeight - 10), sellHeader);
            GUI.DrawTexture(itemHeader.RightPartPixels(24f), ThingDefOf.Silver.uiIcon);

            toSellRect.SplitHorizontally(4, out var gapHeader, out toSellRect);
            Widgets.DrawLineHorizontal(gapHeader.x, gapHeader.y, gapHeader.width, _fadedColor);

            int i = -1;
            var anchor = Text.Anchor;
            var totalItems = sellCache.Items.Count + (Mod.Settings.showAllMatchingItems ? sellCache.PotentialItems.Count : 0);
            var viewRect = toSellRect.TopPartPixels(toSellRect.height - Text.LineHeight);
            var totalHeight = totalItems * Text.LineHeight;
            var shouldScroll = totalHeight > viewRect.height;
            var row = new Rect(0, 0, 0, 0);
            var listing = new Listing_StandardIndent();
            if (shouldScroll)
            {
                viewRect.width += 16;
                listing.BeginScrollView(viewRect, ref sellScroll, viewRect.LeftPartPixels(viewRect.width - 16).TopPartPixels(totalHeight).AtZero());
            }

            var minRenderIndex = shouldScroll ? Math.Floor(sellScroll.y / Text.LineHeight) : 0;
            var maxRenderIndex = shouldScroll ? Math.Ceiling(viewRect.height / Text.LineHeight) + minRenderIndex : 0;
            foreach (var (thingDef, count, (total, totalLabel), (pricePer, pricePerLabel)) in sellCache.Items)
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
                    viewRect.SplitHorizontally(Text.LineHeight, out row, out viewRect);

                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                var color = GUI.color;
                GUI.color = thingDef.uiIconColor;
                GUI.DrawTexture(row.LeftPartPixels(row.height), thingDef.uiIcon);
                GUI.color = color;

                row.x += row.height + 10;
                Widgets.Label(row, thingDef.GetLabel() + $" x{count}");
                row.x -= row.height + 10;

                if (currentTab != WindowTab.Edit)
                {
                    var middle = row.MiddlePartPixels(50, row.height);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(middle, pricePerLabel);
                    Text.Anchor = anchor;
                }

                if (!Mod.Settings.LabelSizeCache.TryGetValue(totalLabel, out var size))
                {
                    size = Text.CalcSize(totalLabel);
                    Mod.Settings.LabelSizeCache[totalLabel] = size;
                }
                Widgets.Label(row.RightPartPixels(size.x + 4), totalLabel);
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
                        viewRect.SplitHorizontally(Text.LineHeight, out row, out viewRect);

                    if (i % 2 == 1)
                        Widgets.DrawLightHighlight(row);

                    var color = GUI.color;
                    GUI.color = potentialItem.Item.uiIconColor;
                    GUI.DrawTexture(row.LeftPartPixels(row.height), potentialItem.Item.uiIcon);
                    GUI.color = _fadedColor;

                    row.x += row.height + 10;
                    Widgets.Label(row, potentialItem.Item.GetLabel());
                    row.x -= row.height + 10;

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
                var height = 0f;
                listing.EndScrollView(ref height);
            }

            var footer = toSellRect.BottomPartPixels(Text.LineHeight);
            Widgets.DrawLightHighlight(footer);

            var iconRect = footer.LeftPartPixels(Text.LineHeight);
            iconRect.y -= 4;
            GUI.DrawTexture(iconRect, sellCache.Trader.Icon.Invoke());

            var sellerLabel = sellCache.Trader.Name + $" ({sellCache.Trader.ImprovementLabel})";
            if (SellerOverride != null)
                sellerLabel = $"<i>{sellerLabel}</i>";

            var sellerLabelWidth = Text.CalcSize(sellerLabel);
            Widgets.Label(footer.RightPartPixels(footer.width - Text.LineHeight), sellerLabel);

            var sellerOverrideRect = footer.LeftPartPixels(iconRect.width + sellerLabelWidth.x + 8);
            Widgets.DrawHighlightIfMouseover(sellerOverrideRect);
            if (Widgets.ButtonInvisible(sellerOverrideRect) && Event.current.button == (int)MouseButton.Right)
            {
                var pawns = GetTraders().Select(x => new FloatMenuOption(
                    x.Name + $" ({x.ImprovementLabel})", () =>
                    {
                        SellerOverride = x.Pawn;
                        nextCache = 0;
                    }, x.Pawn, Color.white)).ToList();

                if (SellerOverride != null)
                    pawns.Add(new FloatMenuOption("Auto", () =>
                    {
                        SellerOverride = null;
                        nextCache = 0;
                    }));

                Find.WindowStack.Add(new FloatMenu(pawns));
            }

            var footerRow = new WidgetRow(footer.xMax - 4, footer.y, UIDirection.LeftThenDown);
            footerRow.LabelFast(sellCache.TotalSilver.Label);
            footerRow.Icon(ThingDefOf.Silver.uiIcon);
            footerRow.LabelFast("Total:");
        }

        public void TryCacheItemsToSell(bool force = false)
        {
            var shouldUpdate = force || (SellListDirty && nextQuickCache < DateTime.UtcNow.Ticks);

            if (!shouldUpdate)
                return;

            comp.SellCache[Find.CurrentMap] = CacheUtility.Cache(comp, Find.CurrentMap, out _, SellerOverride);
            nextQuickCache = DateTime.UtcNow.AddSeconds(1).Ticks;
        }

        public TradeRule SelectedTradeRule;

        public void DoEdit(TradeRule tradeRule)
        {
            editor?.PostClose();

            if (SelectedTradeRule == tradeRule)
            {
                editor = null;
                currentTab = WindowTab.Rules;
                SelectedTradeRule = null;
            }
            else
            {
                editor = new TradeRuleEditor(tradeRule);
                currentTab = WindowTab.Edit;
                SelectedTradeRule = tradeRule;
            }
        }

        public void CreateRule()
        {
            Find.WindowStack.Add(new Dialog_Name("MGAutoSell.NameForNew".Translate(), n =>
                {
                    TradeRule tradeRule = new(n);
                    comp.tradeRules.Add(tradeRule);

                    editor = new TradeRuleEditor(tradeRule);
                    currentTab = WindowTab.Edit;
                },
                "MGAutoSell.NewTradeRule".Translate(),
                name => comp.tradeRules.Any(x => name == x.Search.name)));
        }

        private protected virtual void DoReorderSearch(int from, int to)
        {
            var obj = comp.tradeRules[from];
            comp.tradeRules.RemoveAt(from);
            comp.tradeRules.Insert(from < to ? to - 1 : to, obj);
        }

        public static void DrawMouseAttachedQuerySearch(QuerySearch search, float width)
        {
            Find.WindowStack.ImmediateWindow(34003428,
                new Rect(Event.current.mousePosition + Vector2.one * 12f, new Vector2(width, Text.LineHeight)),
                WindowLayer.Super,
                (Action)(() => Widgets.Label(new Rect(0.0f, 0.0f, width, Text.LineHeight), search.name)), false,
                shadowAlpha: 0.0f);
        }

#if DEBUG
        public static float Median(List<long> values)
        {
            if (values == null || values.Count == 0)
                throw new System.InvalidOperationException("Cannot compute median for empty list.");

            var sorted = values.OrderBy(x => x).ToList();
            int count = sorted.Count;

            if (count % 2 == 1)
            {
                // Odd
                return sorted[count / 2];
            }
            else
            {
                // Even
                return (sorted[(count / 2) - 1] + sorted[count / 2]) / 2f;
            }
        }
#endif

    }

#if DEBUG
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class Patch_OpenTabAfterLoadGame
    {
        public static void Postfix()
        {
            if (!Prefs.DevMode) return; // only in dev
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (Mod.Settings.MenuToOpen)
                {
                    case OpenSetting.Settings:
                        Find.WindowStack.Add(new Dialog_ModSettings(Mod.Instance));
                        break;
                    case OpenSetting.MainMenuTab:
                        if (Current.ProgramState != ProgramState.Playing) return;
                        if (Find.CurrentMap == null) return;

                        // Pick the tab you want.
                        var def = MainTabDefOf.FindAndTrade; // e.g. MainButtonDefOf.Assign, Architect, Research, etc.
                        Find.MainTabsRoot.SetCurrentTab(def, playSound: false);
                        break;
                }
                // Make sure we actually have a map and the UI is initialized.


                //CloseDevConsole();

                // Optional: if you want the actual window instance:
                // var window = Find.MainTabsRoot.OpenTab?.TabWindow;
            });
        }

        private static void CloseDevConsole()
        {
            if (Find.WindowStack == null) return;

            var logWindow = Find.WindowStack.Windows
                .FirstOrDefault(w => w is EditWindow_Log);

            if (logWindow != null)
            {
                Find.WindowStack.TryRemove(logWindow);
            }
        }
    }

    public enum OpenSetting
    {
        None,
        Settings,
        MainMenuTab
    }
#endif

    [DefOf]
    public static class MainTabDefOf
    {
        public static MainButtonDef FindAndTrade;
    }

    public enum WindowTab
    {
        Rules = 0,
        Edit = 1,
        Settings = 2
    }


}
