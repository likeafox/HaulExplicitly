using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using StackFrame = System.Diagnostics.StackFrame;
#if HARMONY_1_2
using Harmony;
#elif HARMONY_2
using HarmonyLib;
#endif
using UnityEngine;
using Verse;

namespace HaulExplicitly
{
    public static class MiscUtil
    {
        public static StackFrame StackFrameWithMethod(string str, int steps = 2, bool debugPrintNames = false)
        {
            for (int i = 2; i < 2 + steps; i++)
            {
                StackFrame sf = new StackFrame(i, false);
                MethodBase caller = sf.GetMethod();
                if (caller == null) return null;
                string description = caller.FullDescription();
                if (debugPrintNames)
                    Log.Message((i - 1).ToString() + " " + description);
                if (description.Contains(str))
                    return sf;
            }
            return null;
        }

        private static Dictionary<string, Material> mats = new Dictionary<string, Material>();
        public static Material GetMoreMaterials(string path)
        {
            try
            {
                return mats[path];
            }
            catch { }
            return mats[path] = MaterialPool.MatFrom(path, ShaderDatabase.MetaOverlay);
        }

        private static Assembly _HarmonyAssembly = null;
        public static Assembly HarmonyAssembly {
            get
            {
                if (_HarmonyAssembly == null)
                {
                    _HarmonyAssembly = Assembly.Load("0Harmony");
                }
                return _HarmonyAssembly;
            }
        }

        public static HashSet<string> AllHarmonyPatchOwners()
        {
            var result = new HashSet<string>();
#if HARMONY_2
            foreach (MethodBase original in Harmony.GetAllPatchedMethods())
            {
                var info = Harmony.GetPatchInfo(original);
                foreach (string owner in info.Owners)
                {
                    result.Add(owner);
                }
            }
#elif HARMONY_1_2
            string harmony_namespace = "Harmony";
            var GetState = HarmonyAssembly.GetType(harmony_namespace + ".HarmonySharedState").GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Static);
            var state = (Dictionary<MethodBase, byte[]>)GetState.Invoke(null, new object[] { });
            foreach (byte[] infobytes in state.Values)
            {
                var Deserialize = HarmonyAssembly.GetType(harmony_namespace + ".PatchInfoSerialization").GetMethod(
                    "Deserialize", BindingFlags.Public | BindingFlags.Static
                    );
                var info = (PatchInfo)Deserialize.Invoke(null, new object[] { infobytes });
                var patches = new Patch[][] { info.prefixes, info.postfixes, info.transpilers }.SelectMany(x => x);
                foreach (Patch p in patches)
                {
                    result.Add(p.owner);
                }
            }
#endif
            return result;
        }
    }
}
