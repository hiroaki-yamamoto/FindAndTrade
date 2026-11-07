using System.Collections.Generic;

namespace MGAutoSell.Caravans
{
    /// <summary>
    /// Defines methods for scoring settlement trade routes and selecting the optimal route.
    /// </summary>
    public interface ITradeRouteCalculator
    {
        /// <summary>
        /// Calculates the expected silver sales for a settlement given what the trader is willing to buy,
        /// the trader's available silver, and the colony's total sellable silver value.
        /// </summary>
        /// <param name="traderWillingTotal">Total value of colony items that the settlement's trader kind will trade.</param>
        /// <param name="traderSilver">Amount of silver currently in the settlement trader's stock.</param>
        /// <param name="colonyTotalSilver">Total value of all sellable items in the colony's sell cache.</param>
        /// <returns>The expected silver sales amount.</returns>
        float CalculateExpectedSales(float traderWillingTotal, float traderSilver, float colonyTotalSilver);

        /// <summary>
        /// Evaluates the <see cref="SettlementTradePriority"/> for a trade route based on its sales-to-fuel ratio
        /// and the settlement's remaining silver reserves.
        /// </summary>
        /// <param name="sales">Expected silver earned from the trade.</param>
        /// <param name="fuelCost">Fuel required to launch to the settlement.</param>
        /// <param name="traderSilver">Total silver held by the settlement trader.</param>
        /// <returns>The evaluated <see cref="SettlementTradePriority"/>.</returns>
        SettlementTradePriority EvaluatePriority(float sales, float fuelCost, float traderSilver);

        /// <summary>
        /// Selects the single best <see cref="TradeRoute"/> from a sequence of candidate routes, prioritizing
        /// higher <see cref="TradeRoute.Priority"/> first and higher <see cref="TradeRoute.Silver"/> second.
        /// </summary>
        /// <param name="routes">The candidate trade routes to evaluate.</param>
        /// <returns>The highest-ranked <see cref="TradeRoute"/>, or <c>null</c> if <paramref name="routes"/> is empty.</returns>
        TradeRoute SelectBestRoute(IEnumerable<TradeRoute> routes);
    }
}
