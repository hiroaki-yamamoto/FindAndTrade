using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MGAutoSell.AI
{
    /// <summary>
    /// WorkGiver that dispatches autonomous colonist negotiators to visiting traders or comms consoles when active trade rules exist.
    /// </summary>
    public class WorkGiver_Barter : WorkGiver_Scanner
    {
        /// <inheritdoc />
        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        /// <inheritdoc />
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_CommsConsole>()
                .Concat<Thing>(pawn.Map.mapPawns.AllPawnsSpawned.Where(x => x.trader != null));

        /// <inheritdoc />
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            var comp = Current.Game?.GetComponent<TradeRulesGameComp>();
            return ShouldSkipForPawn(comp, pawn?.thingIDNumber ?? -1, forced);
        }

        /// <summary>
        /// Evaluates whether autonomous bartering should be skipped for the specified <paramref name="pawnThingId"/>.
        /// </summary>
        /// <param name="comp">The active <see cref="TradeRulesGameComp"/>.</param>
        /// <param name="pawnThingId">The colonist's <see cref="Thing.thingIDNumber"/>.</param>
        /// <param name="forced">Whether the job was manually prioritized by the player.</param>
        /// <returns><c>true</c> if bartering should be skipped; otherwise, <c>false</c>.</returns>
        internal static bool ShouldSkipForPawn(TradeRulesGameComp comp, int pawnThingId, bool forced = false)
        {
            if (comp?.tradeRules == null || !comp.tradeRules.Any())
            {
                return true;
            }

            return !forced && (!comp.autoTrade || comp.autoTraderIDs == null || !comp.autoTraderIDs.Contains(pawnThingId));
        }

        /// <inheritdoc />
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            new(AIDefOf.MGJob_Barter, t);

        /// <inheritdoc />
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            var map = t.Map;
            var comp = Current.Game.GetComponent<TradeRulesGameComp>();
            switch (t)
            {
                case Building_CommsConsole commsConsole:
                    if (commsConsole.Faction != pawn.Faction || commsConsole.IsBurning() ||
                        commsConsole.IsForbidden(pawn) || !commsConsole.CanUseCommsNow ||
                        !pawn.CanReserveAndReach((LocalTargetInfo)commsConsole, PathEndMode.InteractionCell,
                            Danger.Some) || commsConsole.Map.passingShipManager.passingShips?.Any() is not true)
                        return false;

                    var hasPassingShip = map.passingShipManager.passingShips.Any(x =>
                        x is TradeShip tradeShip &&
                        pawn.CanTradeWith(tradeShip.Faction, ((ITrader)tradeShip).TraderKind) &&
                        (forced || !comp.traders.Contains(tradeShip)));

                    if (hasPassingShip && forced)
                        map.passingShipManager.passingShips.ForEach(x => comp.traders.Remove(x as ITrader));
                    return hasPassingShip;
                case Pawn trader:
                    return trader.CanTradeNow && pawn.CanTradeWith(trader.Faction, trader.TraderKind) &&
                           pawn.CanReserveAndReach(trader, PathEndMode.Touch, Danger.Some) &&
                           (forced || !comp.traders.Contains(trader));
                default:
                    return false;
            }
        }
    }
}
