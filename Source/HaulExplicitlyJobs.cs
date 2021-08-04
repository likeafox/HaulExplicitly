using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;
using Mathf = UnityEngine.Mathf;

namespace HaulExplicitly
{
    public class WorkGiver_HaulExplicitly : WorkGiver_Scanner
    {
        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return HaulExplicitly.GetManager(pawn.Map).haulables;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !PotentialWorkThingsGlobal(pawn).Any();
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!CanGetThing(pawn, t, forced))
                return null;

            //plan count and dests
            HaulExplicitlyPosting posting = HaulExplicitly.GetManager(t.Map).PostingWithItem(t);
            if (posting == null)
                return null;
            int space_request = AmountPawnWantsToPickUp(pawn, t, posting);
            var destInfo = DeliverableDestinations.For(t, pawn, posting);
            List<IntVec3> dests = destInfo.RequestSpaceForItemAmount(space_request);
            int dest_space_available = destInfo.FreeSpaceInCells(dests);
            var count = Math.Min(space_request, dest_space_available);
            if (count < 1)
                return null;

            //make job
            JobDef JobDefOfHaulExplicitly =
                (JobDef)(GenDefDatabase.GetDef(typeof(JobDef), "HaulExplicitlyHaul"));
            Job job = new Job(JobDefOfHaulExplicitly, t, dests.First());
            //((JobDriver_HaulExplicitly)job.GetCachedDriver(pawn)).init();
            job.count = count;
            job.targetQueueA = new List<LocalTargetInfo>(
                new LocalTargetInfo[] { new IntVec3(posting.id, dest_space_available, 0) });
            job.targetQueueB = new List<LocalTargetInfo>(dests.Skip(1).Take(dests.Count - 1)
                .Select(c => new LocalTargetInfo(c)));
            job.haulOpportunisticDuplicates = true;
            return job;
        }

        public static bool CanGetThing(Pawn pawn, Thing t, bool forced)
        {
            //tests based on AI.HaulAIUtility.PawnCanAutomaticallyHaulFast
            UnfinishedThing unfinishedThing = t as UnfinishedThing;
            if ((unfinishedThing != null && unfinishedThing.BoundBill != null)
                || !pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger(),
                    mode: TraverseMode.ByPawn)
                || !pawn.CanReserve(t, 1, -1, null, forced))
                return false;
            if (t.IsBurning())
            {
                JobFailReason.Is("BurningLower".Translate(), null);
                return false;
            }
            return true;
        }

        public static int AmountPawnWantsToPickUp(Pawn p, Thing t, HaulExplicitlyPosting posting)
        {
            return Mathf.Min(new int[] {
                posting.RecordWithItem(t).RemainingToHaul(),
                p.carryTracker.AvailableStackSpace(t.def),
                t.stackCount });
        }
    }

    public class JobDriver_HaulExplicitly : JobDriver
    {
        private HaulExplicitlyInventoryRecord _record;
        public HaulExplicitlyInventoryRecord record
        {
            get
            {
                if (_record == null)
                    init();
                return _record;
            }
            private set { _record = value; }
        }
        private HaulExplicitlyPosting _posting;
        public HaulExplicitlyPosting posting
        {
            get
            {
                if (_posting == null)
                    init();
                return _posting;
            }
            private set { _posting = value; }
        }
        public int posting_id { get { return job.targetQueueA[0].Cell.x; } }
        public int dest_space_available
        {
            get
            {
                return job.targetQueueA[0].Cell.y;
            }
            set
            {
                var c = job.targetQueueA[0].Cell;
                job.targetQueueA[0] = new LocalTargetInfo(new IntVec3(c.x, value, c.z));
            }
        }

        public void init()
        {
            Thing targetItem = job.targetA.Thing;
            _posting = HaulExplicitly.GetManager(targetItem.MapHeld).postings[posting_id];
            _record = _posting.RecordWithItem(targetItem);
        }

        public override string GetReport()
        {
            return base.GetReport();
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            var targets = new List<LocalTargetInfo>();
            targets.Add(TargetA);
            targets.Add(TargetB);
            targets.AddRange(job.targetQueueB);
            return targets.All(t => pawn.Reserve(t, job, 1, -1, null, errorOnFailed));
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnBurningImmobile(TargetIndex.B);
            //this.FailOnForbidden(TargetIndex.A);

            Toil gotoThing = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            gotoThing.FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            gotoThing.FailOn(delegate (Toil toil)
            {
                Job job = toil.actor.CurJob;
                Thing thing = job.GetTarget(TargetIndex.A).Thing;
                IntVec3 cell = job.GetTarget(TargetIndex.B).Cell;
                List<Thing> items_in_cell = HaulExplicitlyPosting.GetItemsIfValidItemSpot(
                    toil.actor.Map, cell);
                if (items_in_cell == null)
                    return true;
                if (items_in_cell.Count == 0)
                    return false;
                if (items_in_cell.Count == 1 && thing.CanStackWith(items_in_cell.First()))
                    return false;
                return true;
            });
            yield return gotoThing;
            yield return Toils_HaulExplicitly.PickUpThing(TargetIndex.A, gotoThing);
            Toil carryToDest = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);
            yield return carryToDest;
            yield return Toils_HaulExplicitly.PlaceHauledThingAtDest(TargetIndex.B, carryToDest);
        }
    }

    public class Toils_HaulExplicitly
    {
        public static Toil PickUpThing(TargetIndex haulxItemInd, Toil nextToilIfBeingOpportunistic)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                Pawn actor = toil.actor;
                Job job = actor.CurJob;
                Thing target = job.GetTarget(haulxItemInd).Thing;
                if (Toils_Haul.ErrorCheckForCarry(actor, target))
                    return;

                Thing carriedItem = actor.carryTracker.CarriedThing;
                int targetInitialStackcount = target.stackCount;
                int countToPickUp = Mathf.Min(
                    job.count - (carriedItem?.stackCount ?? 0),
                    actor.carryTracker.AvailableStackSpace(target.def),
                    targetInitialStackcount);
                if (countToPickUp <= 0)
                    throw new Exception("PickUpThing countToPickUp = " + countToPickUp);

                //pick up
                int countPickedUp = actor.carryTracker.TryStartCarry(target, countToPickUp);
                if (countPickedUp < targetInitialStackcount)
                    actor.Map.reservationManager.Release(target, actor, job);
                carriedItem = actor.carryTracker.CarriedThing;
                job.SetTarget(haulxItemInd, carriedItem);
                actor.records.Increment(RecordDefOf.ThingsHauled);

                //register the carried item (into the HaulExplicitly job)
                if (carriedItem.IsAHaulableSetToHaulable())
                    carriedItem.ToggleHaulDesignation();
                var driver = (JobDriver_HaulExplicitly)actor.jobs.curDriver;
                driver.posting.TryAddItemSplinter(carriedItem);

                //pick up next available item in job?
                if (actor.CurJob.haulOpportunisticDuplicates)
                {
                    Thing prospect = null;
                    int best_dist = 999;
                    foreach (Thing item in driver.record.items.Where(
                        i => i.Spawned && WorkGiver_HaulExplicitly.CanGetThing(actor, i, false)))
                    {
                        IntVec3 offset = item.Position - actor.Position;
                        int dist = Math.Abs(offset.x) + Math.Abs(offset.z);
                        if (dist < best_dist && dist < 7)
                        {
                            prospect = item;
                            best_dist = dist;
                        }
                    }
                    if (prospect == null)
                        return;
                    int space_request = WorkGiver_HaulExplicitly
                        .AmountPawnWantsToPickUp(actor, prospect, driver.posting);
                    if (space_request == 0)
                        return;
                    var destInfo = DeliverableDestinations.For(prospect, actor, driver.posting);
                    List<IntVec3> dests = destInfo.RequestSpaceForItemAmount(
                        Math.Max(0, space_request - driver.dest_space_available));
                    int new_dest_space = destInfo.FreeSpaceInCells(dests);
                    var count = Math.Min(space_request, driver.dest_space_available + new_dest_space);
                    if (count < 1)
                        return;

                    //commit to it
                    actor.Reserve(prospect, job);
                    job.SetTarget(haulxItemInd, prospect);
                    job.SetTarget(TargetIndex.C, prospect.Position);
                    foreach (var dest in dests)
                    {
                        actor.Reserve(dest, job);
                        job.targetQueueB.Add(dest);
                    }
                    job.count += count;
                    driver.JumpToToil(nextToilIfBeingOpportunistic);
                }
            };
            return toil;
        }

        public static Toil PlaceHauledThingAtDest(TargetIndex destInd, Toil nextToilIfNotDonePlacing = null)
        {
            Toil toil = new Toil();
            toil.initAction = delegate
            {
                //get alllll the vars
                Pawn actor = toil.actor;
                Thing carriedItem = actor.carryTracker.CarriedThing;
                if (carriedItem == null)
                {
                    Log.Error(actor + " tried to place hauled thing in cell but is not hauling anything.");
                    return;
                }
                int carryBeforeCount = carriedItem.stackCount;
                Job job = actor.CurJob;
                var driver = (JobDriver_HaulExplicitly)actor.jobs.curDriver;
                driver.init();//this fixes problems
                Map map = driver.posting.map;
                IntVec3 dest = job.GetTarget(destInd).Cell;
                Thing floorItem = null;
                foreach (Thing t in dest.GetThingList(map))
                {
                    if (t.def.EverHaulable && t.CanStackWith(carriedItem))
                        floorItem = t;
                }

                //put it down now
                Thing placedThing; //gets set if done
                bool done = actor.carryTracker.TryDropCarriedThing(dest, ThingPlaceMode.Direct, out placedThing);

                if (done)
                {
                    job.count = 0;
                    driver.record.movedQuantity += carryBeforeCount;
                    driver.posting.TryRemoveItem(placedThing);
                }
                else
                {
                    var placedCount = carryBeforeCount - carriedItem.stackCount;
                    job.count -= placedCount;
                    driver.record.movedQuantity += placedCount;

                    var destQueue = job.GetTargetQueue(destInd);
                    if (nextToilIfNotDonePlacing != null && destQueue.Count != 0)
                    {
                        //put the remainder in the next queued cell
                        job.SetTarget(destInd, destQueue[0]);
                        destQueue.RemoveAt(0);
                        driver.JumpToToil(nextToilIfNotDonePlacing);
                    }
                    else
                    {
                        //can't continue the job normally
                        job.count = 0;
                        Job haulAsideJob = HaulAIUtility.HaulAsideJobFor(actor, carriedItem);
                        if (haulAsideJob != null)
                        {
                            actor.jobs.StartJob(haulAsideJob);
                        }
                        else
                        {
                            Log.Error("Incomplete explicit haul for " + actor
                                + ": Could not find anywhere to put "
                                + carriedItem + " near " + actor.Position
                                + ". Destroying. This should never happen!");
                            carriedItem.Destroy(DestroyMode.Vanish);
                            actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        }
                    }
                }
            };
            return toil;
        }
    }
}
