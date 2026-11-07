using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MGAutoSell.Filter;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Provides lightweight factory methods to instantiate RimWorld types (<see cref="ThingDef"/>, <see cref="Thing"/>,
    /// <see cref="TradeQuerySearch"/>) in unit tests without triggering Unity shader or asset bundle static initializers.
    /// </summary>
    internal static class TestHelper
    {
        /// <summary>
        /// Creates a <see cref="ThingDef"/> instance without invoking <see cref="BuildableDef"/>'s static shader initializers.
        /// </summary>
        public static ThingDef CreateThingDef(string defName, string label = null)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.defName = defName;
            def.label = label ?? defName;
            return def;
        }

        /// <summary>
        /// Creates a <see cref="Thing"/> instance with the specified <paramref name="def"/> and <paramref name="stackCount"/>.
        /// </summary>
        public static Thing CreateThing(ThingDef def, int stackCount = 1)
        {
            var thing = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
            thing.def = def;
            thing.stackCount = stackCount;
            return thing;
        }

        /// <summary>
        /// Creates a <see cref="TradeQuerySearch"/> instance with the specified <paramref name="name"/> and <paramref name="allItems"/>.
        /// </summary>
        public static TradeQuerySearch CreateSearch(string name, List<ThingDef> allItems = null)
        {
            var search = (TradeQuerySearch)RuntimeHelpers.GetUninitializedObject(typeof(TradeQuerySearch));
            search.name = name;
            search.AllItems = allItems;
            return search;
        }
    }
}
