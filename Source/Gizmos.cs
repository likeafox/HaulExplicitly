using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;
using UnityEngine;

namespace HaulExplicitly
{
    public static class GizmoUtility
    {
#if !RW_1_4_OR_GREATER
        private static bool? _rwby_patched = null;
        private static bool _gave_warning = false;
#endif

        public static IEnumerable<Gizmo> GetHaulExplicitlyGizmos(Thing t)
        {
            if (t.def.EverHaulable)
            {
#if !RW_1_4_OR_GREATER
                if (t.Spawned)
                {
#endif
                    yield return new Designator_HaulExplicitly();
                    if (Command_Cancel_HaulExplicitly.RelevantToThing(t))
                        yield return new Command_Cancel_HaulExplicitly(t);
                    if (Command_SelectAllForHaulExplicitly.RelevantToThing(t))
                        yield return new Command_SelectAllForHaulExplicitly();
#if !RW_1_4_OR_GREATER
                }
                else
                {
                    bool rwby_patched;
                    try
                    {
                        rwby_patched = _rwby_patched.Value;
                    }
                    catch
                    {
                        _rwby_patched = rwby_patched =
                            MiscUtil.AllHarmonyPatchOwners().Contains("rimworld.carnysenpai.rwbyremnant");
                    }
                    if (!rwby_patched && !_gave_warning)
                    {
                        Log.Warning("GetGizmos was called on a despawned Thing, which is rather unusual.");
                        _gave_warning = true;
                    }
                }
#endif
            }
        }
    }

    public class Designator_Unhaul : Designator_Haul
    {
        public Designator_Unhaul()
        {
            this.defaultLabel = "HaulExplicitly.SetUnhaulableLabel".Translate();
            this.icon = ContentFinder<Texture2D>.Get("Buttons/Unhaulable", true);
            this.defaultDesc = "HaulExplicitly.SetUnhaulableDesc".Translate();
            this.soundDragSustain = SoundDefOf.Designate_DragStandard;
            this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            this.useMouseIcon = true;
            this.soundSucceeded = SoundDefOf.Designate_Haul;
            this.hotKey = null;
        }

        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            return t.IsAHaulableSetToHaulable();
        }
    }

    public class Designator_HaulExplicitly : Designator
    {
        private static HaulExplicitlyPosting prospective_job = null;

        public static void ResetJob()
        {
            Designator_HaulExplicitly.prospective_job = null;
        }

        public static void UpdateJob()
        {
            List<object> objects = Find.Selector.SelectedObjects;
            Designator_HaulExplicitly.prospective_job = new HaulExplicitlyPosting(objects);
        }

        public Designator_HaulExplicitly()
        {
            this.defaultLabel = "HaulExplicitly.HaulExplicitlyLabel".Translate();
            this.icon = ContentFinder<Texture2D>.Get("Buttons/HaulExplicitly", true);
            this.defaultDesc = "HaulExplicitly.HaulExplicitlyDesc".Translate();
            this.soundDragSustain = SoundDefOf.Designate_DragStandard;
            this.soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            this.useMouseIcon = true;
            this.soundSucceeded = SoundDefOf.Designate_Haul;
            this.hotKey = null;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            HaulExplicitlyPosting posting = Designator_HaulExplicitly.prospective_job;
            if (posting == null)
                return false;
            return posting.TryMakeDestinations(UI.MouseMapPosition());
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            HaulExplicitlyPosting posting = Designator_HaulExplicitly.prospective_job;
            posting.TryMakeDestinations(UI.MouseMapPosition(), false);
            HaulExplicitly.RegisterPosting(posting);
            ResetJob();
        }

        public override bool CanRemainSelected()
        {
            return Designator_HaulExplicitly.prospective_job != null;
        }

        public override void Selected()
        {
            Designator_HaulExplicitly.ResetJob();
            Designator_HaulExplicitly.UpdateJob();
        }

        public override void SelectedUpdate()
        {
            HaulExplicitlyPosting posting = Designator_HaulExplicitly.prospective_job;
            if (posting == null)
                return;
            if (posting.TryMakeDestinations(UI.MouseMapPosition()) && posting.destinations != null)
            {
                float alt = AltitudeLayer.MetaOverlays.AltitudeFor();
                foreach (IntVec3 d in posting.destinations)
                {
                    Vector3 drawPos = d.ToVector3ShiftedWithAltitude(alt);
                    Graphics.DrawMesh(MeshPool.plane10, drawPos, Quaternion.identity,
                        DesignatorUtility.DragHighlightThingMat, 0);
                }
            }
        }

        protected override void FinalizeDesignationFailed()
        {
            base.FinalizeDesignationFailed();
        }
        protected override void FinalizeDesignationSucceeded()
        {
            base.FinalizeDesignationSucceeded();
        }

        private Vector2 scrollPosition = Vector2.zero;
        private float gui_last_drawn_height = 0;
        public override void DoExtraGuiControls(float leftX, float bottomY)
        {
            HaulExplicitlyPosting posting = Designator_HaulExplicitly.prospective_job;
            var records = new List<HaulExplicitlyInventoryRecord>(
                posting.inventory.OrderBy(r => r.Label));
            const float max_height = 450f;
            const float width = 268f;
            const float row_height = 28f;
            float height = Math.Min(gui_last_drawn_height + 20f, max_height);
            Rect winRect = new Rect(leftX, bottomY - height, width, height);
            Rect outerRect = new Rect(0f, 0f, width, height).ContractedBy(10f);
            Rect innerRect = new Rect(0f, 0f, outerRect.width - 16f, Math.Max(gui_last_drawn_height, outerRect.height));
            Find.WindowStack.ImmediateWindow(622372, winRect, WindowLayer.GameUI, delegate
            {
                Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);
                GUI.BeginGroup(innerRect);
                GUI.color = ITab_Pawn_Gear.ThingLabelColor;
                GameFont prev_font = Text.Font;
                Text.Font = GameFont.Small;
                float y = 0f;
                Widgets.ListSeparator(ref y, innerRect.width, "Items to haul");
                foreach (var rec in records)
                {
                    Rect rowRect = new Rect(0f, y, innerRect.width - 24f, 28f);
                    if (rec.selectedQuantity > 1)
                    {
                        Rect buttonRect = new Rect(rowRect.x + rowRect.width,
                            rowRect.y + (rowRect.height - 24f) / 2, 24f, 24f);
                        if (Widgets.ButtonImage(buttonRect,
                            RimWorld.Planet.CaravanThingsTabUtility.AbandonSpecificCountButtonTex))
                        {
                            string txt = "HaulExplicitly.ItemHaulSetQuantity".Translate(new NamedArgument((rec.itemDef.label).CapitalizeFirst(), "ITEMTYPE"));
                            var dialog = new Dialog_Slider(txt, 1, rec.selectedQuantity, delegate (int x)
                            {
                                rec.setQuantity = x;
                            }, rec.setQuantity);
                            dialog.layer = WindowLayer.GameUI;
                            Find.WindowStack.Add(dialog);
                        }
                    }

                    if (Mouse.IsOver(rowRect))
                    {
                        GUI.color = ITab_Pawn_Gear.HighlightColor;
                        GUI.DrawTexture(rowRect, TexUI.HighlightTex);
                    }

                    if (rec.itemDef.DrawMatSingle?.mainTexture != null)
                    {
                        Rect iconRect = new Rect(4f, y, 28f, 28f);
                        if (rec.miniDef != null || rec.selectedQuantity == 1)
                            Widgets.ThingIcon(iconRect, rec.items[0]);
                        else
                            Widgets.ThingIcon(iconRect, rec.itemDef);
                    }

                    Text.Anchor = TextAnchor.MiddleLeft;
                    Text.WordWrap = false;
                    Rect textRect = new Rect(36f, y, rowRect.width - 36f, rowRect.height);
                    string str = rec.Label;
                    Widgets.Label(textRect, str.Truncate(textRect.width));

                    y += row_height;
                }
                gui_last_drawn_height = y;
                Text.Font = prev_font;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                GUI.EndGroup();
                Widgets.EndScrollView();
            }, true, false, 1f);
        }
    }

    public class Command_Cancel_HaulExplicitly : Command
    {
        private Thing thing;

        public Command_Cancel_HaulExplicitly(Thing t)
        {
            thing = t;
            this.defaultLabel = "HaulExplicitly.CancelHaulExplicitlyLabel".Translate();
            this.icon = ContentFinder<Texture2D>.Get("Buttons/DontHaulExplicitly", true);
            this.defaultDesc = "HaulExplicitly.CancelHaulExplicitlyDesc".Translate();
            this.hotKey = null;
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            HaulExplicitlyPosting posting = HaulExplicitly.GetManager(Find.CurrentMap).PostingWithItem(thing);
            if (posting != null)
            {
                posting.TryRemoveItem(thing, true);
                foreach (Pawn p in Find.CurrentMap.mapPawns.PawnsInFaction(Faction.OfPlayer).ListFullCopy())
                {
                    var jobs = new List<Job>(p.jobs.jobQueue.AsEnumerable().Select(j => j.job));
                    if (p.CurJob != null) jobs.Add(p.CurJob);
                    foreach (var job in jobs)
                    {
                        if (job.def.driverClass == typeof(JobDriver_HaulExplicitly)
                            && job.targetA.Thing == thing)
                            p.jobs.EndCurrentOrQueuedJob(job, JobCondition.Incompletable);
                    }
                }
            }
        }

        public static bool RelevantToThing(Thing t)
        {
            return HaulExplicitly.GetManager(t).PostingWithItem(t) != null;
        }
    }

    public class Command_SelectAllForHaulExplicitly : Command
    {
        public Command_SelectAllForHaulExplicitly()
        {
            this.defaultLabel = "HaulExplicitly.SelectAllHaulExplicitlyLabel".Translate();
            this.icon = ContentFinder<Texture2D>.Get("Buttons/SelectHaulExplicitlyJob", true);
            this.defaultDesc = "HaulExplicitly.SelectAllHaulExplicitlyDesc".Translate();
            this.hotKey = null;
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);
            Selector selector = Find.Selector;
            List<object> selection = selector.SelectedObjects;
            Thing example = (Thing)selection.First();
            HaulExplicitlyPosting posting = HaulExplicitly.GetManager(example).PostingWithItem(example);
            foreach (object o in posting.items)
            {
                Thing t = o as Thing;
                if (!selection.Contains(o) && t != null &&
#if RW_1_4_OR_GREATER
                    t.SpawnedOrAnyParentSpawned
#else
                    t.Spawned
#endif
                    )
                    selector.Select(o);
            }
        }

        public static bool RelevantToThing(Thing t)
        {
            var mgr = HaulExplicitly.GetManager(t);
            HaulExplicitlyPosting posting = mgr.PostingWithItem(t);
            if (posting == null)
                return false;
            foreach (object o in Find.Selector.SelectedObjects)
            {
                Thing other = o as Thing;
                if (other == null || !posting.items.Contains(other))
                    return false;
            }
            return Find.Selector.SelectedObjects.Count < posting.items.Count(i =>
#if RW_1_4_OR_GREATER
                i.SpawnedOrAnyParentSpawned
#else
                i.Spawned
#endif
                );
        }
    }
}
