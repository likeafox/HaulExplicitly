using System;
using System.Collections.Generic;
using Harmony;
using Verse;
using RimWorld;
using UnityEngine;
using System.Reflection;

namespace HaulExplicitly
{
    [HarmonyPatch(typeof(Thing), "GetGizmos")]
    class Thing_GetGizmos_Patch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Thing __instance)
        {
            foreach (Gizmo gizmo in gizmos)
                yield return gizmo;
            foreach (Gizmo gizmo in GizmoUtility.GetHaulExplicitlyGizmos(__instance))
                yield return gizmo;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), "GetGizmos")]
    class ThingWithComps_GetGizmos_Patch : Thing_GetGizmos_Patch
    {
    }

    [HarmonyPatch(typeof(Designator_Haul), "DesignateThing")]
    class Designator_Haul_DesignateThing_Patch
    {
        static bool Prefix(Thing t)
        {
            t.ToggleHaulDesignation();
            t.Map.listerMergeables.Notify_ThingStackChanged(t);
            return false;
        }
    }

    [HarmonyPatch(typeof(Designator_Haul), "CanDesignateThing")]
    class Designator_Haul_CanDesignateThing_Patch
    {
        static void Postfix(ref Verse.AcceptanceReport __result, Thing t)
        {
            __result = t.IsAHaulableSetToUnhaulable();
        }
    }

    [HarmonyPatch(typeof(Designator_Haul), Harmony.MethodType.Constructor)]
    class Designator_Haul_Constructor_Patch
    {
        static void Postfix(Designator_Haul __instance)
        {
            __instance.icon = ContentFinder<Texture2D>.Get("Buttons/Haulable", true);
            __instance.defaultLabel = "HaulExplicitly.SetHaulableLabel".Translate();
            __instance.defaultDesc = "HaulExplicitly.SetHaulableDesc".Translate();
        }
    }

    [HarmonyPatch(typeof(ReverseDesignatorDatabase), "InitDesignators")]
    class ReverseDesignatorDatabase_InitDesignators_Patch
    {
        static void Postfix(ReverseDesignatorDatabase __instance)
        {
            List<Designator> list =
                (List<Designator>)typeof(ReverseDesignatorDatabase).InvokeMember("desList",
                BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic,
                null, __instance, null);
            list.Add(new Designator_Unhaul());
        }
    }

    [HarmonyPatch(typeof(Designation), "DesignationDraw")]
    class Designation_DesignationDraw_Patch
    {
        static bool Prefix(Designation __instance)
        {
            if (__instance.def != DesignationDefOf.Haul)
                return true;
            Thing t = __instance.target.Thing;
            if (t != null && t.Spawned)
            {
                Vector3 pos = t.DrawPos;
                pos.x += t.RotatedSize.x * 0.3f;
                pos.y = AltitudeLayer.MetaOverlays.AltitudeFor() + 0.155f;
                string matpath;
                Mesh mesh;
                if (t.IsAHaulableSetToHaulable())
                {
                    matpath = "Overlay/Move";
                    pos.z += t.RotatedSize.z * 0.3f;
                    mesh = MeshPool.plane03;
                }
                else
                {
                    matpath = "Overlay/Anchor";
                    pos.z -= t.RotatedSize.z * 0.3f;
                    mesh = MeshPool.plane03;
                }
                Graphics.DrawMesh(mesh, pos, Quaternion.identity, MiscUtil.GetMoreMaterials(matpath), 0);
            }
            return false;
        }
    }
}
