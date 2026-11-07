using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MGAutoSell.Query;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;
using TD_Find_Lib;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Unit tests for <see cref="TradeQuerySearch"/>, <see cref="TradeQueryPrice"/>,
    /// <see cref="TradeQueryPriceAmount"/>, <see cref="TradeQueryTraderType"/>, and <see cref="TradeQueryTraderOrbital"/>.
    /// </summary>
    [TestClass]
    public class TradeQueryTests
    {
        private sealed class MockTradeQuery : ThingQuery, ITradeQuery
        {
            private readonly bool _matchesContext;

            public MockTradeQuery(bool matchesContext, bool enabled = true)
            {
                _matchesContext = matchesContext;
                if (!enabled)
                {
                    // ThingQuery stores enabled state in a private field or property
                    foreach (var field in typeof(ThingQuery).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (field.FieldType == typeof(bool) && field.Name.Contains("enable", System.StringComparison.OrdinalIgnoreCase))
                        {
                            field.SetValue(this, false);
                        }
                    }
                }
            }

            public override bool AppliesDirectlyTo(Thing thing) => true;

            public bool AppliesDirectlyTo(TradeContext context) => _matchesContext;
        }

        private sealed class PlainThingQuery : ThingQuery
        {
            public override bool AppliesDirectlyTo(Thing thing) => true;
        }

        private sealed class FakeTrader : ITrader
        {
            public FakeTrader(TraderKindDef traderKind)
            {
                TraderKind = traderKind;
            }

            public TraderKindDef TraderKind { get; }
            public IEnumerable<Thing> Goods => [];
            public int RandomPriceFactorSeed => 1;
            public string TraderName => "FakeTrader";
            public bool CanTradeNow => true;
            public float TradePriceImprovementOffsetForPlayer => 0f;
            public Faction Faction => null;
            public TradeCurrency TradeCurrency => TradeCurrency.Silver;
            public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator) => [];
            public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator) { }
            public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator) { }
        }

        [TestCleanup]
        public void TearDown()
        {
            TradeSession.trader = null;
        }

        [TestMethod]
        public void TradeQuerySearch_TradeQueries_ExtractsOnlyITradeQueryInstances()
        {
            var search = new TradeQuerySearch { name = "MixedSearch" };

            var tradeQ1 = new MockTradeQuery(true);
            var plainQ = new PlainThingQuery();
            var tradeQ2 = new MockTradeQuery(false);

            search.Children.queries.Add(tradeQ1);
            search.Children.queries.Add(plainQ);
            search.Children.queries.Add(tradeQ2);

            var extracted = search.TradeQueries;

            Assert.AreEqual(2, extracted.Length);
            Assert.AreSame(tradeQ1, extracted[0]);
            Assert.AreSame(tradeQ2, extracted[1]);
        }

        [TestMethod]
        public void TradeQuerySearch_AppliesTo_MatchAllQueriesTrue_EvaluatesConjunctionAndSkipsDisabled()
        {
            var search = new TradeQuerySearch { name = "AllMatchSearch", MatchAllQueries = true };
            search.Children.queries.Add(new MockTradeQuery(matchesContext: true, enabled: true));
            search.Children.queries.Add(new MockTradeQuery(matchesContext: false, enabled: false)); // Disabled query is ignored

            var context = new TradeContext(null, null, TradeAction.PlayerSells);
            Assert.IsTrue(search.AppliesTo(context));

            // Adding an enabled failing query makes MatchAllQueries return false
            search.Children.queries.Add(new MockTradeQuery(matchesContext: false, enabled: true));
            typeof(TradeQuerySearch)
                .GetField("_tradeQueries", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(search, null);

            Assert.IsFalse(search.AppliesTo(context));
        }

        [TestMethod]
        public void TradeQuerySearch_AppliesTo_MatchAllQueriesFalse_RespectsAnyQueries()
        {
            var search = new TradeQuerySearch { name = "AnyMatchSearch", MatchAllQueries = false };
            search.Children.queries.Add(new MockTradeQuery(matchesContext: false, enabled: true));
            search.Children.queries.Add(new MockTradeQuery(matchesContext: true, enabled: false)); // Disabled

            var context = new TradeContext(null, null, TradeAction.PlayerBuys);
            // 0 enabled queries match -> false
            Assert.IsFalse(search.AppliesTo(context));

            // Add an enabled matching query -> true
            search.Children.queries.Add(new MockTradeQuery(matchesContext: true, enabled: true));
            typeof(TradeQuerySearch)
                .GetField("_tradeQueries", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(search, null);

            Assert.IsTrue(search.AppliesTo(context));
        }

        [TestMethod]
        public void TradeQueryPrice_EvaluatesPriceTypeRangeAndHandlesNullContext()
        {
            var query = new TradeQueryPrice();
            Assert.AreEqual(new PriceTypeRange(PriceType.Normal, PriceType.Normal), query.sel);
            Assert.IsTrue(query.AppliesDirectlyTo((Thing)null));
            Assert.IsFalse(query.AppliesDirectlyTo((TradeContext)null));

            query.sel = new PriceTypeRange(PriceType.Cheap, PriceType.Normal);
            Assert.IsFalse(query.MatchesPriceType(PriceType.Undefined));
            Assert.IsFalse(query.MatchesPriceType(PriceType.VeryCheap));
            Assert.IsTrue(query.MatchesPriceType(PriceType.Cheap));
            Assert.IsTrue(query.MatchesPriceType(PriceType.Normal));
            Assert.IsFalse(query.MatchesPriceType(PriceType.Expensive));
        }

        [TestMethod]
        public void TradeQueryPriceAmount_EvaluatesFloatRangeAndHandlesNullContext()
        {
            var query = (TradeQueryPriceAmount)RuntimeHelpers.GetUninitializedObject(typeof(TradeQueryPriceAmount));
            query.sel = new FloatRange(10f, 50f);

            Assert.IsTrue(query.AppliesDirectlyTo((Thing)null));
            Assert.IsFalse(query.AppliesDirectlyTo((TradeContext)null));

            Assert.IsFalse(query.MatchesPriceAmount(9.9f), $"sel.min={query.sel.min}, sel.max={query.sel.max}");
            Assert.IsTrue(query.MatchesPriceAmount(10f));
            Assert.IsTrue(query.MatchesPriceAmount(25f));
            Assert.IsTrue(query.MatchesPriceAmount(50f));
            Assert.IsFalse(query.MatchesPriceAmount(50.1f));
        }

        [TestMethod]
        public void TradeQueryTraderType_MatchesLabelAndFiltersDistinctNonEmptyOptions()
        {
            var bulkDef1 = new TraderKindDef { defName = "Bulk1", label = "bulk goods trader" };
            var bulkDef2 = new TraderKindDef { defName = "Bulk2", label = "bulk goods trader" };
            var exoticDef = new TraderKindDef { defName = "Exotic", label = "exotic goods trader" };
            var emptyDef = new TraderKindDef { defName = "Empty", label = "   " };

            var distinct = TradeQueryTraderType.FilterDistinctOptions([bulkDef1, bulkDef2, exoticDef, emptyDef, null]);
            Assert.AreEqual(2, distinct.Count);
            Assert.AreSame(bulkDef1, distinct[0]);
            Assert.AreSame(exoticDef, distinct[1]);

            var query = (TradeQueryTraderType)RuntimeHelpers.GetUninitializedObject(typeof(TradeQueryTraderType));
            query.sel = bulkDef1;

            Assert.IsTrue(query.AppliesDirectlyTo((Thing)null));
            Assert.IsNull(query.TipForSel());

            TradeSession.trader = new FakeTrader(bulkDef2);
            Assert.IsTrue(query.AppliesDirectlyTo(new TradeContext(null, null, TradeAction.PlayerSells)));

            TradeSession.trader = new FakeTrader(exoticDef);
            Assert.IsFalse(query.AppliesDirectlyTo(new TradeContext(null, null, TradeAction.PlayerSells)));
        }

        [TestMethod]
        public void TradeQueryTraderOrbital_MatchesOnlyWhenSessionTraderIsOrbital()
        {
            var query = (TradeQueryTraderOrbital)RuntimeHelpers.GetUninitializedObject(typeof(TradeQueryTraderOrbital));
            Assert.IsTrue(query.AppliesDirectlyTo((Thing)null));

            TradeSession.trader = null;
            Assert.IsFalse(query.AppliesDirectlyTo(new TradeContext(null, null, TradeAction.PlayerSells)));

            TradeSession.trader = new FakeTrader(new TraderKindDef { orbital = false });
            Assert.IsFalse(query.AppliesDirectlyTo(new TradeContext(null, null, TradeAction.PlayerSells)));

            TradeSession.trader = new FakeTrader(new TraderKindDef { orbital = true });
            Assert.IsTrue(query.AppliesDirectlyTo(new TradeContext(null, null, TradeAction.PlayerSells)));
        }
    }
}
