using System.Collections.Generic;
using MGAutoSell.Caravans;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SettlementTracker"/>, <see cref="InProgressRoute"/>,
    /// <see cref="TradeRulesGameComp"/>, and <see cref="TradeRouteCalculator.IsBetterRoute"/>.
    /// </summary>
    [TestClass]
    public class CaravanAndSettlementTests
    {
        [TestMethod]
        public void SettlementTracker_GetBestForMap_PrioritizesFullHealthOverHigherFuelDamagedShuttle()
        {
            var tracker = TestHelper.CreateSettlementTracker();
            var mapA = TestHelper.CreateMap();
            var mapB = TestHelper.CreateMap();

            var damagedHighFuel = TestHelper.CreateShuttle(hitPoints: 50);
            var fullHealthLowFuel = TestHelper.CreateShuttle(hitPoints: 100);
            var fullHealthHighFuel = TestHelper.CreateShuttle(hitPoints: 100);
            var otherMapShuttle = TestHelper.CreateShuttle(hitPoints: 100);

            tracker.Shuttles.Add(damagedHighFuel);
            tracker.Shuttles.Add(fullHealthLowFuel);
            tracker.Shuttles.Add(fullHealthHighFuel);
            tracker.Shuttles.Add(otherMapShuttle);

            var fuels = new Dictionary<Building_PassengerShuttle, float>
            {
                [damagedHighFuel] = 500f,
                [fullHealthLowFuel] = 100f,
                [fullHealthHighFuel] = 250f,
                [otherMapShuttle] = 999f
            };

            var maps = new Dictionary<Building_PassengerShuttle, Map>
            {
                [damagedHighFuel] = mapA,
                [fullHealthLowFuel] = mapA,
                [fullHealthHighFuel] = mapA,
                [otherMapShuttle] = mapB
            };

            var best = tracker.GetBestForMap(
                mapA,
                fuelGetter: s => fuels[s],
                mapGetter: s => maps[s],
                isFullHealthEvaluator: s => s.HitPoints >= 100);

            Assert.AreSame(fullHealthHighFuel, best);
        }

        [TestMethod]
        public void SettlementTracker_GetBestForMap_WhenAllDamaged_SelectsHighestFuel()
        {
            var tracker = TestHelper.CreateSettlementTracker();
            var map = TestHelper.CreateMap();

            var damaged1 = TestHelper.CreateShuttle(hitPoints: 40);
            var damaged2 = TestHelper.CreateShuttle(hitPoints: 60);

            tracker.Shuttles.Add(damaged1);
            tracker.Shuttles.Add(damaged2);

            var best = tracker.GetBestForMap(
                map,
                fuelGetter: s => s == damaged1 ? 80f : 140f,
                mapGetter: _ => map,
                isFullHealthEvaluator: static _ => false);

            Assert.AreSame(damaged2, best);
            Assert.IsNull(tracker.GetBestForMap(null));
        }

        [TestMethod]
        public void SettlementTracker_GetDistances_CachesDistancesAndAppliesExclusionAndMaxDistance()
        {
            var tracker = TestHelper.CreateSettlementTracker();
            var map = TestHelper.CreateMap();

            var nearSettlement = TestHelper.CreateSettlement();
            var farSettlement = TestHelper.CreateSettlement();
            var excludedSettlement = TestHelper.CreateSettlement();

            tracker.Settlements.Add(nearSettlement);
            tracker.Settlements.Add(farSettlement);
            tracker.Settlements.Add(excludedSettlement);
            tracker.Settlements.Add(null);

            int calculatorCalls = 0;
            float DistanceCalc(Map m, Settlement s)
            {
                calculatorCalls++;
                return s == nearSettlement ? 15f : 80f;
            }

            var result1 = tracker.GetDistances(
                map,
                maxDistance: static _ => 50f,
                settlementPredicate: s => s == excludedSettlement,
                distanceCalculator: DistanceCalc);

            Assert.AreEqual(1, result1.Count);
            Assert.AreEqual(15f, result1[nearSettlement]);
            Assert.AreEqual(2, calculatorCalls); // nearSettlement and farSettlement calculated; excludedSettlement skipped before calc

            // Second call uses cached distances without invoking DistanceCalc again
            var result2 = tracker.GetDistances(
                map,
                maxDistance: static _ => 100f,
                settlementPredicate: s => s == excludedSettlement,
                distanceCalculator: DistanceCalc);

            Assert.AreEqual(2, result2.Count);
            Assert.AreEqual(15f, result2[nearSettlement]);
            Assert.AreEqual(80f, result2[farSettlement]);
            Assert.AreEqual(2, calculatorCalls);
        }

        [TestMethod]
        public void InProgressRoute_PropertiesAndTick_TrackStateCorrectly()
        {
            var map = TestHelper.CreateMap();
            var route = new InProgressRoute(map);
            var pawn = TestHelper.CreatePawn(1);
            var shuttle = TestHelper.CreateShuttle();
            var settlement = TestHelper.CreateSettlement();
            var item = TestHelper.CreateThing(TestHelper.CreateThingDef("Steel"), 25);

            route.Things = [item];
            route.Trader = pawn;
            route.Shuttle = shuttle;
            route.Settlement = settlement;
            route.RouteStart = 100L;

            route.MapComponentTick();
            route.MapComponentTick();

            Assert.AreEqual(1, route.Things.Count);
            Assert.AreSame(pawn, route.Trader);
            Assert.AreSame(shuttle, route.Shuttle);
            Assert.AreSame(settlement, route.Settlement);
            Assert.AreEqual(102L, route.RouteStart);
        }

        [TestMethod]
        public void TradeRulesGameComp_GetSilverCountFromStock_And_FetchCacheHit_WorkCorrectly()
        {
            var silverDef = TestHelper.CreateThingDef("Silver");
            var steelDef = TestHelper.CreateThingDef("Steel");

            var stock = new List<Thing>
            {
                null,
                TestHelper.CreateThing(steelDef, 200),
                TestHelper.CreateThing(silverDef, 1450)
            };

            Assert.AreEqual(1450, TradeRulesGameComp.GetSilverCountFromStock(stock, silverDef));
            Assert.AreEqual(0, TradeRulesGameComp.GetSilverCountFromStock([], silverDef));
            Assert.AreEqual(0, TradeRulesGameComp.GetSilverCountFromStock(null, silverDef));
            Assert.AreEqual(0, TradeRulesGameComp.GetTraderSilverCount(null, silverDef));

            var comp = TestHelper.CreateGameComp();
            var map = TestHelper.CreateMap();
            var expectedCache = new ItemsToSell([], [], new ItemAndLabel<float>(500f, "$500"), null, []);
            comp.SellCache[map] = expectedCache;

            var fetched = comp.Fetch(map, refresh: false);
            Assert.AreSame(expectedCache, fetched);
        }

        [TestMethod]
        public void TradeRouteCalculator_IsBetterRoute_HandlesNullsAndTiesCorrectly()
        {
            var routeLow = new TradeRoute(null, 10f, 300f, 50f, SettlementTradePriority.Low);
            var routeLowHigherSilver = new TradeRoute(null, 10f, 500f, 50f, SettlementTradePriority.Low);
            var routeLowSameSilver = new TradeRoute(null, 12f, 500f, 50f, SettlementTradePriority.Low);

            Assert.IsFalse(TradeRouteCalculator.IsBetterRoute(null, null));
            Assert.IsTrue(TradeRouteCalculator.IsBetterRoute(routeLow, null));
            Assert.IsFalse(TradeRouteCalculator.IsBetterRoute(null, routeLow));
            Assert.IsTrue(TradeRouteCalculator.IsBetterRoute(routeLowHigherSilver, routeLow));
            Assert.IsFalse(TradeRouteCalculator.IsBetterRoute(routeLowSameSilver, routeLowHigherSilver));
        }
    }
}
