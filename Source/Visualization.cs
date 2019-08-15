using System;
using System.Collections.Generic;
using UnityEngine;
using Harmony;
using Verse;

namespace HaulExplicitly
{
    public static class HaulExplicitlyPostingVisualizationDrawer
    {
        private static List<int> postings_drawn_this_frame = new List<int>();

        private static float alt { get { return AltitudeLayer.MetaOverlays.AltitudeFor(); } }

        public static void DrawForItem(Thing item)
        {
            var mgr = HaulExplicitly.GetManager(item.Map);
            HaulExplicitlyPosting posting = mgr.PostingWithItem(item);
            if (posting == null)
                return;
            //draw line
            Vector3 start = item.Position.ToVector3ShiftedWithAltitude(alt);
            Vector3 circle_center = posting.center;
            circle_center.y = alt;
            Vector3 line_vector = circle_center - start;
            if (line_vector.magnitude > posting.visualization_radius)
            {
                line_vector = line_vector.normalized * (line_vector.magnitude - posting.visualization_radius);
                GenDraw.DrawLineBetween(start, start + line_vector);
            }

            if (postings_drawn_this_frame.Contains(posting.id))
                return;
            postings_drawn_this_frame.Add(posting.id);
            //draw circle
            GenDraw.DrawCircleOutline(circle_center, posting.visualization_radius);
        }

        public static void Clear()
        {
            postings_drawn_this_frame.Clear();
        }
    }

    [HarmonyPatch(typeof(Thing), "DrawExtraSelectionOverlays")]
    class Thing_DrawExtraSelectionOverlays_Patch
    {
        static void Postfix(Thing __instance)
        {
            if (__instance.def.EverHaulable)
                HaulExplicitlyPostingVisualizationDrawer.DrawForItem(__instance);
        }
    }

    [HarmonyPatch(typeof(RimWorld.SelectionDrawer), "DrawSelectionOverlays")]
    class SelectionDrawer_DrawSelectionOverlays_Patch
    {
        static void Postfix()
        {
            HaulExplicitlyPostingVisualizationDrawer.Clear();
        }
    }
}
