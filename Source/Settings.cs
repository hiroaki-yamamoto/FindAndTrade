using System;
using System.Collections.Generic;
using System.Linq;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using RimWorld;
using TD_Find_Lib;
using UnityEngine;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Stores persistent user preferences and renders the Mod Settings configuration and benchmark window.
    /// </summary>
    public class Settings : ModSettings
    {
        /// <summary>
        /// Cache of measured text label dimensions keyed by label string to avoid repeated <see cref="Text.CalcSize"/> calls.
        /// </summary>
        public Dictionary<string, Vector2> LabelSizeCache = [];

        /// <summary>
        /// When <c>true</c>, inspects every individual stack in a <see cref="Tradeable"/> row rather than only the first item.
        /// </summary>
        public bool scanEveryStack = true;

        /// <summary>
        /// When <c>true</c>, marks traders as already traded when opening a manual trade dialog so colonists do not re-trade with them.
        /// </summary>
        public bool rememberManualTrade = true;

        /// <summary>
        /// When <c>true</c>, displays potential matching <see cref="ThingDef"/>s for buy rules in the sell panel.
        /// </summary>
        public bool showAllMatchingItems = true;

        /// <summary>
        /// When <c>true</c>, displays the current stock quantity in place of the mode label on rule rows.
        /// </summary>
        public bool showQuantityInsteadOfLabel = true;

        /// <summary>
        /// When <c>true</c>, highlights import/export boxes in color when a rule will trigger a transaction on the next trade.
        /// </summary>
        public bool colorRuleCountsOnWork = true;

        /// <summary>
        /// When <c>true</c>, displays <c>min~max</c> quantity ranges on rule rows aggregated by <see cref="ThingDef"/>.
        /// </summary>
        public bool showMinMaxLabelWhereApplicable = true;

        /// <summary>
        /// Feature flag controlling experimental Odyssey shuttle trade routing.
        /// </summary>
        public static bool FF_Shuttles = false;
#if DEBUG
        /// <summary>
        /// Debug setting specifying which window to open automatically after loading a save game.
        /// </summary>
        public OpenSetting MenuToOpen = OpenSetting.None;
#endif

        private static string _benchmarkLabel;
        private static string _benchmarkLabelDisabled;
        private static string _benchmarkLabelDisabledNoDev;
        private static string _scanEveryStackLabel;
        private static string _scanEveryStackTooltip;
        private static string _rememberManualTradeLabel;
        private static string _rememberManualTradeTooltip;
        private static string _showAllMatchingItemsLabel;
        private static string _showAllMatchingItemsTooltip;
        private static string _showQuantityInsteadOfLabelLabel;
        private static string _showQuantityInsteadOfLabelTooltip;
        private static string _colorRuleCountsOnWorkLabel;
        private static string _colorRuleCountsOnWorkTooltip;
        private static string _showMinMaxLabelWhereApplicableLabel;
        private static string _showMinMaxLabelWhereApplicableTooltip;

        private static BenchmarkResults benchmarkResults = null;
        private static ItemsToSell _showAllMatchItemsEnabled;
        private static ItemsToSell _showAllMatchItemsDisabled;
        private static ItemsToSell _exampleTradeRulesCache;
        private static readonly List<TradeRule> _exampleTradeRules = [];
        private static Vector2 sellScroll = Vector2.zero;
        private static float? firstListingHeight;
        private static float maxHeightOfSides;

        /// <summary>
        /// Initializes localized setting strings and constructs sample rules for the interactive settings preview.
        /// </summary>
        public void Init()
        {
            _benchmarkLabel = "MGAutoSell.Settings.Benchmark".Translate();
            _benchmarkLabelDisabled = "MGAutoSell.Settings.BenchmarkDisabled".Translate();
            _benchmarkLabelDisabledNoDev = "MGAutoSell.Settings.BenchmarkDisabledNoDev".Translate();
            _scanEveryStackLabel = "MGAutoSell.Settings.scanEveryStackLabel".Translate();
            _scanEveryStackTooltip = "MGAutoSell.Settings.scanEveryStackTooltip".Translate();
            _rememberManualTradeLabel = "MGAutoSell.Settings.rememberManualTradeLabel".Translate();
            _rememberManualTradeTooltip = "MGAutoSell.Settings.rememberManualTradeTooltip".Translate();
            _showAllMatchingItemsLabel = "MGAutoSell.Settings.showAllMatchingItemsLabel".Translate();
            _showAllMatchingItemsTooltip = "MGAutoSell.Settings.showAllMatchingItemsTooltip".Translate();
            _showQuantityInsteadOfLabelLabel = "MGAutoSell.Settings.showQuantityInsteadOfLabelLabel".Translate();
            _showQuantityInsteadOfLabelTooltip = "MGAutoSell.Settings.showQuantityInsteadOfLabelTooltip".Translate();
            _colorRuleCountsOnWorkLabel = "MGAutoSell.Settings.colorRuleCountsOnWorkLabel".Translate();
            _colorRuleCountsOnWorkTooltip = "MGAutoSell.Settings.colorRuleCountsOnWorkTooltip".Translate();
            _showMinMaxLabelWhereApplicableLabel = "MGAutoSell.Settings.showMinMaxLabelWhereApplicableLabel".Translate();
            _showMinMaxLabelWhereApplicableTooltip = "MGAutoSell.Settings.showMinMaxLabelWhereApplicableTooltip".Translate();

            var search = new QuerySearch
            {
                name = "Drugs"
            };
            var drugsQuery = ThingQueryMaker.MakeQuery<ThingQueryThingDefCategory>();
            drugsQuery.sel = "drugs";
            search.Children.Add(drugsQuery);

            var drugsPossible = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(static x => x.FirstThingCategory == ThingCategoryDefOf.Drugs)
                .Select(x => new PotentialItem(x, "(" + search.name + ")"))
                .ToList();

            var yayo = ThingDefOf.Yayo;
            var totals = new ItemAndLabel<float>(yayo.BaseMarketValue, yayo.BaseMarketValue.ToStringMoney());
            var drugsActual = new List<SellRecord>
            {
                new(ThingDefOf.Yayo, 1, totals, totals)
            };

            _showAllMatchItemsEnabled = new ItemsToSell(drugsActual, drugsPossible, drugsActual[0].Total, null, null);
            _showAllMatchItemsDisabled = new ItemsToSell(drugsActual, [], drugsActual[0].Total, null, null);

            _exampleTradeRules.Clear();
            var itemRules = new Dictionary<TradeRule, RuleUsage>();
            var steelTradeRule = new TradeRule("Steel")
            {
                Import = 1000,
                ImportBuffer = "1000",
                Mode = TradeMode.Import
            };
            itemRules[steelTradeRule] = new RuleUsage(
                [new RuleRecord(ThingDefOf.Steel, 900)],
                (new ItemAndLabel<int>(900, "x900"), new ItemAndLabel<int>(900, "x900")));
            _exampleTradeRules.Add(steelTradeRule);

            var meals = new TradeRule("Meals")
            {
                Import = 20,
                ImportBuffer = "20",
                Mode = TradeMode.Import,
            };
            itemRules[meals] = new RuleUsage(
                [new RuleRecord(ThingDefOf.MealSimple, 24)],
                (new ItemAndLabel<int>(24, "x24"), new ItemAndLabel<int>(24, "x24")));
            _exampleTradeRules.Add(meals);

            var pleasurableDrugs = new TradeRule("Pleasurable Drugs")
            {
                Import = 5,
                ImportBuffer = "5",
                Mode = TradeMode.Maintain,
                Export = 30,
                ExportBuffer = "30"
            };
            itemRules[pleasurableDrugs] = new RuleUsage(
                [new RuleRecord(ThingDefOf.Yayo, 4), new RuleRecord(ThingDefOf.Beer, 31)],
                (new ItemAndLabel<int>(4, "x4"), new ItemAndLabel<int>(31, "x31")));
            _exampleTradeRules.Add(pleasurableDrugs);

            var organs = new TradeRule("Organs")
            {
                Export = 0,
                ExportBuffer = "0",
                Mode = TradeMode.Export
            };
            itemRules[organs] = new RuleUsage(
                [new RuleRecord(ThingDef.Named("Heart"), 2)],
                (new ItemAndLabel<int>(2, "x2"), new ItemAndLabel<int>(2, "x2")));
            _exampleTradeRules.Add(organs);

            var lowQualityArt = new TradeRule("Low Quality Art")
            {
                Export = 0,
                ExportBuffer = "0",
                Mode = TradeMode.Export
            };
            itemRules[lowQualityArt] = new RuleUsage([], (new ItemAndLabel<int>(0, "x0"), new ItemAndLabel<int>(0, "x0")));
            _exampleTradeRules.Add(lowQualityArt);

            _exampleTradeRulesCache = new ItemsToSell(null, null, null, null, itemRules);
        }

        /// <inheritdoc />
        public override void ExposeData()
        {
            Scribe_Values.Look(ref scanEveryStack, "scanEveryStack", true);
            Scribe_Values.Look(ref showAllMatchingItems, "showAllMatchingItems", true);
            Scribe_Values.Look(ref showQuantityInsteadOfLabel, "showQuantityInsteadOfLabel", true);
            Scribe_Values.Look(ref colorRuleCountsOnWork, "colorRuleCountsOnWork", true);
            Scribe_Values.Look(ref rememberManualTrade, "rememberManualTrade", true);

#if DEBUG
            Scribe_Values.Look(ref MenuToOpen, "MenuToOpen");
#endif
        }

        /// <summary>
        /// Renders the Mod Settings window contents including interactive previews and the Dev Mode benchmark runner.
        /// </summary>
        /// <param name="rect">The bounding rectangle of the settings window.</param>
        public void DoSettingsWindow(Rect rect)
        {
            var faded = new Color(1f, 1f, 1f, 0.4f);
            var color = GUI.color;

            rect.SplitHorizontallyWithMargin(out var top, out var bottom, out _, topHeight: firstListingHeight ?? 100f, compressibleMargin: 20f);
            bottom.SplitVerticallyWithMargin(out var left, out var right, 16f);
            var listing = new Listing_Standard();
            listing.Begin(top);

            listing.CheckboxLabeled(_scanEveryStackLabel, ref scanEveryStack, _scanEveryStackTooltip);
            listing.CheckboxLabeled(_rememberManualTradeLabel, ref rememberManualTrade, _rememberManualTradeTooltip);
#if DEBUG
            if (listing.ButtonTextLabeled("DEBUG: Open window on launch", MenuToOpen.ToString(), TextAnchor.MiddleLeft))
            {
                Find.WindowStack.Add(new FloatMenu([
                    new(OpenSetting.None.ToString(), () => MenuToOpen = OpenSetting.None),
                    new(OpenSetting.Settings.ToString(), () => MenuToOpen = OpenSetting.Settings),
                    new(OpenSetting.MainMenuTab.ToString().SplitCamelCase(), () => MenuToOpen = OpenSetting.MainMenuTab),
                ]));
            }
#endif
            firstListingHeight = listing.CurHeight;
            listing.End();

            // Sell list
            listing.Begin(right);

            listing.CheckboxLabeled(_showAllMatchingItemsLabel, ref showAllMatchingItems, _showAllMatchingItemsTooltip);
            if (listing.CurHeight > maxHeightOfSides)
            {
                maxHeightOfSides = listing.CurHeight;
            }
            else if (listing.CurHeight < maxHeightOfSides)
            {
                listing.Gap(maxHeightOfSides - listing.CurHeight);
            }

            listing.GapLine(4f);

            var sellList = listing.GetRect(150f);
            TabUtility.DrawSellPanel(sellList, showAllMatchingItems ? _showAllMatchItemsEnabled : _showAllMatchItemsDisabled, ref sellScroll);

            listing.End();

            // Rules
            listing.Begin(left);

            listing.CheckboxLabeled(_showQuantityInsteadOfLabelLabel, ref showQuantityInsteadOfLabel, _showQuantityInsteadOfLabelTooltip);
            listing.CheckboxLabeled(_colorRuleCountsOnWorkLabel, ref colorRuleCountsOnWork, _colorRuleCountsOnWorkTooltip);

            if (!showQuantityInsteadOfLabel)
            {
                GUI.color = faded;
            }

            listing.CheckboxLabeled(_showMinMaxLabelWhereApplicableLabel, ref showMinMaxLabelWhereApplicable, _showMinMaxLabelWhereApplicableTooltip);
            GUI.color = color;

            float height = 300f;
            if (listing.CurHeight > maxHeightOfSides)
            {
                maxHeightOfSides = listing.CurHeight;
            }
            else if (listing.CurHeight < maxHeightOfSides)
            {
                listing.Gap(maxHeightOfSides - listing.CurHeight);
            }

            listing.GapLine(4f);

            var body = listing.GetRect(height);
            var drawerListing = new Listing_Standard();
            drawerListing.Begin(body);

            for (int index = 0; index < _exampleTradeRules.Count; index++)
            {
                var tradeRule = _exampleTradeRules[index];
                TradeRuleDrawUtility.DrawRow(drawerListing.GetRect(30f), tradeRule, index, _exampleTradeRulesCache, -1);
            }

            height = drawerListing.CurHeight;
            drawerListing.End();
            listing.End();

            float biggestHeight = height > 150f ? height : 150f;
            GUI.color = faded;
            Widgets.DrawLineVertical(body.width + 8f, body.y + bottom.y, biggestHeight);
            GUI.color = color;

            bottom.SplitHorizontallyWithMargin(out bottom, out var benchmarkTray, out _, 20f, biggestHeight + maxHeightOfSides);

            if (!Prefs.DevMode)
            {
                listing.Begin(benchmarkTray.BottomPartPixels(8f + Text.LineHeight));
                GUI.color = faded;
                listing.GapLine(8f);
                listing.Label(_benchmarkLabelDisabledNoDev);
                GUI.color = color;
                listing.End();
                return;
            }

            listing.Begin(benchmarkTray);
            listing.GapLine(8f);

            if (Find.CurrentMap == null)
            {
                listing.Label(_benchmarkLabelDisabled);
                listing.End();
                return;
            }

            if (listing.ButtonText(_benchmarkLabel))
            {
                // Run it twice to get an accurate result
                if (benchmarkResults == null)
                {
                    CacheUtility.Cache(Current.Game.GetComponent<TradeRulesGameComp>(), Find.CurrentMap, out benchmarkResults, withBenchmark: true);
                }

                CacheUtility.Cache(Current.Game.GetComponent<TradeRulesGameComp>(), Find.CurrentMap, out benchmarkResults, withBenchmark: true);
            }

            if (benchmarkResults != null)
            {
                int index = 0;

                // AllItems is roughly 0.250ms on my PC, which works out to 1/4 runtime for 1ms.
                // But should extend the length as needed if going past 1ms
                long max = TimeSpan.FromMilliseconds(
                    Math.Max(1, Math.Round((double)benchmarkResults.AllItems.Value * 4 / TimeSpan.TicksPerMillisecond, 0))).Ticks;
                DoBenchmarkLine(listing, "Searching map for items", benchmarkResults.AllItems, max, ref index);
                DoBenchmarkLine(listing, "Add junk to be sold", benchmarkResults.Junk, max, ref index);
                DoBenchmarkLine(listing, "Match items to rules", benchmarkResults.Sell, max, ref index);
                DoBenchmarkLine(listing, "Select traders", benchmarkResults.Traders, max, ref index);
                DoBenchmarkLine(listing, "Sell list", benchmarkResults.SellEntries, max, ref index);
                if (showAllMatchingItems)
                {
                    DoBenchmarkLine(listing, "Regenerate all rules' matching ThingDefs lists", benchmarkResults.PossibleItems, max, ref index);
                }

                if (showQuantityInsteadOfLabel || colorRuleCountsOnWork)
                {
                    DoBenchmarkLine(listing, "Quantity per rule", benchmarkResults.Quantity, max, ref index);
                }

                DoBenchmarkLine(listing, "Create cache", benchmarkResults.BuildCache, max, ref index);
            }

            listing.End();
        }

        private static void DoBenchmarkLine(Listing listing, string label, ItemAndLabel<long> item, long max, ref int index)
        {
            var rect = listing.GetRect(26f);

            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            rect.SplitVertically(rect.width / 2f, out var left, out var right);

            var textAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(left, label);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(left, item.Label);
            Text.Anchor = textAnchor;

            float percent = Math.Clamp((float)item.Value / max, 0f, 1f);

            var color = GUI.color;
            GUI.color = Color.Lerp(ColoredText.FactionColor_Neutral, ColoredText.FactionColor_Hostile, percent * 4f);
            GUI.DrawTexture(right.ContractedBy(4f).LeftPart(percent), TexUI.FastFillTex);
            GUI.color = color;

            index++;
        }
    }
}
