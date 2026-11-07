using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MGAutoSell.AI;
using MGAutoSell.Extensions;
using MGAutoSell.Filter;
using MGAutoSell.HarmonyPatches;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for extension methods (<see cref="TradeableExtensions"/>, <see cref="WidgetRowExtensions"/>),
    /// Harmony patch helpers (<see cref="AddRuleLabelsToTradeUI"/>, <see cref="TradePawnCleanup"/>,
    /// <see cref="TradeShipCleanup"/>, <see cref="ShuttleTradeGizmo"/>, <see cref="SettlementTradeGizmo"/>),
    /// AI workgiver filtering (<see cref="WorkGiver_Barter"/>), designator eligibility (<see cref="Designator_AutoSell"/>),
    /// and UI sorting/reordering helpers (<see cref="TabUtility"/>, <see cref="MainTabWindow_FindAndTrade"/>).
    /// </summary>
    [TestClass]
    public class ExtensionsAndPatchesTests
    {
        [TestMethod]
        public void TradeableExtensions_AnyThingNotJunk_EvaluatesAllFallbackBranches()
        {
            var steelDef = TestHelper.CreateThingDef("Steel");
            var colonyJunk = TestHelper.CreateThing(steelDef, 10);
            var colonyClean = TestHelper.CreateThing(steelDef, 20);
            var traderClean = TestHelper.CreateThing(steelDef, 15);
            var junkSet = new HashSet<Thing> { colonyJunk };

            // Null or empty tradeable -> false
            Assert.IsFalse(((Tradeable)null).AnyThingNotJunk(out var item0, junkSet));
            Assert.IsNull(item0);

            var emptyTradeable = new Tradeable { thingsColony = [], thingsTrader = [] };
            Assert.IsFalse(emptyTradeable.AnyThingNotJunk(out var item1, junkSet));
            Assert.IsNull(item1);

            // Branch 1: First colony item is not junk -> returns immediately
            var cleanColonyTradeable = new Tradeable { thingsColony = [colonyClean], thingsTrader = [] };
            Assert.IsTrue(cleanColonyTradeable.AnyThingNotJunk(out var item2, junkSet));
            Assert.AreSame(colonyClean, item2);

            // Branch 2: First colony item is junk, but trader has an item -> returns FirstThingTrader
            var traderFallbackTradeable = new Tradeable { thingsColony = [colonyJunk], thingsTrader = [traderClean] };
            Assert.IsTrue(traderFallbackTradeable.AnyThingNotJunk(out var item3, junkSet));
            Assert.AreSame(traderClean, item3);

            // Branch 3: First colony item is junk, no trader item, second colony stack is not junk -> returns second colony stack
            var secondStackTradeable = new Tradeable { thingsColony = [colonyJunk, colonyClean], thingsTrader = [] };
            Assert.IsTrue(secondStackTradeable.AnyThingNotJunk(out var item4, junkSet));
            Assert.AreSame(colonyClean, item4);

            // Branch 4: All items are junk -> returns false
            var allJunkTradeable = new Tradeable { thingsColony = [colonyJunk], thingsTrader = [] };
            Assert.IsFalse(allJunkTradeable.AnyThingNotJunk(out var item5, junkSet));
            Assert.IsNull(item5);
        }

        [TestMethod]
        public void WidgetRowExtensions_TryUpdateIntFieldFromText_ParsesClampsAndHandlesPartialInput()
        {
            int val = 25;
            string buffer = "25";

            // Unchanged input -> false
            Assert.IsFalse(WidgetRowExtensions.TryUpdateIntFieldFromText("25", ref val, ref buffer));
            Assert.AreEqual(25, val);

            // Valid integer -> updates both
            Assert.IsTrue(WidgetRowExtensions.TryUpdateIntFieldFromText("450", ref val, ref buffer));
            Assert.AreEqual(450, val);
            Assert.AreEqual("450", buffer);

            // Negative integer -> clamps to 0
            Assert.IsTrue(WidgetRowExtensions.TryUpdateIntFieldFromText("-50", ref val, ref buffer));
            Assert.AreEqual(0, val);
            Assert.AreEqual("0", buffer);

            // Empty string or '-' during typing -> updates buffer without throwing
            Assert.IsTrue(WidgetRowExtensions.TryUpdateIntFieldFromText("", ref val, ref buffer));
            Assert.AreEqual("", buffer);

            Assert.IsTrue(WidgetRowExtensions.TryUpdateIntFieldFromText("-", ref val, ref buffer));
            Assert.AreEqual("-", buffer);

            // Non-numeric garbage -> ignored
            Assert.IsFalse(WidgetRowExtensions.TryUpdateIntFieldFromText("abc", ref val, ref buffer));
            Assert.AreEqual("-", buffer);
        }

        [TestMethod]
        public void AddRuleLabelsToTradeUI_InitCleanupAndAddLabel_BehaveCorrectly()
        {
            var prevSettings = Mod.Settings;
            try
            {
                var tradeable = TestHelper.CreateTradeable(TestHelper.CreateThingDef("Steel"), colonyCount: 10);
                var fakeWindow = (Dialog_Trade)RuntimeHelpers.GetUninitializedObject(typeof(Dialog_Trade));

                AddRuleLabelsToTradeUI.Init(fakeWindow);
                Assert.AreSame(fakeWindow, AddRuleLabelsToTradeUI.TradeWindow);

                AddRuleLabelsToTradeUI.ExtraLabels[tradeable] = " - (Metals)";

                // When scanEveryStack is true, appends suffix
                Mod.Settings = new Settings { scanEveryStack = true };
                string label = "Steel";
                AddRuleLabelsToTradeUI.AddLabel(tradeable, ref label);
                Assert.AreEqual("Steel - (Metals)", label);

                // When scanEveryStack is false, does not append
                Mod.Settings.scanEveryStack = false;
                string unchanged = "Steel";
                AddRuleLabelsToTradeUI.AddLabel(tradeable, ref unchanged);
                Assert.AreEqual("Steel", unchanged);

                AddRuleLabelsToTradeUI.Cleanup(fakeWindow);
                Assert.IsNull(AddRuleLabelsToTradeUI.TradeWindow);
                Assert.AreEqual(0, AddRuleLabelsToTradeUI.ExtraLabels.Count);
            }
            finally
            {
                Mod.Settings = prevSettings;
                AddRuleLabelsToTradeUI.Cleanup(null);
            }
        }

        [TestMethod]
        public void TradePawnAndShipCleanup_RemoveTrackedTradersAndSellerOverrides()
        {
            var comp = TestHelper.CreateGameComp();
            var map1 = TestHelper.CreateMap();
            var map2 = TestHelper.CreateMap();
            var pawnA = TestHelper.CreatePawn(10);
            var pawnB = TestHelper.CreatePawn(20);

            comp.traders.Add(pawnA);
            comp.traders.Add(pawnB);
            comp.SellerOverride[map1] = pawnA;
            comp.SellerOverride[map2] = pawnB;

            TradePawnCleanup.CleanupPawn(comp, pawnA);

            Assert.IsFalse(comp.traders.Contains(pawnA));
            Assert.IsTrue(comp.traders.Contains(pawnB));
            Assert.IsFalse(comp.SellerOverride.ContainsKey(map1));
            Assert.AreSame(pawnB, comp.SellerOverride[map2]);

            var tradeShip = (TradeShip)RuntimeHelpers.GetUninitializedObject(typeof(TradeShip));
            var nonTraderShip = (PassingShip)RuntimeHelpers.GetUninitializedObject(typeof(PassingShip));
            comp.traders.Add(tradeShip);

            TradeShipCleanup.CleanupShip(comp, nonTraderShip);
            Assert.IsTrue(comp.traders.Contains(tradeShip));

            TradeShipCleanup.CleanupShip(comp, tradeShip);
            Assert.IsFalse(comp.traders.Contains(tradeShip));
        }

        [TestMethod]
        public void ShuttleAndSettlementGizmos_ToggleAndRemoveDestroyed_ManageSetsCorrectly()
        {
            var tracker = TestHelper.CreateSettlementTracker();
            var shuttle = TestHelper.CreateShuttle();
            var regularBuilding = (Building)RuntimeHelpers.GetUninitializedObject(typeof(Building));
            var settlement = TestHelper.CreateSettlement();

            Assert.IsTrue(ShuttleTradeGizmo.ToggleShuttle(tracker.Shuttles, shuttle));
            Assert.IsTrue(tracker.Shuttles.Contains(shuttle));
            Assert.IsFalse(ShuttleTradeGizmo.ToggleShuttle(tracker.Shuttles, shuttle));
            Assert.IsFalse(tracker.Shuttles.Contains(shuttle));

            tracker.Shuttles.Add(shuttle);
            Assert.IsFalse(ShuttleTradeGizmo.RemoveDestroyedShuttle(tracker, regularBuilding));
            Assert.IsTrue(ShuttleTradeGizmo.RemoveDestroyedShuttle(tracker, shuttle));
            Assert.IsFalse(tracker.Shuttles.Contains(shuttle));

            Assert.IsTrue(SettlementTradeGizmo.ToggleSettlement(tracker.Settlements, settlement));
            Assert.IsTrue(tracker.Settlements.Contains(settlement));
            Assert.IsFalse(SettlementTradeGizmo.ToggleSettlement(tracker.Settlements, settlement));
            Assert.IsFalse(tracker.Settlements.Contains(settlement));
        }

        [TestMethod]
        public void WorkGiverBarter_ShouldSkipForPawn_EvaluatesRulesAutoTradeAndForcedFlag()
        {
            var comp = TestHelper.CreateGameComp();

            // No trade rules -> always skip, even if forced
            Assert.IsTrue(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 42, forced: false));
            Assert.IsTrue(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 42, forced: true));

            comp.tradeRules.Add(new TradeRule());

            // Rules exist, autoTrade is false: skip when not forced, allow when forced
            comp.autoTrade = false;
            comp.autoTraderIDs.Add(42);
            Assert.IsTrue(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 42, forced: false));
            Assert.IsFalse(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 42, forced: true));

            // autoTrade is true: allow only pawns in autoTraderIDs when not forced
            comp.autoTrade = true;
            Assert.IsFalse(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 42, forced: false));
            Assert.IsTrue(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 99, forced: false));
            Assert.IsFalse(WorkGiver_Barter.ShouldSkipForPawn(comp, pawnThingId: 99, forced: true));
        }

        [TestMethod]
        public void DesignatorAutoSell_IsDefDesignatableAsJunk_ValidatesTradeabilityAndHaulability()
        {
            Assert.IsFalse(Designator_AutoSell.IsDefDesignatableAsJunk(null));

            var sellableHaulable = TestHelper.CreateThingDef("Steel", tradeability: Tradeability.Sellable, alwaysHaulable: true);
            var allHaulable = TestHelper.CreateThingDef("Gold", tradeability: Tradeability.All, alwaysHaulable: true);
            var buyableOnly = TestHelper.CreateThingDef("BuyOnly", tradeability: Tradeability.Buyable, alwaysHaulable: true);
            var nonTradeable = TestHelper.CreateThingDef("NoTrade", tradeability: Tradeability.None, alwaysHaulable: true);
            var notHaulable = TestHelper.CreateThingDef("BuildingWall", tradeability: Tradeability.All, alwaysHaulable: false);

            Assert.IsTrue(Designator_AutoSell.IsDefDesignatableAsJunk(sellableHaulable));
            Assert.IsTrue(Designator_AutoSell.IsDefDesignatableAsJunk(allHaulable));
            Assert.IsFalse(Designator_AutoSell.IsDefDesignatableAsJunk(buyableOnly));
            Assert.IsFalse(Designator_AutoSell.IsDefDesignatableAsJunk(nonTradeable));
            Assert.IsFalse(Designator_AutoSell.IsDefDesignatableAsJunk(notHaulable));
        }

        [TestMethod]
        public void TabUtility_CompareTraders_OrdersIdeologyLeaderFirstThenByImprovementDescending()
        {
            var regularLow = new TraderRecord(null, "Low", null, "5%", 0.05f, IsLeader: false);
            var regularHigh = new TraderRecord(null, "High", null, "25%", 0.25f, IsLeader: false);
            var leaderMedium = new TraderRecord(null, "Leader", null, "12%", 0.12f, IsLeader: true);

            var list = new List<TraderRecord> { regularLow, regularHigh, leaderMedium };
            list.Sort(TabUtility.CompareTraders);

            Assert.AreSame(leaderMedium, list[0]);
            Assert.AreSame(regularHigh, list[1]);
            Assert.AreSame(regularLow, list[2]);
        }

        [TestMethod]
        public void MainTabWindow_ReorderList_HandlesMoveDownAndMoveUpCorrectly()
        {
            // Moving index 0 ("A") to below index 2 ("C") reports to = 3 in ReorderableWidget -> inserts at 3 - 1 = 2
            var listDown = new List<string> { "A", "B", "C", "D" };
            MainTabWindow_FindAndTrade.ReorderList(listDown, from: 0, to: 3);
            CollectionAssert.AreEqual(new List<string> { "B", "C", "A", "D" }, listDown);

            // Moving index 3 ("D") to before index 1 ("B") reports to = 1 -> inserts at 1
            var listUp = new List<string> { "A", "B", "C", "D" };
            MainTabWindow_FindAndTrade.ReorderList(listUp, from: 3, to: 1);
            CollectionAssert.AreEqual(new List<string> { "A", "D", "B", "C" }, listUp);
        }
    }
}
