using System;
using System.Collections.Generic;
using UnityEngine;
#if HARMONY_1_2
using Harmony;
#elif HARMONY_2
using HarmonyLib;
#endif
using Verse;

namespace HaulExplicitly
{
    public class HaulExplicitlyPostingVisualizationDrawer
    {
        private static List<int> postings_drawn_this_frame = new List<int>();

        private static float alt { get { return AltitudeLayer.MetaOverlays.AltitudeFor(); } }

        public static void DrawForItem(Thing item)
        {
#if RW_1_4_OR_GREATER
            // for now, I don't want to draw lines for selected carried items. May change in the future.
            // Also note that this makes the later .PositionHeld/.Position distinction irrelevant
            if (!item.Spawned)
                return;
#endif
            var mgr = HaulExplicitly.GetManager(item);
            HaulExplicitlyPosting posting = mgr.PostingWithItem(item);
            if (posting == null)
                return;
            //draw line
            Vector3 start = item
#if RW_1_4_OR_GREATER
                .PositionHeld
#else
                .Position
#endif
                .ToVector3ShiftedWithAltitude(alt);
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
