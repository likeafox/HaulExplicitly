using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;
using Vector3 = UnityEngine.Vector3;
using System.Reflection;
using Harmony;

namespace HaulExplicitly
{
    public class HaulExplicitlyInventoryRecord : IExposable
    {
        //data
        private System.WeakReference _parentPosting;
        public HaulExplicitlyPosting parentPosting
        {
            get
            {
                try { return (HaulExplicitlyPosting)_parentPosting.Target; } catch { }
                foreach (var mgr in HaulExplicitly.GetManagers())
                    foreach (var posting in mgr.postings.Values)
                        if (posting.inventory.Contains(this))
                            return (HaulExplicitlyPosting)(_parentPosting = new WeakReference(posting)).Target;
                throw new NullReferenceException("Orphaned HaulExplicitlyInventoryRecord");
            }
        }
        public List<Thing> items = new List<Thing>();
        private ThingDef _itemDef;
        public ThingDef itemDef { get { return _itemDef; } private set { _itemDef = value; } }
        private ThingDef _itemStuff;
        public ThingDef itemStuff { get { return _itemStuff; } private set { _itemStuff = value; } }
        private ThingDef _miniDef;
        public ThingDef miniDef { get { return _miniDef; } private set { _miniDef = value; } }
        private int _selectedQuantity;
        public int selectedQuantity { get { return _selectedQuantity; } private set { _selectedQuantity = value; } }
        private int _playerSetQuantity = -1;
        public int setQuantity
        {
            get { return (_playerSetQuantity == -1) ? selectedQuantity : _playerSetQuantity; }
            set
            {
                if (value < 0 || value > selectedQuantity)
                    throw new ArgumentOutOfRangeException();
                _playerSetQuantity = (int)value;
            }
        }
        public bool PlayerChangedQuantity
        {
            get { return _playerSetQuantity != -1; }
        }
        private int _mergeCapacity;
        public int mergeCapacity { get { return _mergeCapacity; } private set { _mergeCapacity = value; } }
        private int _numMergeStacksWillUse;
        public int numMergeStacksWillUse { get { return _numMergeStacksWillUse; } private set { _numMergeStacksWillUse = value; } }
        public int movedQuantity = 0;

        //
        public void ExposeData()
        {
            Scribe_Collections.Look(ref items, "items", LookMode.Reference);
            Scribe_Defs.Look(ref _itemDef, "itemDef");
            Scribe_Defs.Look(ref _itemStuff, "itemStuff");
            Scribe_Defs.Look(ref _miniDef, "minifiableDef");
            Scribe_Values.Look(ref _selectedQuantity, "selectedQuantity");
            Scribe_Values.Look(ref _playerSetQuantity, "setQuantity");
            Scribe_Values.Look(ref _mergeCapacity, "mergeCapacity");
            Scribe_Values.Look(ref _numMergeStacksWillUse, "numMergeStacksWillUse");
            Scribe_Values.Look(ref movedQuantity, "movedQuantity");
        }

        //methods
        public HaulExplicitlyInventoryRecord() { }
        public HaulExplicitlyInventoryRecord(Thing initial, HaulExplicitlyPosting parentPosting)
        {
            _parentPosting = new System.WeakReference(parentPosting);
            items.Add(initial);
            itemDef = initial.def;
            itemStuff = initial.Stuff;
            miniDef = (initial as MinifiedThing)?.InnerThing.def;
            selectedQuantity = initial.stackCount;
            ResetMerge();
        }

        public void ResetMerge()
        {
            mergeCapacity = 0;
            numMergeStacksWillUse = 0;
        }

        public void AddMergeCell(int itemQuantity)
        {
            numMergeStacksWillUse++;
            mergeCapacity += itemDef.stackLimit - itemQuantity;
        }

        public static int StacksWorth(ThingDef td, int quantity)
        {
            return (quantity / td.stackLimit) + ((quantity % td.stackLimit == 0) ? 0 : 1);
        }

        public int numStacksWillUse
        {
            get { return StacksWorth(itemDef, Math.Max(0, setQuantity - mergeCapacity)) + numMergeStacksWillUse; }
        }

        public bool CanMixWith(Thing t)
        {
            return (t.def.category == ThingCategory.Item
                && itemDef == t.def
                && itemStuff == t.Stuff
                && miniDef == (t as MinifiedThing)?.InnerThing.def);
        }

        public bool hasItem(Thing t)
        {
            return items.Contains(t);
        }

        public bool TryAddItem(Thing t, bool sideEffects = true)
        {
            if (!CanMixWith(t))
                return false;
            items.Add(t);
            if (sideEffects)
                selectedQuantity += t.stackCount;
            return true;
        }

        public bool TryRemoveItem(Thing t, bool playerCancelled = false)
        {
            bool r = items.Remove(t);
            if (r && playerCancelled)
            {
                selectedQuantity -= t.stackCount;
                _playerSetQuantity = Math.Min(_playerSetQuantity, selectedQuantity);
            }
            return r;
        }

        public int RemainingToHaul()
        {
            var pawns_list = new List<Pawn>(parentPosting.map.mapPawns.PawnsInFaction(Faction.OfPlayer));
            int beingHauledNow = 0;
            foreach (Pawn p in pawns_list)
            {
                try
                {
                    if (p.jobs.curJob.def.driverClass == typeof(JobDriver_HaulExplicitly)
                        && this == ((JobDriver_HaulExplicitly)p.jobs.curDriver).record)
                        beingHauledNow += p.jobs.curJob.count;
                }
                catch { }
            }
            return Math.Max(0, setQuantity - (movedQuantity + beingHauledNow));
        }

        public string Label
        {
            get
            {
                return GenLabel.ThingLabel(miniDef ?? itemDef, itemStuff, setQuantity).CapitalizeFirst();
            }
        }
    }

    [HarmonyPatch(typeof(CompressibilityDecider), "DetermineReferences")]
    class CompressibilityDecider_DetermineReferences_Patch
    {
        static void Postfix(CompressibilityDecider __instance)
        {
            HashSet<Thing> referencedThings =
                (HashSet<Thing>)typeof(CompressibilityDecider).InvokeMember("referencedThings",
                BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic,
                null, __instance, null);
            Map map = (Map)typeof(CompressibilityDecider).InvokeMember("map",
                BindingFlags.GetField | BindingFlags.Instance | BindingFlags.NonPublic,
                null, __instance, null);

            foreach (Thing t in HaulExplicitly.GetManager(map).haulables)
                referencedThings.Add(t);
        }
    }

    public enum HaulExplicitlyStatus : byte
    {
        Planning,  //hasn't been posted yet
        InProgress,
        DestinationBlocked, //one or more item types can't fit in their destinations right now
        Incompletable, //all possible hauls have been done (inventory exhausted), but the job is incomplete
        Complete,
        OverkillError //one of the records has had too much hauled
    }

    public class HaulExplicitlyPosting : IExposable
    {
        private int _id;
        private Map _map;
        public int id { get { return _id; } private set { _id = value; } }
        public Map map { get { return _map; } private set { _map = value; } }
        public List<HaulExplicitlyInventoryRecord> inventory = new List<HaulExplicitlyInventoryRecord>();
        public List<Thing> items = new List<Thing>();
        public List<IntVec3> destinations = null;

        public Vector3? cursor = null;
        public Vector3 center = new Vector3();
        public float visualization_radius = 0.0f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref _id, "postingId");
            Scribe_References.Look(ref _map, "map", true);
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Deep);
            Scribe_Collections.Look(ref destinations, "destinations", LookMode.Value);
            Scribe_Values.Look(ref cursor, "cursor");
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref visualization_radius, "visualizationRadius");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                ReloadItemsFromInventory();
            }
        }

        public HaulExplicitlyPosting() { }
        public HaulExplicitlyPosting(IEnumerable<object> objects)
        {
            id = HaulExplicitly.GetNewPostingID();
            map = Find.CurrentMap;
            foreach (object o in objects)
            {
                Thing t = o as Thing;
                if (t == null || !t.def.EverHaulable)
                    continue;

                items.Add(t);
                foreach (HaulExplicitlyInventoryRecord record in inventory)
                    if (record.TryAddItem(t))
                        goto match;
                inventory.Add(new HaulExplicitlyInventoryRecord(t, this));
            match: { }
            }
        }

        public bool TryRemoveItem(Thing t, bool playerCancelled = false)
        {
            if (!items.Contains(t))
                return false;
            HaulExplicitlyInventoryRecord owner_record = null;
            foreach (var record in inventory)
            {
                if (record.hasItem(t))
                {
                    owner_record = record;
                    break;
                }
            }
            if (owner_record == null || !owner_record.TryRemoveItem(t, playerCancelled))
            {
                Log.Error("Something went wronghbhnoetb9ugob9g3b49.");
                return false;
            }
            items.Remove(t);
            return true;
        }

        public bool TryAddItemSplinter(Thing t)
        {
            if (items.Contains(t))
                return false;
            var recordfinder = inventory.GetEnumerator();
            while (recordfinder.MoveNext())
                if (recordfinder.Current.CanMixWith(t))
                    goto found;
            Log.Error("TryAddItemSplinter failed to find matching record for " + t);
            return false;
        found:
            items.Add(t);
            recordfinder.Current.TryAddItem(t, false);
            return true;
        }

        public HaulExplicitlyInventoryRecord RecordWithItem(Thing t)
        {
            foreach (var record in inventory)
            {
                if (record.hasItem(t))
                    return record;
            }
            return null;
        }

        public void Clean()
        {
            var destroyed_items = new List<Thing>(items.Where(i => i.Destroyed));
            foreach (var i in destroyed_items)
                TryRemoveItem(i);
        }

        public void ReloadItemsFromInventory()
        {
            items = new List<Thing>();
            foreach (var r in inventory)
                foreach (Thing t in r.items)
                    items.Add(t);
        }

        private void inventoryResetMerge()
        {
            foreach (HaulExplicitlyInventoryRecord record in inventory)
                record.ResetMerge();
        }

        public HaulExplicitlyStatus Status()
        {
            throw new NotImplementedException();
        }

        private bool IsPossibleItemDestination(IntVec3 c)
        {
            if (!c.InBounds(this.map)
                || c.Fogged(this.map)
                || c.InNoZoneEdgeArea(this.map)
                || c.GetTerrain(this.map).passability == Traversability.Impassable
                    )
                return false;
            foreach (Thing t in this.map.thingGrid.ThingsAt(c))
            {
                if (!t.def.CanOverlapZones || t.def.passability == Traversability.Impassable || t.def.IsDoor)
                    return false;
            }
            return true;
        }

        private IEnumerable<IntVec3> PossibleItemDestinationsAtCursor(Vector3 cursor)
        {
            IntVec3 cursor_cell = new IntVec3(cursor);
            var cardinals = new IntVec3[] {
                IntVec3.North, IntVec3.South, IntVec3.East, IntVec3.West };
            HashSet<IntVec3> expended = new HashSet<IntVec3>();
            HashSet<IntVec3> available = new HashSet<IntVec3>();
            if (IsPossibleItemDestination(cursor_cell))
                available.Add(cursor_cell);
            while (available.Count > 0)
            {
                IntVec3 nearest = new IntVec3();
                float nearest_dist = 100000000.0f;
                foreach (IntVec3 c in available)
                {
                    float dist = (c.ToVector3Shifted() - cursor).magnitude;
                    if (dist < nearest_dist)
                    {
                        nearest = c;
                        nearest_dist = dist;
                    }
                }
                yield return nearest;
                available.Remove(nearest);
                expended.Add(nearest);

                foreach (IntVec3 dir in cardinals)
                {
                    IntVec3 c = nearest + dir;
                    if (!expended.Contains(c) && !available.Contains(c))
                    {
                        var set = IsPossibleItemDestination(c) ? available : expended;
                        set.Add(c);
                    }
                }
            }
        }

        public bool TryMakeDestinations(Vector3 cursor, bool try_be_lazy = true)
        {
            if (try_be_lazy && cursor == this.cursor)
                return destinations != null;
            this.cursor = cursor;
            int min_stacks = 0;
            foreach (HaulExplicitlyInventoryRecord record in inventory)
                min_stacks += record.numStacksWillUse;

            inventoryResetMerge();
            var dests = new List<IntVec3>();
            var prospects = PossibleItemDestinationsAtCursor(cursor).GetEnumerator();
            while (prospects.MoveNext())
            {
                IntVec3 cell = prospects.Current;
                List<Thing> items_in_cell = GetItemsIfValidItemSpot(map, cell);
                if (map.reservationManager.IsReservedByAnyoneOf(cell, Faction.OfPlayer)
                    || items_in_cell == null)
                    continue;

                if (items_in_cell.Count == 0)
                {
                    dests.Add(cell);
                }
                else
                {
                    Thing item = items_in_cell.First();
                    if (items_in_cell.Count != 1 || items.Contains(item))
                        continue;
                    //probably not necessary-- commented out for future reference:
                    //if (map.reservationManager.IsReservedByAnyoneOf(i, Faction.OfPlayer))
                    //    continue;

                    foreach (HaulExplicitlyInventoryRecord record in inventory)
                    {
                        if (record.CanMixWith(item) && item.stackCount != item.def.stackLimit)
                        {
                            dests.Add(cell);
                            record.AddMergeCell(item.stackCount);
                            break;
                        }
                    }
                }

                if (dests.Count >= min_stacks) //this check is just so it doesn't do the more expensive check every time
                {
                    int stacks = 0;
                    foreach (HaulExplicitlyInventoryRecord record in inventory)
                        stacks += record.numStacksWillUse;
                    if (dests.Count >= stacks)
                    {
                        //success operations
                        Vector3 sum = Vector3.zero;
                        foreach (IntVec3 dest in dests)
                            sum += dest.ToVector3Shifted();
                        center = (1.0f / (float)dests.Count) * sum;
                        visualization_radius = (float)Math.Sqrt(dests.Count / Math.PI);
                        destinations = dests;
                        return true;
                    }
                }
            }
            destinations = null;
            return false;
        }

        public static List<Thing> GetItemsIfValidItemSpot(Map map, IntVec3 cell)
        {
            //references used for this function (referenced during Rimworld 0.19):
            // Designation_ZoneAddStockpile.CanDesignateCell
            // StoreUtility.IsGoodStoreCell
            var result = new List<Thing>();
            if (!cell.InBounds(map)
                || cell.Fogged(map)
                || cell.InNoZoneEdgeArea(map)
                || cell.GetTerrain(map).passability == Traversability.Impassable
                || cell.ContainsStaticFire(map))
                return null;
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            foreach (Thing thing in things)
            {
                if (!thing.def.CanOverlapZones
                    || (thing.def.entityDefToBuild != null
                        && thing.def.entityDefToBuild.passability != Traversability.Standable)
                    || (thing.def.surfaceType == SurfaceType.None
                        && thing.def.passability != Traversability.Standable))
                    return null;
                if (thing.def.EverStorable(false))
                    result.Add(thing);
            }
            return result;
        }

        internal string stringy_details()
        {
            //this function will output a bunch of the inner variables into a string
            var s = new List<string>(new string[] { "HaulExplicitlyPosting #",
                id + "\n",
                "total inventory records: "+inventory.Count+"\n",
                "map = "+map.ToString()+"\n"
            });
            var inventory_all_items = new List<Thing>();
            var inventory_readout = new List<string>(new string[] { "Inventory readout:\n" });
            for (int i = 0; i < inventory.Count; i++)
            {
                inventory_all_items = new List<Thing>(inventory_all_items.Concat(inventory[i].items));
                inventory_readout.Add("  (inventory record " + i + "[" + inventory[i].itemDef.defName + "])\n      ");
                foreach (var item in inventory[i].items)
                    inventory_readout.Add(" (" + item + ")");
            }
            string coherent = "-";
            try
            {
                var a = new List<string>(items.Select(i => i.ThingID));
                var b = new List<string>(inventory_all_items.Select(i => i.ThingID));
                a.Sort();
                b.Sort();
                //Log.Message(string.Join(" ", a.ToArray()));
                //Log.Message(string.Join(" ", b.ToArray()));
                coherent = a.SequenceEqual(b).ToString();
                //*if (_same_length_hash(new List<string>(items.Select(i => i.ThingID)))
                //    .SetEquals(_same_length_hash(new List<string>(inventory_all_items.Select(i => i.ThingID)))))
                //    coherent = "true";
                //else
                //    coherent = "false";*/
            }
            catch { coherent = "false (exception)"; }
            s.Add("Coherent: " + coherent + "\n");
            //s.Add("items:\n");
            //foreach (var i in items)
            //    s.Add("   (" + i + ")");
            //s.Add("\n");
            s = new List<string>(s.Concat(inventory_readout));
            s.Add("\ndestinations:\n");
            foreach (var r in inventory)
                foreach (var dest in destinations)
                    s.Add("  (" + dest + ")");
            s.Add("\n");
            //s.Add("inventory:\n");
            //foreach (var r in inventory)
            //    s.Add("")
            return string.Join("", s.ToArray());
        }
    }

    public class HaulExplicitlyJobManager : IExposable
    {
        private Map _map;
        public Map map { get { return _map; } private set { _map = value; } }
        public Dictionary<int, HaulExplicitlyPosting> postings;

        public IEnumerable<Thing> haulables
        {
            get
            {
                foreach (HaulExplicitlyPosting posting in postings.Values)
                    foreach (Thing item in posting.items)
                        yield return item;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref _map, "map", true);
            Scribe_Collections.Look(ref postings, "postings", LookMode.Value, LookMode.Deep);
        }

        public void CleanGarbage()
        {
            var keys = new List<int>(postings.Keys);
            foreach (int k in keys)
            {
                postings[k].Clean();
                //var status = postings[k].Status();
                //if (status == HaulExplicitlyJobStatus.Complete
                //    || status == HaulExplicitlyJobStatus.Incompletable)
                //    postings.Remove(k);
            }
        }

        public HaulExplicitlyJobManager() { }
        public HaulExplicitlyJobManager(Map map)
        {
            this.map = map;
            postings = new Dictionary<int, HaulExplicitlyPosting>();
        }

        public HaulExplicitlyPosting PostingWithItem(Thing item)
        {
            foreach (var posting in postings.Values)
                if (posting.items.Contains(item))
                    return posting;
            return null;
        }
    }

    public class DeliverableDestinations
    {
        public List<IntVec3> partial_cells = new List<IntVec3>();
        public List<IntVec3> free_cells = new List<IntVec3>();
        private Func<IntVec3, float> grader;
        public HaulExplicitlyPosting posting { get; private set; }
        public HaulExplicitlyInventoryRecord record { get; private set; }
        private int dests_with_this_stack_type = 0;
        public List<int> partialCellSpaceAvailable = new List<int>();
        private Thing thing;

        private DeliverableDestinations(Thing item, Pawn carrier, HaulExplicitlyPosting posting, Func<IntVec3, float> grader)
        {
            this.grader = grader;
            this.posting = posting;
            record = posting.RecordWithItem(item);
            Map map = posting.map;
            thing = item;
            IntVec3 item_pos = (!item.SpawnedOrAnyParentSpawned) ? carrier.PositionHeld : item.PositionHeld;
            var traverseparms = TraverseParms.For(carrier, Danger.Deadly, TraverseMode.ByPawn, false);
            foreach (IntVec3 cell in posting.destinations)
            {
                List<Thing> items_in_cell = HaulExplicitlyPosting.GetItemsIfValidItemSpot(map, cell);
                bool valid_destination = items_in_cell != null;

                //see if this cell already has, or will have, an item of our item's stack type
                // (tests items in the cell, as well as reservations on the cell)
                bool cell_is_same_stack_type = false;
                if (valid_destination)
                    foreach (Thing i in items_in_cell)
                        if (record.CanMixWith(i))
                            cell_is_same_stack_type = true;
                Pawn claimant = map.reservationManager.FirstRespectedReserver(cell, carrier);
                if (claimant != null)
                {
                    List<Job> jobs = new List<Job>(claimant.jobs.jobQueue.Select(x => x.job));
                    jobs.Add(claimant.jobs.curJob);
                    foreach (Job job in jobs)
                    {
                        if (job.def.driverClass == typeof(JobDriver_HaulExplicitly)
                            && (job.targetB == cell || job.targetQueueB.Contains(cell))
                            && (record.CanMixWith(job.targetA.Thing)))
                        {
                            cell_is_same_stack_type = true;
                            break;
                        }
                    }
                }
                //finally, increment our counter of cells with our item's stack type
                if (cell_is_same_stack_type)
                    dests_with_this_stack_type++;

                //check if cell is valid, reachable from item, unreserved, and pawn is allowed to go there
                bool reachable = map.reachability.CanReach(item_pos, cell,
                    PathEndMode.ClosestTouch, traverseparms);
                if (!valid_destination || !reachable || claimant != null || cell.IsForbidden(carrier))
                    continue;

                // oh, just item things
                if (items_in_cell.Count == 0)
                    free_cells.Add(cell);
                try
                {
                    Thing item_in_cell = items_in_cell.Single();
                    int space_avail = item_in_cell.def.stackLimit - item_in_cell.stackCount;
                    if (cell_is_same_stack_type && space_avail > 0)
                    {
                        partial_cells.Add(cell);
                        partialCellSpaceAvailable.Add(space_avail);
                    }
                }
                catch { }
            }
        }

        public static DeliverableDestinations For(
            Thing item, Pawn carrier, HaulExplicitlyPosting posting = null, Func<IntVec3, float> grader = null)
        {
            if (posting == null) //do the handholdy version of this function
            {
                posting = HaulExplicitly.GetManager(item.Map).PostingWithItem(item);
                if (posting == null)
                    throw new ArgumentException();
            }
            return new DeliverableDestinations(item, carrier, posting, (grader != null) ? grader : DefaultGrader);
        }

        public static float DefaultGrader(IntVec3 c)
        {
            return 0.0f;
        }

        public List<IntVec3> UsableDests()
        {
            int free_cells_will_use = Math.Min(free_cells.Count,
                Math.Max(0, record.numStacksWillUse - dests_with_this_stack_type));
            List<IntVec3> result = new List<IntVec3>(partial_cells);
            result.AddRange(
                free_cells.OrderByDescending(grader)
                .Take(free_cells_will_use));
            return result;
        }

        public List<IntVec3> RequestSpaceForItemAmount(int amount)
        {
            List<IntVec3> usableDests = UsableDests();
            if (usableDests.Count == 0)
                return new List<IntVec3>();
            var destsOrdered = new List<IntVec3>(proximityOrdering(usableDests.RandomElement(), usableDests));
            int u; //number of dests to use
            int dest_space_available = 0;
            for (u = 0; u < destsOrdered.Count && dest_space_available < amount; u++)
            {
                int i = partial_cells.IndexOf(destsOrdered[u]);
                int space = (i == -1) ? thing.def.stackLimit : partialCellSpaceAvailable[i];
                dest_space_available += space;
            }
            return new List<IntVec3>(destsOrdered.Take(u));
        }

        public int FreeSpaceInCells(IEnumerable<IntVec3> cells)
        {
            int space = 0;
            foreach (IntVec3 c in cells)
            {
                if (!partial_cells.Contains(c) && !free_cells.Contains(c))
                    throw new ArgumentException("Specified cells don't exist in DeliverableDestinations.");
                Thing item;
                try
                {
                    item = posting.map.thingGrid.ThingsAt(c).Where(t => t.def.EverStorable(false)).First();
                    space += thing.def.stackLimit - item.stackCount;
                }
                catch
                {
                    space += thing.def.stackLimit;
                }
            }
            return space;
        }

        private static IEnumerable<IntVec3> proximityOrdering(IntVec3 center, IEnumerable<IntVec3> cells)
        {
            return cells.OrderBy(c => Math.Abs(center.x - c.x) + Math.Abs(center.y - c.y));
        }
    }
}
