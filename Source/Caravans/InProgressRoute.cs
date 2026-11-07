using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MGAutoSell.Caravans
{
    /// <summary>
    /// Map component tracking an in-progress automated shuttle trade route for a specific map.
    /// </summary>
    internal class InProgressRoute : MapComponent
    {
        private List<Thing> _things = [];
        private Pawn _trader;
        private Building_PassengerShuttle _shuttle;
        private Settlement _settlement;
        private long _routeStart;

        /// <summary>
        /// Gets or sets the list of items being loaded for the trade route.
        /// </summary>
        public List<Thing> Things
        {
            get => _things;
            set => _things = value;
        }

        /// <summary>
        /// Gets or sets the colonist negotiator assigned to the route.
        /// </summary>
        public Pawn Trader
        {
            get => _trader;
            set => _trader = value;
        }

        /// <summary>
        /// Gets or sets the passenger shuttle assigned to the route.
        /// </summary>
        public Building_PassengerShuttle Shuttle
        {
            get => _shuttle;
            set => _shuttle = value;
        }

        /// <summary>
        /// Gets or sets the destination world settlement for the route.
        /// </summary>
        public Settlement Settlement
        {
            get => _settlement;
            set => _settlement = value;
        }

        /// <summary>
        /// Gets or sets the elapsed ticks since the route was initiated.
        /// </summary>
        public long RouteStart
        {
            get => _routeStart;
            set => _routeStart = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InProgressRoute"/> class for the specified <paramref name="map"/>.
        /// </summary>
        /// <param name="map">The map associated with this component.</param>
        public InProgressRoute(Map map) : base(map)
        {
        }

        /// <summary>
        /// Serializes and deserializes in-progress route state.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _things, nameof(Things), LookMode.Reference);
            Scribe_References.Look(ref _trader, nameof(Trader));
            Scribe_References.Look(ref _shuttle, nameof(Shuttle));
            Scribe_References.Look(ref _settlement, nameof(Settlement));
            Scribe_Values.Look(ref _routeStart, nameof(RouteStart), 0L);
            _things ??= [];
        }

        /// <summary>
        /// Advances the route timer on each map tick.
        /// </summary>
        public override void MapComponentTick()
        {
            _routeStart++;
        }
    }
}
