using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MGAutoSell.Caravans;
using MGAutoSell.Filter;
using MGAutoSell.Records;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MGAutoSell.Tests
{
    /// <summary>
    /// Provides lightweight factory methods to instantiate RimWorld types (<see cref="ThingDef"/>, <see cref="Thing"/>,
    /// <see cref="Tradeable"/>, <see cref="TradeQuerySearch"/>, <see cref="TradeRulesGameComp"/>, etc.) in unit tests
    /// without triggering Unity shader or asset bundle static initializers, as well as a direct in-process test runner.
    /// </summary>
    internal static class TestHelper
    {
        /// <summary>
        /// Registers an <see cref="AppDomain.AssemblyResolve"/> handler before any test types are loaded so that
        /// RimWorld and mod dependencies (<c>Assembly-CSharp</c>, <c>TD_Find_Lib</c>, etc.) resolve from
        /// <see cref="AppContext.BaseDirectory"/> regardless of assembly version mismatches.
        /// </summary>
        [ModuleInitializer]
        internal static void InitializeAssemblyResolver()
        {
            var baseDir = AppContext.BaseDirectory;
            var rimWorldDir = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\.."));
            if (Directory.Exists(rimWorldDir))
            {
                Environment.CurrentDirectory = rimWorldDir;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var simpleName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simpleName))
                {
                    return null;
                }

                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return loaded;
                    }
                }

                var candidatePath = Path.Combine(baseDir, simpleName + ".dll");
                if (File.Exists(candidatePath))
                {
                    return Assembly.LoadFrom(candidatePath);
                }

                var rwCandidatePath = Path.Combine(rimWorldDir, simpleName + ".dll");
                return File.Exists(rwCandidatePath) ? Assembly.LoadFrom(rwCandidatePath) : null;
            };

            PatchShaderResolver(rimWorldDir);
        }

        private static string _rimWorldShaderDir;

        private static void PatchShaderResolver(string rimWorldDir)
        {
            _rimWorldShaderDir = Path.Combine(rimWorldDir, "Rendering", "Shaders");
            if (!Directory.Exists(_rimWorldShaderDir))
            {
                return;
            }

            try
            {
                var localShaderDir = Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders");
                Directory.CreateDirectory(localShaderDir);
                foreach (var file in Directory.GetFiles(_rimWorldShaderDir))
                {
                    var dest = Path.Combine(localShaderDir, Path.GetFileName(file));
                    if (!File.Exists(dest))
                    {
                        File.Copy(file, dest);
                    }
                }

                var matLoaderType = typeof(Verse.MatLoader);
                var dictField = matLoaderType.GetField("dict", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var requestType = matLoaderType.GetNestedType("Request", BindingFlags.Public | BindingFlags.NonPublic);
                var dictInstance = dictField?.GetValue(null) as System.Collections.IDictionary;
                if (dictInstance != null && requestType != null)
                {
                    var createDeferred = typeof(UnityEngine.Material).GetMethod(
                        "CreateDeferred",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var dummyMat = createDeferred != null
                        ? (UnityEngine.Material)createDeferred.Invoke(null, [new Func<UnityEngine.Material>(static () => null)])
                        : (UnityEngine.Material)RuntimeHelpers.GetUninitializedObject(typeof(UnityEngine.Material));
                    var pathField = requestType.GetField("path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var rqField = requestType.GetField("renderQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    foreach (var targetType in new[] { typeof(Verse.TexUI), typeof(Verse.Widgets) })
                    {
                        var cctor = targetType.TypeInitializer;
                        if (cctor == null)
                        {
                            continue;
                        }

                        var instrs = HarmonyLib.PatchProcessor.GetOriginalInstructions(cctor);
                        for (int i = 0; i < instrs.Count; i++)
                        {
                            if (instrs[i].operand is string str)
                            {
                                foreach (int rq in new[] { -1, 0, 3000, 3100, 4000 })
                                {
                                    var req = Activator.CreateInstance(requestType);
                                    pathField?.SetValue(req, str);
                                    rqField?.SetValue(req, rq);
                                    dictInstance[req] = dummyMat;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TestHelper] PatchShaderResolver warning: {ex}");
            }
        }

        /// <summary>
        /// Direct entry point that executes all <see cref="TestClassAttribute"/> and <see cref="TestMethodAttribute"/>
        /// tests in-process when invoked via <c>dotnet MGAutoSell.Tests.dll</c>, while remaining 100% compatible with VSTest.
        /// </summary>
        public static int Main(string[] args)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            var assembly = typeof(TestHelper).Assembly;
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<TestClassAttribute>() == null)
                {
                    continue;
                }

                MethodInfo initMethod = null;
                MethodInfo cleanupMethod = null;
                var testMethods = new List<MethodInfo>();

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.GetCustomAttribute<TestInitializeAttribute>() != null)
                    {
                        initMethod = method;
                    }
                    else if (method.GetCustomAttribute<TestCleanupAttribute>() != null)
                    {
                        cleanupMethod = method;
                    }
                    else if (method.GetCustomAttribute<TestMethodAttribute>() != null)
                    {
                        testMethods.Add(method);
                    }
                }

                foreach (var testMethod in testMethods)
                {
                    var instance = Activator.CreateInstance(type);
                    try
                    {
                        initMethod?.Invoke(instance, null);
                        testMethod.Invoke(instance, null);
                        passed++;
                    }
                    catch (TargetInvocationException tie)
                    {
                        failed++;
                        var inner = tie.InnerException ?? tie;
                        failures.Add($"[FAIL] {type.Name}.{testMethod.Name}: {inner}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add($"[FAIL] {type.Name}.{testMethod.Name}: {ex}");
                    }
                    finally
                    {
                        try
                        {
                            cleanupMethod?.Invoke(instance, null);
                        }
                        catch (Exception cleanupEx)
                        {
                            failed++;
                            failures.Add($"[CLEANUP FAIL] {type.Name}.{testMethod.Name}: {cleanupEx}");
                        }
                    }
                }
            }

            Console.WriteLine($"Test Summary: {passed + failed} total, {passed} passed, {failed} failed.");
            foreach (var failure in failures)
            {
                Console.Error.WriteLine(failure);
            }

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Creates a <see cref="ThingDef"/> instance without invoking <see cref="BuildableDef"/>'s static shader initializers.
        /// </summary>
        public static ThingDef CreateThingDef(
            string defName,
            string label = null,
            Tradeability tradeability = Tradeability.All,
            bool alwaysHaulable = true)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.defName = defName;
            def.label = label ?? defName;
            def.tradeability = tradeability;
            def.alwaysHaulable = alwaysHaulable;
            return def;
        }

        private static int _nextThingId = 1;

        /// <summary>
        /// Creates a <see cref="Thing"/> instance with the specified <paramref name="def"/> and <paramref name="stackCount"/>.
        /// </summary>
        public static Thing CreateThing(ThingDef def, int stackCount = 1)
        {
            var thing = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
            thing.def = def;
            thing.stackCount = stackCount;
            thing.thingIDNumber = System.Threading.Interlocked.Increment(ref _nextThingId);
            return thing;
        }

        /// <summary>
        /// Creates a <see cref="Pawn"/> instance with the specified <paramref name="thingIdNumber"/>.
        /// </summary>
        public static Pawn CreatePawn(int thingIdNumber)
        {
            var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
            pawn.thingIDNumber = thingIdNumber;
            pawn.stackCount = 1;
            return pawn;
        }

        /// <summary>
        /// Creates a <see cref="Tradeable"/> instance populated with optional colony and trader stacks and transfer count.
        /// </summary>
        public static Tradeable CreateTradeable(
            ThingDef def,
            int colonyCount = 0,
            int traderCount = 0,
            int countToTransfer = 0)
        {
            var tradeable = new Tradeable
            {
                thingsColony = [],
                thingsTrader = []
            };

            if (countToTransfer != 0)
            {
                TradeDealProcessor.SetTradeCount(tradeable, countToTransfer);
            }

            if (colonyCount > 0)
            {
                tradeable.thingsColony.Add(CreateThing(def, colonyCount));
            }

            if (traderCount > 0)
            {
                tradeable.thingsTrader.Add(CreateThing(def, traderCount));
            }

            return tradeable;
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

        /// <summary>
        /// Creates an initialized <see cref="TradeRulesGameComp"/> without requiring a running <see cref="Game"/> instance.
        /// </summary>
        public static TradeRulesGameComp CreateGameComp()
        {
            var comp = (TradeRulesGameComp)RuntimeHelpers.GetUninitializedObject(typeof(TradeRulesGameComp));
            comp.tradeRules = new TradeRulesGroup();
            comp.traders = [];
            comp.autoTraderIDs = [];
            comp.SellCache = [];
            comp.SellerOverride = [];
            comp.NextTradeRoute = [];
            return comp;
        }

        /// <summary>
        /// Creates an uninitialized <see cref="Map"/> reference for dictionary and identity comparisons in unit tests.
        /// </summary>
        public static Map CreateMap()
        {
            return (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        }

        /// <summary>
        /// Creates an uninitialized <see cref="Settlement"/> reference for unit tests.
        /// </summary>
        public static Settlement CreateSettlement()
        {
            return (Settlement)RuntimeHelpers.GetUninitializedObject(typeof(Settlement));
        }

        /// <summary>
        /// Creates an uninitialized <see cref="Building_PassengerShuttle"/> reference for unit tests.
        /// </summary>
        public static Building_PassengerShuttle CreateShuttle(int hitPoints = 100)
        {
            var shuttle = (Building_PassengerShuttle)RuntimeHelpers.GetUninitializedObject(typeof(Building_PassengerShuttle));
            shuttle.HitPoints = hitPoints;
            return shuttle;
        }

        /// <summary>
        /// Creates an uninitialized <see cref="SettlementTracker"/> with initialized <see cref="SettlementTracker.Settlements"/>
        /// and <see cref="SettlementTracker.Shuttles"/> collections.
        /// </summary>
        public static SettlementTracker CreateSettlementTracker()
        {
            var tracker = (SettlementTracker)RuntimeHelpers.GetUninitializedObject(typeof(SettlementTracker));
            tracker.Settlements = [];
            tracker.Shuttles = [];
            typeof(SettlementTracker)
                .GetField("_distanceCache", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(tracker, new Dictionary<(Map, Settlement), float>());
            return tracker;
        }
    }
}
