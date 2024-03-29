﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using StackFrame = System.Diagnostics.StackFrame;
#if HARMONY_1_2
using Harmony;
#elif HARMONY_2_0
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
#if HARMONY_1_2
            string harmony_namespace = "Harmony";
#elif HARMONY_2_0
            string harmony_namespace = "HarmonyLib";
#endif
            var GetState = HarmonyAssembly.GetType(harmony_namespace + ".HarmonySharedState").GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Static);
            var state = (Dictionary<MethodBase, byte[]>)GetState.Invoke(null, new object[] { });
            foreach (byte[] infobytes in state.Values)
            {
#if HARMONY_1_2
                var Deserialize_flags = BindingFlags.Public | BindingFlags.Static;
#elif HARMONY_2_0
                var Deserialize_flags = BindingFlags.NonPublic | BindingFlags.Static;
#endif
                var Deserialize = HarmonyAssembly.GetType(harmony_namespace + ".PatchInfoSerialization").GetMethod("Deserialize", Deserialize_flags);
                var info = (PatchInfo)Deserialize.Invoke(null, new object[] { infobytes });
#if HARMONY_1_2
                var patches = new Patch[][] { info.prefixes, info.postfixes, info.transpilers }.SelectMany(x => x);
#elif HARMONY_2_0
                var patches = new Patch[][] { info.prefixes, info.postfixes, info.transpilers, info.finalizers }.SelectMany(x => x);
#endif
                foreach (Patch p in patches)
                {
                    result.Add(p.owner);
                }
            }
            return result;
        }
    }
}
