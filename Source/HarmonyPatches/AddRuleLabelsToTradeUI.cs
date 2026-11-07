using System.Collections.Generic;
using HarmonyLib;
using RimWorld;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patch that appends matched <see cref="Filter.TradeRule"/> labels to <see cref="Tradeable"/> rows in <see cref="Dialog_Trade"/>.
    /// </summary>
    [HarmonyPatch]
    public static class AddRuleLabelsToTradeUI
    {
        /// <summary>
        /// Mapping of <see cref="Tradeable"/> instances to their formatted rule suffix labels.
        /// </summary>
        public static Dictionary<Tradeable, string> ExtraLabels = new();

        /// <summary>
        /// Reference to the currently open <see cref="Dialog_Trade"/> window, if any.
        /// </summary>
        public static Dialog_Trade TradeWindow;

        /// <summary>
        /// Stores the active <see cref="Dialog_Trade"/> instance when the trade window opens.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.PostOpen))]
        [HarmonyPostfix]
        public static void Init(Dialog_Trade __instance)
        {
            TradeWindow = __instance;
        }

        /// <summary>
        /// Clears all cached rule labels and releases the <see cref="Dialog_Trade"/> reference when the window closes.
        /// </summary>
        [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.Close))]
        [HarmonyPostfix]
        public static void Cleanup(Dialog_Trade __instance)
        {
            ExtraLabels.Clear();
            TradeWindow = null;
        }

        /// <summary>
        /// Appends the matched rule label suffix to <see cref="Tradeable.Label"/> when stack scanning is enabled.
        /// </summary>
        [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.Label), MethodType.Getter)]
        [HarmonyPostfix]
        public static void AddLabel(Tradeable __instance, ref string __result)
        {
            if (Mod.Settings is not { scanEveryStack: true } || __instance == null)
            {
                return;
            }

            if (!ExtraLabels.TryGetValue(__instance, out var extraLabel))
            {
                return;
            }

            __result += extraLabel;
        }
    }
}
