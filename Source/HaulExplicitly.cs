using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;

namespace HaulExplicitly
{
    public class Mod : Verse.Mod
    {
        public Mod(ModContentPack content) : base(content)
        {
#if HARMONY_1_2
            var harmony = Harmony.HarmonyInstance.Create("likeafox.rimworld.haulexplicitly");
#elif HARMONY_2
            var harmony = new HarmonyLib.Harmony("likeafox.rimworld.haulexplicitly");
#endif
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public class HaulExplicitly : GameComponent
    {
        //data
        private Dictionary<int, HaulExplicitlyJobManager> managers = new Dictionary<int, HaulExplicitlyJobManager>();
        private HashSet<Zone_Stockpile> retainingZones = new HashSet<Zone_Stockpile>();

        //volatile data
        private static HaulExplicitly _instance;
        private static HaulExplicitly GetInstance()
        {
            if (_instance == null)
                throw new NullReferenceException("HaulExplicitly is not instantiated yet.");
            return _instance;
        }

        //interfaces
        public override void ExposeData()
        {
            base.ExposeData();

            //clean-up
            if (Scribe.mode == LoadSaveMode.Saving)
                CleanGarbage();

            //deal with save format version updates
            int savegameFormatVersion = 1; // format version of the currently executing code
            Scribe_Values.Look(ref savegameFormatVersion, "savegameFormatVersion", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                //do updating of document format here
            }

            //regular saving/loading
            Scribe_Collections.Look(ref managers, "managers",
                LookMode.Value, LookMode.Deep//, ref mapIdsScribe, ref managersScribe
                );
            Scribe_Collections.Look(ref retainingZones, "holdingZones", LookMode.Reference);

            //hopefully this will allow at least some limited recovery from this issue:
            // https://gist.github.com/HugsLibRecordKeeper/c04dca50bda4e311e81c9e4c9666ccc1
            if (managers == null)
            {
                managers = new Dictionary<int, HaulExplicitlyJobManager>();
                Log.Error("Haul Explicitly job managers were not loaded.  Were there errors the last time the game was saved?");
            }
        }

        public HaulExplicitly(Game game) : this() { }
        public HaulExplicitly() { _instance = this; }

        public static void CleanGarbage()
        {
            var self = GetInstance();
            var keys = new HashSet<int>(self.managers.Keys);
            keys.ExceptWith(Find.Maps.Select(m => m.uniqueID));
            foreach (var k in keys)
                self.managers.Remove(k);
            foreach (var mgr in self.managers.Values)
                mgr.CleanGarbage();
            var all_zones = new List<Zone_Stockpile>();
            foreach (Map map in Find.Maps)
                foreach (var zone in map.zoneManager.AllZones)
                    if (zone is Zone_Stockpile)
                        all_zones.Add(zone as Zone_Stockpile);
            self.retainingZones.IntersectWith(all_zones);
        }

        internal static int GetNewPostingID()
        {
            var self = GetInstance();
            int max = -1;
            foreach (var mgr in self.managers.Values)
                foreach (var posting in mgr.postings.Values)
                    if (posting.id > max)
                        max = posting.id;
            return max + 1;
        }

        public static HaulExplicitlyJobManager GetManager(Map map)
        {
            var self = GetInstance();
            HaulExplicitlyJobManager r = self.managers.TryGetValue(map.uniqueID);
            if (r != null)
                return r;
            var mgr = new HaulExplicitlyJobManager(map);
            self.managers[map.uniqueID] = mgr;
            return mgr;
        }

        public static HaulExplicitlyJobManager GetManager(int mapID)
        {
            foreach (Map map in Find.Maps)
                if (map.uniqueID == mapID)
                    return GetManager(map);
            Log.Error("HaulExplicitly.GetManager can't find map " + mapID);
            return null;
        }

        public static HaulExplicitlyJobManager GetManager(Thing t)
        {
#if RW_1_4_OR_GREATER
            if (t is Verse.Pawn)
            {
                return GetManager(t.Map);
            }
            else
            {
                return GetManager(t.MapHeld);
            }
#else
            return GetManager(t.Map);
#endif
        }

        public static List<HaulExplicitlyJobManager> GetManagers()
        {
            var self = GetInstance();
            return self.managers.Values.ToList();
        }

        public static void RegisterPosting(HaulExplicitlyPosting posting)
        {
            HaulExplicitlyJobManager manager = GetManager(posting.map);
            foreach (Thing i in posting.items)
            {
                {
                    ThingWithComps twc = i as ThingWithComps;
                    if (twc != null && twc.GetComp<CompForbiddable>() != null)
                        i.SetForbidden(false);
                }
                if (i.IsAHaulableSetToHaulable())
                    i.ToggleHaulDesignation();
                foreach (var p2 in manager.postings.Values)
                    p2.TryRemoveItem(i);
            }
            if (manager.postings.Keys.Contains(posting.id))
                throw new ArgumentException("Posting ID "+posting.id+" already exists in this manager.");
            manager.postings[posting.id] = posting;
        }

        public static HashSet<Zone_Stockpile> GetRetainingZones()
        {
            var self = GetInstance();
            return self.retainingZones;
        }
    }
}
