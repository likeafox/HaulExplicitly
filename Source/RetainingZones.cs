using System;
using System.Collections.Generic;
#if HARMONY_1_2
using Harmony;
#elif HARMONY_2
using HarmonyLib;
#endif
using Verse;
using RimWorld;

namespace HaulExplicitly
{
    public static class RetainingZoneUtility
    {
        public static bool IsRetaining(this Zone_Stockpile z)
        {
            var hs = HaulExplicitly.GetRetainingZones();
            return hs.Contains(z);
        }

        public static void SetRetaining(this Zone_Stockpile z, bool value)
        {
            var hs = HaulExplicitly.GetRetainingZones();
            if (value)
                hs.Add(z);
            else
                hs.Remove(z);
        }
    }

    [HarmonyPatch(typeof(Zone_Stockpile), "GetGizmos")]
    class Zone_Stockpile_GetGizmos_Patch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Zone_Stockpile __instance)
        {
            foreach (Gizmo gizmo in gizmos)
                yield return gizmo;
            yield return new Command_Toggle
            {
                icon = ContentFinder<UnityEngine.Texture2D>.Get("Buttons/RetainingZone", true),
                defaultLabel = "HaulExplicitly.RetainingZoneLabel".Translate(),
                defaultDesc = "HaulExplicitly.RetainingZoneDesc".Translate(),
                isActive = (() => __instance.IsRetaining()),
                toggleAction = delegate { __instance.SetRetaining(!__instance.IsRetaining()); },
                hotKey = null
            };
        }
    }

    [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStorageFor")]
    class StoreUtility_TryFindBestBetterStorageFor_Patch
    {
        static bool Prefix(Thing t, Map map, ref bool __result)
        {
            var z = t?.GetSlotGroup()?.parent as Zone_Stockpile;
            if (z != null && z.IsRetaining())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(StoreUtility), "TryFindBestBetterStoreCellFor")]
    class StoreUtility_TryFindBestBetterStoreCellFor_Patch
    {
        static bool Prefix(Thing t, Map map, ref bool __result)
        {
            var z = t?.GetSlotGroup()?.parent as Zone_Stockpile;
            if (z != null && z.IsRetaining())
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
