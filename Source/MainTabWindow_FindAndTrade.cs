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
using Verse;
using static MGAutoSell.TabUtility;

namespace MGAutoSell
{
    /// <summary>
    /// Primary main-tab window for configuring trade rules, previewing sellable items, and assigning automated negotiators.
    /// </summary>
    public class MainTabWindow_FindAndTrade : MainTabWindow
    {
        private ItemsToSell sellCache;
        private List<TraderRecord> tradersCache;

        private TradeRulesGameComp comp;
        private TradeRuleEditor editor;

        private Pawn SellerOverride;

        private long nextQuickCache = 0;

        private Vector2 listerScroll = Vector2.zero;
        private Vector2 settingScroll = Vector2.zero;
        private Vector2 sellScroll = Vector2.zero;
        private string previousRenderTime;

        private readonly string title = $"<i>{"MGAutoSell.Title".Translate()}</i>";
        private readonly string tradeAutomaticallyLabel = "MGAutoSell.AutoSellToggle".Translate();

#if DEBUG
        private readonly List<long> ticks = [];
        private long nextPerformance = 0;
#endif

        /// <inheritdoc />
        public override Vector2 RequestedTabSize => new(1010f, 300f);

        /// <inheritdoc />
        protected override float Margin => 8f;

        /// <summary>
        /// Gets a value indicating whether the rule editor is currently open and may have modified the active rule.
        /// </summary>
        public bool SellListDirty => editor != null;

        private int reorderID;

        private WindowTab currentTab = WindowTab.Rules;

        private readonly Color _fadedColor = new(1f, 1f, 1f, 0.4f);

        /// <summary>
        /// Currently selected <see cref="TradeRule"/> in the rule editor, or <c>null</c> if none is selected.
        /// </summary>
        public TradeRule SelectedTradeRule;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainTabWindow_FindAndTrade"/> class.
        /// </summary>
        public MainTabWindow_FindAndTrade()
        {
            preventCameraMotion = false;
            doCloseX = true;
        }

        /// <inheritdoc />
        public override void PreOpen()
        {
            base.PreOpen();
            comp = Current.Game.GetComponent<TradeRulesGameComp>();
            var map = Find.CurrentMap;
            if (map != null && comp.SellerOverride.TryGetValue(map, out var overridePawn))
            {
                SellerOverride = overridePawn;
            }

            comp.Fetch(map, true);
        }

        /// <inheritdoc />
        public override void PostClose()
        {
            editor?.PostClose();
            editor = null;
            SelectedTradeRule = null;
            currentTab = WindowTab.Rules;
            tradersCache = null;
            base.PostClose();
        }

        /// <inheritdoc />
        public override void DoWindowContents(Rect inRect)
        {
            var map = Find.CurrentMap;
            sellCache = comp.Fetch(map);
#if DEBUG
            if (nextPerformance == 0)
            {
                nextPerformance = DateTimeOffset.Now.AddSeconds(1).ToUnixTimeSeconds();
            }

            if (DateTimeOffset.Now.ToUnixTimeSeconds() > nextPerformance && ticks.Count > 0)
            {
                previousRenderTime = $"{ticks.Min()}~{ticks.Max()} | {StatisticsUtility.Median(ticks)}";
                ticks.Clear();
                nextPerformance = DateTimeOffset.Now.AddSeconds(1).ToUnixTimeSeconds();
            }

            long timestamp = Stopwatch.GetTimestamp();
#endif
            var color = GUI.color;
            var font = Text.Font;
            Text.Font = GameFont.Small;

            float width = currentTab == WindowTab.Edit ? 600f : 450f;
            var leftPanel = inRect.LeftPartPixels(width);

            switch (currentTab)
            {
                case WindowTab.Edit:
                    DrawEditTab(leftPanel);
                    break;
                case WindowTab.Settings:
                {
                    leftPanel.SplitHorizontally(30f, out var header, out var body);
                    DrawTitle(header);
                    DrawSettingsTab(body);
                    break;
                }
                case WindowTab.Rules:
                default:
                {
                    leftPanel.SplitHorizontally(30f, out var header, out var body);
                    DrawTitle(header, true);
                    DrawRulesTab(body);
                    break;
                }
            }

            TryCacheItemsToSell();

            var rightPanel = inRect.RightPartPixels(inRect.width - leftPanel.width - 12f - 16f);
            rightPanel.x -= 16f;

            GUI.color = _fadedColor;
            Widgets.DrawLineVertical(leftPanel.width + 6f, 0f, 300f);
            GUI.color = color;

            DrawSellPanel(
                rightPanel,
                sellCache,
                ref sellScroll,
                currentTab,
                SellerOverride,
                () => TryCacheItemsToSell(true),
                newSeller =>
                {
                    SellerOverride = newSeller;
                    if (map != null)
                    {
                        if (newSeller != null)
                        {
                            comp.SellerOverride[map] = newSeller;
                        }
                        else
                        {
                            comp.SellerOverride.Remove(map);
                        }
                    }

                    TryCacheItemsToSell(true);
                });

            Text.Font = font;
#if DEBUG
            ticks.Add(Stopwatch.GetTimestamp() - timestamp);
#endif
        }

        private void DrawTitle(Rect headerRect, bool showSettingsIcon = false)
        {
            var color = GUI.color;
            headerRect.y -= 4f;
            Text.Font = GameFont.Medium;
            GUI.color = _fadedColor;
            Widgets.Label(headerRect, title);
            Text.Font = GameFont.Small;
            GUI.color = color;
            headerRect.y += 4f;

            GUI.color = _fadedColor;
            Widgets.DrawLineHorizontal(headerRect.x, headerRect.yMax - 2f, headerRect.width);
            GUI.color = color;

            if (showSettingsIcon)
            {
                float iconSize = Text.LineHeight;
                var settingsRect = headerRect.TopPartPixels(iconSize).RightPartPixels(iconSize);
                var presetsRect = headerRect.TopPartPixels(iconSize).RightPartPixels(iconSize * 2f + 4f).LeftPartPixels(iconSize);

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
            {
                return;
            }

            currentTab = WindowTab.Rules;
            editor?.PostClose();
            editor = null;
            SelectedTradeRule = null;
        }

        private void DrawSettingsTab(Rect panel)
        {
            panel.SplitHorizontally(panel.height - 30f, out var body, out var footer);
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
            Widgets.DrawLineHorizontal(gap.x + 5f, gap.y + 2f, gap.width - 10f);
            GUI.color = color;

            const int spacePerRow = 24;
            float totalHeight = tradersCache.Count * spacePerRow;
            bool shouldScroll = totalHeight > drawer.height;
            var listing = new Listing_StandardIndent();
            if (shouldScroll)
            {
                listing.BeginScrollView(
                    drawer,
                    ref settingScroll,
                    drawer.LeftPartPixels(drawer.width - 16f).TopPartPixels(totalHeight).AtZero());
            }
            else
            {
                listing.Begin(drawer);
            }

            for (int i = 0; i < tradersCache.Count; i++)
            {
                var trader = tradersCache[i];
                bool enabled = comp.autoTraderIDs.Contains(trader.Pawn.thingIDNumber);
                bool prev = enabled;
                var rect = listing.GetRect(spacePerRow);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(rect);
                }

                rect.SplitVertically(spacePerRow, out var iconRect, out rect);
                rect.SplitVertically(rect.width / 2f, out var labelRect, out rect);

                iconRect.y -= 4f;
                GUI.DrawTexture(iconRect, trader.Icon.Invoke());

                Widgets.Label(labelRect, $"{trader.Name} ({trader.ImprovementLabel})");

                rect.SplitVertically(rect.width - spacePerRow, out rect, out var checkboxRect);
                Widgets.Checkbox(checkboxRect.x, checkboxRect.y, ref enabled, spacePerRow, paintable: true);
                if (enabled != prev)
                {
                    if (enabled)
                    {
                        comp.autoTraderIDs.Add(trader.Pawn.thingIDNumber);
                    }
                    else
                    {
                        comp.autoTraderIDs.Remove(trader.Pawn.thingIDNumber);
                    }
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

            float height = 0f;
            if (shouldScroll)
            {
                listing.EndScrollView(ref height);
            }
            else
            {
                listing.End();
            }
        }

        private void DrawRulesTab(Rect panel)
        {
            float height = 300f;
            panel.SplitHorizontally(panel.height - 30f, out var body, out var bottom);
            var buttonRect = bottom.LeftPartPixels(30f);

            var drawerListing = new Listing_StandardIndent();
            drawerListing.BeginScrollView(
                body,
                ref listerScroll,
                body.LeftPartPixels(body.width - 16f).TopPartPixels(comp.tradeRules.Count * 30f).AtZero());

            if (Event.current.type == EventType.Repaint)
            {
                reorderID = ReorderableWidget.NewGroup(
                    DoReorderSearch,
                    ReorderableDirection.Vertical,
                    new Rect(0.0f, -30f, drawerListing.ColumnWidth, height + 30f),
                    -1f,
                    (index, _) => DrawMouseAttachedQuerySearch(comp.tradeRules[index].Search, drawerListing.ColumnWidth));
            }

            double minRenderIndex = Math.Floor(listerScroll.y / 30f);
            double maxRenderIndex = Math.Ceiling(panel.height / 30f) + minRenderIndex;
            for (int index = 0; index < comp.tradeRules.Count; index++)
            {
                var tradeRule = comp.tradeRules[index];
                if (index < minRenderIndex || index > maxRenderIndex)
                {
                    GUI.enabled = false;
                }

                var action = TradeRuleDrawUtility.DrawRow(
                    drawerListing.GetRect(30f),
                    tradeRule,
                    index,
                    sellCache,
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
            {
                CreateRule();
            }

            TooltipHandler.TipRegion(buttonRect, "MGAutoSell.NewTradeRule".Translate());
#if DEBUG
            var color = GUI.color;
            GUI.color = _fadedColor;
            var controls = new WidgetRow(bottom.xMax, bottom.yMax - Text.LineHeight, UIDirection.LeftThenDown);
            controls.Label($"<i> Render: {previousRenderTime}</i>");
            GUI.color = color;
#endif
        }

        /// <summary>
        /// Rebuilds the map's <see cref="ItemsToSell"/> cache if <paramref name="force"/> is <c>true</c>
        /// or if the rule editor is open and the throttle interval has elapsed.
        /// </summary>
        /// <param name="force">Whether to bypass the throttle interval and force an immediate cache update.</param>
        public void TryCacheItemsToSell(bool force = false)
        {
            bool shouldUpdate = force || (SellListDirty && nextQuickCache < DateTime.UtcNow.Ticks);
            if (!shouldUpdate)
            {
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            sellCache = CacheUtility.Cache(comp, map, out _, SellerOverride);
            comp.SellCache[map] = sellCache;
            nextQuickCache = DateTime.UtcNow.AddSeconds(1).Ticks;
        }

        /// <summary>
        /// Opens or toggles the <see cref="TradeRuleEditor"/> for the specified <paramref name="tradeRule"/>.
        /// </summary>
        /// <param name="tradeRule">The trade rule to edit.</param>
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

        /// <summary>
        /// Opens a naming dialog to create a new <see cref="TradeRule"/> and immediately switches to the rule editor.
        /// </summary>
        public void CreateRule()
        {
            Find.WindowStack.Add(new Dialog_Name(
                "MGAutoSell.NameForNew".Translate(),
                n =>
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

        /// <summary>
        /// Renders a floating label attached to the mouse cursor while dragging a rule to reorder it.
        /// </summary>
        /// <param name="search">The query search being dragged.</param>
        /// <param name="width">Width of the floating label window.</param>
        public static void DrawMouseAttachedQuerySearch(QuerySearch search, float width)
        {
            Find.WindowStack.ImmediateWindow(
                34003428,
                new Rect(Event.current.mousePosition + Vector2.one * 12f, new Vector2(width, Text.LineHeight)),
                WindowLayer.Super,
                () => Widgets.Label(new Rect(0.0f, 0.0f, width, Text.LineHeight), search.name),
                false,
                shadowAlpha: 0.0f);
        }

        /// <summary>
        /// Computes the median of a list of 64-bit integer tick measurements.
        /// </summary>
        /// <param name="values">The non-empty list of measurements.</param>
        /// <returns>The median value.</returns>
        public static float Median(List<long> values) => StatisticsUtility.Median(values);
    }

#if DEBUG
    /// <summary>
    /// Development-only Harmony patch that automatically opens the configured window after loading a save game.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.LoadGame))]
    public static class Patch_OpenTabAfterLoadGame
    {
        /// <summary>
        /// Postfix callback executed after <see cref="Game.LoadGame"/>.
        /// </summary>
        public static void Postfix()
        {
            if (!Prefs.DevMode)
            {
                return;
            }

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                switch (Mod.Settings.MenuToOpen)
                {
                    case OpenSetting.Settings:
                        Find.WindowStack.Add(new Dialog_ModSettings(Mod.Instance));
                        break;
                    case OpenSetting.MainMenuTab:
                        if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
                        {
                            return;
                        }

                        var def = MainTabDefOf.FindAndTrade;
                        Find.MainTabsRoot.SetCurrentTab(def, playSound: false);
                        break;
                }
            });
        }
    }

    /// <summary>
    /// Specifies which window to open automatically on save load in debug builds.
    /// </summary>
    public enum OpenSetting
    {
        /// <summary>Do not open any window automatically.</summary>
        None,

        /// <summary>Open the Mod Settings dialog.</summary>
        Settings,

        /// <summary>Open the Find And Trade main tab.</summary>
        MainMenuTab
    }
#endif

    /// <summary>
    /// RimWorld <see cref="DefOf"/> bindings for the Find And Trade main tab button.
    /// </summary>
    [DefOf]
    public static class MainTabDefOf
    {
        /// <summary>
        /// The <see cref="MainButtonDef"/> for the Find And Trade tab.
        /// </summary>
        public static MainButtonDef FindAndTrade;
    }

    /// <summary>
    /// Identifies the active sub-tab within <see cref="MainTabWindow_FindAndTrade"/>.
    /// </summary>
    public enum WindowTab
    {
        /// <summary>The primary rules list tab.</summary>
        Rules = 0,

        /// <summary>The rule query editor tab.</summary>
        Edit = 1,

        /// <summary>The automated trader selection settings tab.</summary>
        Settings = 2
    }
}
