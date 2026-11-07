using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MGAutoSell.HarmonyPatches
{
    /// <summary>
    /// Harmony patch that automatically evaluates active trade rules when the vanilla <see cref="Dialog_Trade"/> window opens.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.PostOpen))]
    public static class DoTradeOnOpen
    {
        /// <summary>
        /// Postfix callback executed after <see cref="Dialog_Trade.PostOpen"/>.
        /// </summary>
        public static void Postfix()
        {
            try
            {
                var deal = TradeSession.deal;
                if (deal == null)
                {
                    return;
                }

                TradeDealProcessor.DoTradeDeal(deal);

                if (Mod.Settings.rememberManualTrade && TradeSession.trader is ILoadReferenceable)
                {
                    Current.Game.GetComponent<TradeRulesGameComp>().traders.Add(TradeSession.trader);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to process Trade UI: {ex}");
            }
        }
    }
}
