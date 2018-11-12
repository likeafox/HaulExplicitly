using Verse;
using RimWorld;

namespace HaulExplicitly
{
    public static class HaulablesUtilities
    {
        public static bool HasHaulabilityToggled(this Thing t)
        {
            //the haul designation "toggles" whether something is haulable
            return t.MapHeld.designationManager.DesignationOn(t, DesignationDefOf.Haul) != null;
        }
        public static bool IsAHaulableSetToHaulable(this Thing t)
        {
            if (!t.def.EverHaulable)
                return false;
            //(alwaysHaulable gets priority if alwaysHaulable and designateHaulable are both set)
            return t.def.alwaysHaulable != t.HasHaulabilityToggled();
        }
        public static bool IsAHaulableSetToUnhaulable(this Thing t)
        {
            if (!t.def.EverHaulable)
                return false;
            return t.def.alwaysHaulable == t.HasHaulabilityToggled();
        }

        public static bool ShouldBeHaulableExt(this ListerHaulables lh,
            Thing t, bool will_toggle_haul_des = false)
        {
            if (t.IsForbidden(Faction.OfPlayer) || t.IsInValidBestStorage())
                return false;
            if (will_toggle_haul_des)
                return t.IsAHaulableSetToUnhaulable();
            return t.IsAHaulableSetToHaulable();
        }

        public static void ToggleHaulDesignation(this Thing t)
        {
            DesignationManager dm = t.MapHeld.designationManager;
            Designation des = dm.DesignationOn(t, DesignationDefOf.Haul);
            if (des != null)
                dm.RemoveDesignation(des);
            else
                dm.AddDesignation(new Designation(t, DesignationDefOf.Haul));
        }
    }
}
