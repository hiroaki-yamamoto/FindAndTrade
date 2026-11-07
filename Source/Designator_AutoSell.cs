using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MGAutoSell
{
    /// <summary>
    /// Designator that marks haulable, sellable map items as junk (<see cref="MGDesignatorDefOf.MGAutoSell"/>) to be sold unconditionally.
    /// </summary>
    [HarmonyPatch(typeof(ReverseDesignatorDatabase), "InitDesignators")]
    public class Designator_AutoSell : Designator
    {
        /// <inheritdoc />
        public override bool DragDrawMeasurements => true;

        /// <inheritdoc />
        protected override DesignationDef Designation => MGDesignatorDefOf.MGAutoSell;

        /// <inheritdoc />
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        /// <summary>
        /// Initializes a new instance of the <see cref="Designator_AutoSell"/> class.
        /// </summary>
        public Designator_AutoSell()
        {
            defaultLabel = "MGAutoSell.JunkDesignator".Translate();
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Haul;
            icon = Textures.JunkGizmo;
        }

        /// <inheritdoc />
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map))
            {
                return false;
            }

            foreach (var thing in c.GetThingList(Map))
            {
                if (thing != null && CanDesignateThing(thing))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (t == null || Map?.designationManager?.DesignationOn(t, this.Designation) != null)
            {
                return false;
            }

            return IsDefDesignatableAsJunk(t.def);
        }

        /// <summary>
        /// Determines whether items of the specified <paramref name="def"/> are eligible to be designated as junk
        /// (must be player-sellable and haulable).
        /// </summary>
        /// <param name="def">The item definition to inspect.</param>
        /// <returns><c>true</c> if the definition is sellable and haulable; otherwise, <c>false</c>.</returns>
        internal static bool IsDefDesignatableAsJunk(ThingDef def)
        {
            return def != null &&
                   def.tradeability is Tradeability.Sellable or Tradeability.All &&
                   def.EverHaulable;
        }

        /// <inheritdoc />
        public override void DesignateSingleCell(IntVec3 c)
        {
            var thingList = Map.thingGrid.ThingsListAt(c);
            foreach (var t in thingList.Where(t => CanDesignateThing(t)))
            {
                DesignateThing(t);
            }
        }

        /// <inheritdoc />
        public override void DesignateThing(Thing t)
        {
            Map.designationManager.RemoveAllDesignationsOn(t);
            Map.designationManager.AddDesignation(new Designation((LocalTargetInfo)t, Designation));
        }

        /// <inheritdoc />
        public override void SelectedUpdate() => GenUI.RenderMouseoverBracket();

        /// <summary>
        /// Harmony postfix that registers <see cref="Designator_AutoSell"/> in RimWorld's <see cref="ReverseDesignatorDatabase"/>.
        /// </summary>
        [HarmonyPostfix]
        public static void AddDesignatorToDatabase(ReverseDesignatorDatabase __instance)
        {
            __instance.AllDesignators.Add(new Designator_AutoSell());
        }
    }

    /// <summary>
    /// RimWorld <see cref="DefOf"/> bindings for the Find And Trade junk designation.
    /// </summary>
    [DefOf]
    public static class MGDesignatorDefOf
    {
        /// <summary>
        /// The <see cref="DesignationDef"/> applied to items marked as junk.
        /// </summary>
        public static DesignationDef MGAutoSell;
    }
}
