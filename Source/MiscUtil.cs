using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using StackFrame = System.Diagnostics.StackFrame;
using Harmony;
using UnityEngine;
using Verse;

namespace HaulExplicitly
{
    public static class MiscUtil
    {
        public static StackFrame StackFrameWithMethod(string str, int steps = 2)
        {
            for (int i = 2; i < 2 + steps; i++)
            {
                StackFrame sf = new StackFrame(i, false);
                MethodBase caller = sf.GetMethod();
                if (caller == null) return null;
                if (caller.FullDescription().Contains(str))
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

        public static HashSet<string> AllHarmonyPatchOwners()
        {
            var result = new HashSet<string>();
            var GetState = typeof(HarmonySharedState).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Static);
            var state = (Dictionary<MethodBase, byte[]>)GetState.Invoke(null, new object[] { });
            foreach (byte[] infobytes in state.Values)
            {
                var info = PatchInfoSerialization.Deserialize(infobytes);
                var patches = new Patch[][] { info.prefixes, info.postfixes, info.transpilers }.SelectMany(x => x);
                foreach (Patch p in patches)
                {
                    result.Add(p.owner);
                }
            }
            return result;
        }
    }
}
