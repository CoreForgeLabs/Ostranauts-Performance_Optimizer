// SaveForce v1.10.0 — Save loading optimizer for Ostranauts
// v1.10.0: + inter-batch GC, AddCondAmount profiling
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LitJson;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Events;
using Ostranauts.Tools;
using Ostranauts.UI.Loading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace SaveForce
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class SaveForcePlugin : BaseUnityPlugin
    {
        public const string GUID = "com.coreforgelabs.saveforce";
        public const string NAME = "SaveForce";
        public const string VERSION = "1.11.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgParallelShips;
        internal static ConfigEntry<bool> CfgReduceYields;
        internal static ConfigEntry<int> CfgYieldBatchSize;
        internal static ConfigEntry<bool> CfgConditionCache;

        private static bool s_profileReported = false;
        internal static bool s_isLoading = false;

        // Profiling
        internal static Stopwatch sw_Total = new Stopwatch();
        internal static Stopwatch sw_ZipExtract = new Stopwatch();
        internal static Stopwatch sw_MainJson = new Stopwatch();
        internal static Stopwatch sw_SystemInit = new Stopwatch();

        // Parallel ship parsing
        internal static Dictionary<string, JsonShip> s_parsedShipsCache;
        internal static Dictionary<string, byte[]> s_originalShipBytes;
        internal static Dictionary<string, byte[]> s_dictFilesRef;

        // GetCondOwner profiling
        internal static Stopwatch sw_GetCondOwner = new Stopwatch();
        internal static int s_getCondOwnerCalls = 0;
        internal static Stopwatch sw_SpawnItems = new Stopwatch();
        internal static int s_spawnItemsCalls = 0;
        internal static Stopwatch sw_GetMesh = new Stopwatch();
        internal static int s_getMeshCalls = 0;
        internal static Stopwatch sw_SetData = new Stopwatch();
        internal static int s_setDataCalls = 0;
        internal static Stopwatch sw_Awake = new Stopwatch();
        internal static int s_awakeCalls = 0;
        internal static Stopwatch sw_AddCond = new Stopwatch();
        internal static long s_addCondCalls = 0;
        internal static Stopwatch sw_ItemSetData = new Stopwatch();
        internal static int s_itemSetDataCalls = 0;
        internal static long s_visualizeOverlaysSkipped = 0;

        private void Awake()
        {
            Log = Logger;

            CfgParallelShips = Config.Bind("Optimizations", "ParallelShipParsing", true,
                "Parse ship JSONs on background threads");
            CfgReduceYields = Config.Bind("Optimizations", "ReduceYields", true,
                "Batch yield returns in StarSystem.Init");
            CfgYieldBatchSize = Config.Bind("Optimizations", "YieldBatchSize", 10,
                "Items per yield batch");
            CfgConditionCache = Config.Bind("Optimizations", "ConditionTemplateCache", true,
                "Cache Condition objects via MemberwiseClone instead of new Condition(JsonCond)");


            // Discover method signatures for profiling
            try
            {
                // DoLoadGame
                var m = AccessTools.Method(typeof(CrewSim), "DoLoadGame");
                if (m != null)
                {
                    var parms = m.GetParameters();
                    for (int i = 0; i < parms.Length; i++)
                        Log.LogInfo("  DoLoadGame[" + i + "]: " + parms[i].ParameterType.Name + " " + parms[i].Name);
                }

                // GetCondOwner overloads
                var gcoMethods = typeof(DataHandler).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(mi => mi.Name == "GetCondOwner").ToArray();
                Log.LogInfo("  GetCondOwner: " + gcoMethods.Length + " overload(s)");
                for (int i = 0; i < gcoMethods.Length; i++)
                {
                    var ps = gcoMethods[i].GetParameters();
                    string sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    Log.LogInfo("    [" + i + "] (" + sig + ") → " + gcoMethods[i].ReturnType.Name);
                }

                // SpawnItems overloads
                var siMethods = typeof(Ship).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(mi => mi.Name == "SpawnItems").ToArray();
                Log.LogInfo("  SpawnItems: " + siMethods.Length + " overload(s)");
                for (int i = 0; i < siMethods.Length; i++)
                {
                    var ps = siMethods[i].GetParameters();
                    string sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    Log.LogInfo("    [" + i + "] (" + sig + ")");
                }

                // CreatePart
                var cpMethods = typeof(Ship).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(mi => mi.Name == "CreatePart").ToArray();
                Log.LogInfo("  CreatePart: " + cpMethods.Length + " overload(s)");
                for (int i = 0; i < cpMethods.Length; i++)
                {
                    var ps = cpMethods[i].GetParameters();
                    string sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    Log.LogInfo("    [" + i + "] (" + sig + ")");
                }

                // InitShip  
                var isMethods = typeof(Ship).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(mi => mi.Name == "InitShip").ToArray();
                Log.LogInfo("  InitShip: " + isMethods.Length + " overload(s)");
                for (int i = 0; i < isMethods.Length; i++)
                {
                    var ps = isMethods[i].GetParameters();
                    string sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    Log.LogInfo("    [" + i + "] (" + sig + ")");
                }

                // GetMesh
                var gmMethods = typeof(DataHandler).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(mi => mi.Name == "GetMesh").ToArray();
                Log.LogInfo("  GetMesh: " + gmMethods.Length + " overload(s)");
                for (int i = 0; i < gmMethods.Length; i++)
                {
                    var ps = gmMethods[i].GetParameters();
                    string sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
                    Log.LogInfo("    [" + i + "] (" + sig + ") → " + gmMethods[i].ReturnType.Name);
                }
            }
            catch (Exception ex) { Log.LogInfo("Reflection: " + ex.Message); }

            // Pre-register JsonShip with LitJson
            try
            {
                JsonMapper.ToObject<JsonShip[]>("[]");
                Log.LogInfo("  LitJson JsonShip OK");
            }
            catch (Exception ex) { Log.LogWarning("LitJson: " + ex.Message); }


            Harmony harmony = new Harmony(GUID);
            int ok = 0;
            List<Type> patches = new List<Type>
            {
                typeof(Patch_ZipExtractProfile),
                typeof(Patch_LoadSaveFileProfile),
                typeof(Patch_LoadingScreenProfile),
                typeof(Patch_DoLoadGame_ParallelParse),
                typeof(Patch_SystemInit),
                typeof(Patch_GetCondOwner_Profile),
                typeof(Patch_GetMesh_Profile),
                typeof(Patch_UpdateFaces_SkipDuringLoad),
                typeof(Patch_LootParseCache),
                typeof(Patch_SetData_Profile),
                typeof(Patch_Awake_Profile),
                typeof(Patch_CondRuleCache),
                typeof(Patch_AddCondAmount_Profile),
                typeof(Patch_VisualizeOverlays_SkipDuringLoad),
                typeof(Patch_ItemSetData_Profile),
            };
            if (CfgConditionCache.Value)
                patches.Add(typeof(Patch_GetCond_TemplateCache));
            for (int i = 0; i < patches.Count; i++)
            {
                try
                {
                    harmony.CreateClassProcessor(patches[i]).Patch();
                    ok++;
                    Log.LogInfo("  [OK] " + patches[i].Name);
                }
                catch (Exception ex)
                {
                    Log.LogWarning("  [FAIL] " + patches[i].Name + ": " + ex.Message);
                }
            }

            Log.LogInfo("=== SaveForce v" + VERSION + " (" + ok + "/" + patches.Count + " patches) ===");
            Log.LogInfo("  ParallelShips=" + CfgParallelShips.Value +
                " ParallelShips=" + CfgParallelShips.Value +
                " CondCache=" + CfgConditionCache.Value +
                " ReduceYields=" + CfgReduceYields.Value +
                " BatchSize=" + CfgYieldBatchSize.Value);
        }


        private void Update()
        {
            if (!s_profileReported && sw_Total.IsRunning)
            {
                try
                {
                    if (CrewSim.objInstance != null)
                    {
                        FieldInfo fi = AccessTools.Field(typeof(CrewSim), "_finishedLoading");
                        if (fi != null && (bool)fi.GetValue(CrewSim.objInstance))
                        {
                            sw_Total.Stop();
                            PrintProfile();
                            s_profileReported = true;
                        }
                    }
                }
                catch { }
            }
        }

        internal static void PrintProfile()
        {
            s_isLoading = false;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== SaveForce LOAD PROFILE ===");
            sb.AppendLine("  TOTAL:         " + sw_Total.ElapsedMilliseconds + " ms");
            sb.AppendLine("  ZIP Extract:   " + sw_ZipExtract.ElapsedMilliseconds + " ms");
            sb.AppendLine("  Main JSON:     " + sw_MainJson.ElapsedMilliseconds + " ms");
            sb.AppendLine("  System Init:   " + sw_SystemInit.ElapsedMilliseconds + " ms");
            sb.AppendLine("  GetCondOwner:  " + sw_GetCondOwner.ElapsedMilliseconds + " ms (" + s_getCondOwnerCalls + " calls)");
            sb.AppendLine("  GetMesh:       " + sw_GetMesh.ElapsedMilliseconds + " ms (" + s_getMeshCalls + " calls)");
            if (s_getMeshCalls > 0)
                sb.AppendLine("  GetMesh avg:   " + (sw_GetMesh.ElapsedMilliseconds * 1000 / s_getMeshCalls) + " us/call");
            sb.AppendLine("  [non-mesh]:    " + (sw_GetCondOwner.ElapsedMilliseconds - sw_GetMesh.ElapsedMilliseconds) + " ms (CO without mesh)");
            if (s_getCondOwnerCalls > 0)
                sb.AppendLine("  GetCO avg:     " + (sw_GetCondOwner.ElapsedMilliseconds * 1000 / s_getCondOwnerCalls) + " us/call");
            sb.AppendLine("  SetData:       " + sw_SetData.ElapsedMilliseconds + " ms (" + s_setDataCalls + " calls)");
            sb.AppendLine("  Awake:         " + sw_Awake.ElapsedMilliseconds + " ms (" + s_awakeCalls + " calls)");
            sb.AppendLine("  AddCondAmt:    " + sw_AddCond.ElapsedMilliseconds + " ms (" + s_addCondCalls + " calls)");
            sb.AppendLine("  ItemSetData:   " + sw_ItemSetData.ElapsedMilliseconds + " ms (" + s_itemSetDataCalls + " calls)");
            sb.AppendLine("  VisOverlays:   " + s_visualizeOverlaysSkipped + " skipped during load");
            sb.AppendLine("  --- Condition Optimizations ---");
            try
            {
                sb.AppendLine("  CondCache:     " + Patch_GetCond_TemplateCache.s_hits + " hits / " +
                    Patch_GetCond_TemplateCache.s_misses + " misses (" +
                    Patch_GetCond_TemplateCache.s_templateCount + " templates)");
            }
            catch { sb.AppendLine("  CondCache:     disabled"); }
            try
            {
                sb.AppendLine("  ParseCache:    " + Patch_LootParseCache.s_hits + " hits / " +
                    Patch_LootParseCache.s_misses + " misses");
            }
            catch { sb.AppendLine("  ParseCache:    disabled"); }
            try
            {
                sb.AppendLine("  UpdateFaces:   " + Patch_UpdateFaces_SkipDuringLoad.s_skipped + " skipped during load");
            }
            catch { sb.AppendLine("  UpdateFaces:   n/a"); }
            try
            {
                sb.AppendLine("  CondRuleCache: " + Patch_CondRuleCache.s_hits + " hits / " +
                    Patch_CondRuleCache.s_misses + " misses");
            }
            catch { sb.AppendLine("  CondRuleCache: disabled"); }
            sb.AppendLine("==============================");
            Log.LogInfo(sb.ToString());
        }

        internal static void ResetProfile()
        {
            sw_Total.Reset();
            sw_ZipExtract.Reset();
            sw_MainJson.Reset();
            sw_SystemInit.Reset();
            sw_GetCondOwner.Reset();
            s_getCondOwnerCalls = 0;
            sw_SpawnItems.Reset();
            s_spawnItemsCalls = 0;
            sw_GetMesh.Reset();
            s_getMeshCalls = 0;
            sw_SetData.Reset();
            s_setDataCalls = 0;
            sw_Awake.Reset();
            s_awakeCalls = 0;
            sw_AddCond.Reset();
            s_addCondCalls = 0;
            sw_ItemSetData.Reset();
            s_itemSetDataCalls = 0;
            s_visualizeOverlaysSkipped = 0;
            s_profileReported = false;
            s_parsedShipsCache = null;
            s_originalShipBytes = null;
            s_dictFilesRef = null;
        }
    }

    // ============== Batched coroutine enumerator ==============

    public class BatchedEnumerator : IEnumerator
    {
        private readonly IEnumerator _inner;
        private readonly int _batchSize;
        private int _totalYields;
        private int _skipped;

        public BatchedEnumerator(IEnumerator inner, int batchSize)
        {
            _inner = inner;
            _batchSize = batchSize;
        }

        public object Current { get { return _inner.Current; } }

        public bool MoveNext()
        {
            int processed = 0;
            while (true)
            {
                bool hasMore = _inner.MoveNext();
                if (!hasMore)
                {
                    SaveForcePlugin.Log.LogInfo("[OPT] BatchYields: " + _totalYields +
                        " total, " + _skipped + " skipped (batch=" + _batchSize + ")");
                    return false;
                }

                _totalYields++;
                processed++;

                if (_inner.Current == null && processed < _batchSize)
                {
                    _skipped++;
                    continue;
                }

                // v1.10.0: GC between batches during loading
                if (SaveForcePlugin.s_isLoading && _totalYields % 50 == 0)
                    GC.Collect();

                return true;
            }
        }

        public void Reset() { _inner.Reset(); }
    }

    // ===================== HARMONY PATCHES =====================

    [HarmonyPatch(typeof(DotNetZipCompressor), "ExtractArchive", new Type[] { typeof(string) })]
    public static class Patch_ZipExtractProfile
    {
        static void Prefix()
        {
            SaveForcePlugin.ResetProfile();
            SaveForcePlugin.s_isLoading = true;
            SaveForcePlugin.sw_Total.Start();
            SaveForcePlugin.sw_ZipExtract.Start();
            SaveForcePlugin.Log.LogInfo("[PROFILE] ZIP extraction started...");
        }

        static void Postfix(Dictionary<string, byte[]> __result)
        {
            SaveForcePlugin.sw_ZipExtract.Stop();
            int count = __result != null ? __result.Count : 0;
            long totalBytes = 0;
            if (__result != null)
                foreach (var kv in __result) totalBytes += kv.Value.Length;
            SaveForcePlugin.Log.LogInfo("[PROFILE] ZIP extracted: " + count + " files, " +
                (totalBytes / 1048576) + " MB in " + SaveForcePlugin.sw_ZipExtract.ElapsedMilliseconds + " ms");
        }
    }

    [HarmonyPatch(typeof(DataHandler), "LoadSaveFile",
        new Type[] { typeof(string), typeof(Dictionary<string, byte[]>) })]
    public static class Patch_LoadSaveFileProfile
    {
        static void Prefix()
        {
            try { Patch_GetCond_TemplateCache.ClearCache(); } catch { }
            // v1.8.0: Clear parse cache
            try { Patch_LootParseCache.ClearCache(); } catch { }
            try { Patch_CondRuleCache.ClearCache(); } catch { }
            SaveForcePlugin.sw_MainJson.Start();
        }
        static void Postfix()
        {
            SaveForcePlugin.sw_MainJson.Stop();
            SaveForcePlugin.Log.LogInfo("[PROFILE] Main JSON parsed in " +
                SaveForcePlugin.sw_MainJson.ElapsedMilliseconds + " ms");
        }
    }

    [HarmonyPatch(typeof(Ostranauts.LoadingScreen), "SetProgressBar")]
    public static class Patch_LoadingScreenProfile
    {
        private static string s_lastStage = "";
        private static Stopwatch s_sw = new Stopwatch();

        static void Prefix(float totalAmount, string textToDisplay)
        {
            if (string.IsNullOrEmpty(textToDisplay)) return;
            if (!SaveForcePlugin.sw_Total.IsRunning) return;

            if (s_sw.IsRunning && !string.IsNullOrEmpty(s_lastStage))
            {
                s_sw.Stop();
                SaveForcePlugin.Log.LogInfo("[PROFILE] Stage '" + s_lastStage +
                    "' took " + s_sw.ElapsedMilliseconds + " ms (progress=" +
                    (totalAmount * 100f).ToString("F0") + "%)");
            }

            s_lastStage = textToDisplay;
            s_sw.Reset();
            s_sw.Start();
        }
    }

    [HarmonyPatch(typeof(CrewSim), "DoLoadGame")]
    public static class Patch_DoLoadGame_ParallelParse
    {
        static void Prefix(object[] __args)
        {
            SaveForcePlugin.Log.LogInfo("[PROFILE] DoLoadGame started");

            if (!SaveForcePlugin.CfgParallelShips.Value) return;

            Dictionary<string, byte[]> dictFiles = null;
            if (__args != null)
                for (int i = 0; i < __args.Length; i++)
                {
                    dictFiles = __args[i] as Dictionary<string, byte[]>;
                    if (dictFiles != null)
                    {
                        SaveForcePlugin.Log.LogInfo("[OPT] dictFiles at arg[" + i + "], " + dictFiles.Count + " entries");
                        break;
                    }
                }

            if (dictFiles == null) { SaveForcePlugin.Log.LogWarning("[OPT] dictFiles NOT found!"); return; }

            List<string> shipKeys = new List<string>();
            foreach (string key in dictFiles.Keys)
                if (key.Contains("ships/") && !key.Contains(".png"))
                    shipKeys.Add(key);

            if (shipKeys.Count == 0) return;

            SaveForcePlugin.Log.LogInfo("[OPT] Parallel parsing " + shipKeys.Count + " ship files...");

            var originalBytes = new Dictionary<string, byte[]>(shipKeys.Count);
            byte[] emptyJson = Encoding.UTF8.GetBytes("[]");
            for (int i = 0; i < shipKeys.Count; i++)
            {
                originalBytes[shipKeys[i]] = dictFiles[shipKeys[i]];
                dictFiles[shipKeys[i]] = emptyJson;
            }

            var parsedShips = new Dictionary<string, JsonShip>();
            object lockObj = new object();
            int remaining = shipKeys.Count;
            int errors = 0;
            var doneEvent = new ManualResetEvent(false);
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < shipKeys.Count; i++)
            {
                string thisKey = shipKeys[i];
                byte[] thisBytes = originalBytes[thisKey];
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(thisBytes);
                        JsonShip[] ships = JsonMapper.ToObject<JsonShip[]>(json);
                        if (ships != null && ships.Length > 0)
                        {
                            lock (lockObj)
                            {
                                for (int j = 0; j < ships.Length; j++)
                                    if (ships[j] != null && ships[j].strName != null)
                                        parsedShips[ships[j].strName] = ships[j];
                            }
                        }
                    }
                    catch { Interlocked.Increment(ref errors); }
                    if (Interlocked.Decrement(ref remaining) == 0)
                        doneEvent.Set();
                });
            }

            doneEvent.WaitOne();
            sw.Stop();

            if (errors > shipKeys.Count / 2)
            {
                SaveForcePlugin.Log.LogError("[OPT] Too many parse errors, falling back");
                for (int i = 0; i < shipKeys.Count; i++)
                    dictFiles[shipKeys[i]] = originalBytes[shipKeys[i]];
                return;
            }

            SaveForcePlugin.s_parsedShipsCache = parsedShips;
            SaveForcePlugin.s_originalShipBytes = originalBytes;
            SaveForcePlugin.s_dictFilesRef = dictFiles;

            SaveForcePlugin.Log.LogInfo("[OPT] Parallel parse: " + parsedShips.Count + " ships in " + sw.ElapsedMilliseconds + " ms" +
                (errors > 0 ? " (" + errors + " errors)" : ""));

            var gcSw = Stopwatch.StartNew();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            gcSw.Stop();
            SaveForcePlugin.Log.LogInfo("[OPT] Post-parse GC: " + gcSw.ElapsedMilliseconds + " ms");
        }
    }

    [HarmonyPatch(typeof(StarSystem), "Init",
        new Type[] { typeof(JsonStarSystemSave), typeof(JsonShip[]) })]
    public static class Patch_SystemInit
    {
        static void Prefix(ref JsonShip[] __1)
        {
            if (SaveForcePlugin.s_parsedShipsCache != null &&
                SaveForcePlugin.s_parsedShipsCache.Count > 0)
            {
                int origCount = __1 != null ? __1.Length : 0;
                JsonShip[] cached = new JsonShip[SaveForcePlugin.s_parsedShipsCache.Count];
                SaveForcePlugin.s_parsedShipsCache.Values.CopyTo(cached, 0);
                __1 = cached;
                SaveForcePlugin.Log.LogInfo("[OPT] Injected " + cached.Length + " ships (was " + origCount + ")");
                SaveForcePlugin.s_parsedShipsCache = null;
            }

            if (SaveForcePlugin.s_originalShipBytes != null && SaveForcePlugin.s_dictFilesRef != null)
            {
                foreach (var kv in SaveForcePlugin.s_originalShipBytes)
                    SaveForcePlugin.s_dictFilesRef[kv.Key] = kv.Value;
                SaveForcePlugin.s_originalShipBytes = null;
                SaveForcePlugin.s_dictFilesRef = null;
            }

            if (SaveForcePlugin.s_isLoading)
            {
                var gcSw = Stopwatch.StartNew();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                gcSw.Stop();
                SaveForcePlugin.Log.LogInfo("[OPT] Pre-spawn GC: " + gcSw.ElapsedMilliseconds + " ms");
    
                SaveForcePlugin.sw_SystemInit.Start();
            }
        }

        static void Postfix(ref IEnumerator __result)
        {
            if (SaveForcePlugin.CfgReduceYields.Value && SaveForcePlugin.s_isLoading)
                __result = new BatchedEnumerator(__result, SaveForcePlugin.CfgYieldBatchSize.Value);
        }
    }

    /// <summary>
    /// Profile GetCondOwner — aggregate timing to find if it's the spawning bottleneck.
    /// Uses the most common overload (non-generic, returns CondOwner).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_GetCondOwner_Profile
    {
        static MethodBase TargetMethod()
        {
            // Find the main GetCondOwner overload (the one Ship.CreatePart calls)
            // It should be a static method on DataHandler that returns CondOwner
            var methods = typeof(DataHandler).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "GetCondOwner" && !m.IsGenericMethod)
                .OrderByDescending(m => m.GetParameters().Length)
                .ToArray();

            if (methods.Length > 0)
            {
                SaveForcePlugin.Log.LogInfo("[PROFILE] Targeting GetCondOwner with " +
                    methods[0].GetParameters().Length + " params");
                return methods[0];
            }

            SaveForcePlugin.Log.LogWarning("[PROFILE] GetCondOwner not found!");
            return null;
        }

        static void Prefix()
        {
            SaveForcePlugin.sw_GetCondOwner.Start();
        }

        static void Postfix()
        {
            SaveForcePlugin.sw_GetCondOwner.Stop();
            SaveForcePlugin.s_getCondOwnerCalls++;
        }
    }

    /// <summary>Profile GetMesh to understand GetCondOwner breakdown</summary>
    [HarmonyPatch(typeof(DataHandler), "GetMesh", new Type[] { typeof(string), typeof(Transform) })]
    public static class Patch_GetMesh_Profile
    {
        static void Prefix()
        {
            SaveForcePlugin.sw_GetMesh.Start();
        }

        static void Postfix()
        {
            SaveForcePlugin.sw_GetMesh.Stop();
            SaveForcePlugin.s_getMeshCalls++;
        }
    }
    // ===================== v1.7.0: CONDITION LOADING OPTIMIZATIONS =====================

    [HarmonyPatch(typeof(DataHandler), "GetCond", new Type[] { typeof(string) })]
    public static class Patch_GetCond_TemplateCache
    {
        private static Dictionary<string, Condition> s_cache = new Dictionary<string, Condition>();
        private static MethodInfo s_memberwiseCloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private delegate object CloneFunc(object instance);
        private static CloneFunc s_cloneDelegate = (CloneFunc)Delegate.CreateDelegate(
            typeof(CloneFunc), s_memberwiseCloneMethod);

        internal static int s_hits = 0;
        internal static int s_misses = 0;
        internal static int s_templateCount { get { return s_cache.Count; } }

        static bool Prefix(string strName, ref Condition __result)
        {
            if (strName == null) { __result = null; return false; }
            Condition template;
            if (s_cache.TryGetValue(strName, out template))
            {
                __result = (Condition)s_cloneDelegate(template);
                s_hits++;
                return false;
            }
            s_misses++;
            return true;
        }

        static void Postfix(string strName, Condition __result)
        {
            if (__result != null && strName != null && !s_cache.ContainsKey(strName))
                s_cache[strName] = (Condition)s_cloneDelegate(__result);
        }

        internal static void ClearCache()
        {
            if (s_cache.Count > 0)
                SaveForcePlugin.Log.LogInfo("[CondCache] Clearing " + s_cache.Count +
                    " templates (hits=" + s_hits + " misses=" + s_misses + ")");
            s_cache.Clear();
            s_hits = 0;
            s_misses = 0;
        }
    }
    // ===================== v1.8.0: LOADING PERFORMANCE OPTIMIZATIONS =====================

    /// <summary>
    /// Skip GUIRenderTargets.UpdateFaces during save loading.
    /// AddCondAmount calls UpdateFaces for every NEW condition added.
    /// During loading ~462K calls are wasted; faces rebuilt in PostGameLoad.
    /// </summary>
    [HarmonyPatch(typeof(GUIRenderTargets), "UpdateFaces",
        new Type[] { typeof(CondOwner), typeof(string), typeof(bool) })]
    public static class Patch_UpdateFaces_SkipDuringLoad
    {
        internal static int s_skipped = 0;

        static bool Prefix()
        {
            if (SaveForcePlugin.s_isLoading)
            {
                s_skipped++;
                return false;
            }
            return true;
        }

        internal static void Reset() { s_skipped = 0; }
    }

    /// <summary>
    /// Global cache for Loot.ParseCondEquation(string).
    /// Per-instance alreadyParsed starts empty per CO.
    /// ~50K COs x ~10 conds = ~500K calls for only ~1-10K unique strings.
    /// </summary>
    [HarmonyPatch(typeof(Loot), "ParseCondEquation", new Type[] { typeof(string) })]
    public static class Patch_LootParseCache
    {
        private static Dictionary<string, KeyValuePair<string, Ostranauts.Core.Models.Tuple<double, double>>> s_cache
            = new Dictionary<string, KeyValuePair<string, Ostranauts.Core.Models.Tuple<double, double>>>();

        internal static int s_hits = 0;
        internal static int s_misses = 0;

        static bool Prefix(string strDef, ref KeyValuePair<string, Ostranauts.Core.Models.Tuple<double, double>> __result)
        {
            if (strDef == null) return true;

            KeyValuePair<string, Ostranauts.Core.Models.Tuple<double, double>> cached;
            if (s_cache.TryGetValue(strDef, out cached))
            {
                __result = cached;
                s_hits++;
                return false;
            }

            s_misses++;
            return true;
        }

        static void Postfix(string strDef, KeyValuePair<string, Ostranauts.Core.Models.Tuple<double, double>> __result)
        {
            if (strDef != null && !s_cache.ContainsKey(strDef))
                s_cache[strDef] = __result;
        }

        internal static void ClearCache()
        {
            if (s_cache.Count > 0)
                SaveForcePlugin.Log.LogInfo("[ParseCache] Clearing " + s_cache.Count +
                    " entries (hits=" + s_hits + " misses=" + s_misses + ")");
            s_cache.Clear();
            s_hits = 0;
            s_misses = 0;
        }
    }
    // ===================== v1.9.0: SUB-PROFILING + CONDRULE CACHE =====================

    /// <summary>Profile CondOwner.SetData — the main work inside GetCondOwner</summary>
    [HarmonyPatch(typeof(CondOwner), "SetData",
        new Type[] { typeof(JsonCondOwner), typeof(bool), typeof(JsonCondOwnerSave) })]
    public static class Patch_SetData_Profile
    {
        static void Prefix() { SaveForcePlugin.sw_SetData.Start(); }
        static void Postfix() { SaveForcePlugin.sw_SetData.Stop(); SaveForcePlugin.s_setDataCalls++; }
    }

    /// <summary>Profile CondOwner.Awake — 33+ collection allocations per CO</summary>
    [HarmonyPatch(typeof(CondOwner), "Awake")]
    public static class Patch_Awake_Profile
    {
        static void Prefix() { SaveForcePlugin.sw_Awake.Start(); }
        static void Postfix() { SaveForcePlugin.sw_Awake.Stop(); SaveForcePlugin.s_awakeCalls++; }
    }

    /// <summary>
    /// Cache CondRule.LoadSaveInfo results — avoids Split + dictCondRules lookup + Clone per call.
    /// Each CO loads ~3-5 CondRules from save, most with identical strings across COs.
    /// MemberwiseClone gives independent fModifier/nNesting per CO instance.
    /// </summary>
    [HarmonyPatch(typeof(CondRule), "LoadSaveInfo", new Type[] { typeof(string) })]
    public static class Patch_CondRuleCache
    {
        private static Dictionary<string, CondRule> s_cache = new Dictionary<string, CondRule>();
        private static MethodInfo s_cloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private delegate object CloneFunc(object instance);
        private static CloneFunc s_clone = (CloneFunc)Delegate.CreateDelegate(
            typeof(CloneFunc), s_cloneMethod);

        internal static int s_hits = 0;
        internal static int s_misses = 0;

        static bool Prefix(string strDef, ref CondRule __result)
        {
            if (strDef == null) { __result = null; return false; }

            CondRule template;
            if (s_cache.TryGetValue(strDef, out template))
            {
                __result = (CondRule)s_clone(template);
                s_hits++;
                return false;
            }

            s_misses++;
            return true;
        }

        static void Postfix(string strDef, CondRule __result)
        {
            if (__result != null && strDef != null && !s_cache.ContainsKey(strDef))
                s_cache[strDef] = (CondRule)s_clone(__result);
        }

        internal static void ClearCache()
        {
            if (s_cache.Count > 0)
                SaveForcePlugin.Log.LogInfo("[CondRuleCache] Clearing " + s_cache.Count +
                    " entries (hits=" + s_hits + " misses=" + s_misses + ")");
            s_cache.Clear();
            s_hits = 0;
            s_misses = 0;
        }
    }
    /// <summary>Profile AddCondAmount — main work inside SetData condition loop</summary>
    [HarmonyPatch(typeof(CondOwner), "AddCondAmount",
        new Type[] { typeof(string), typeof(double), typeof(double), typeof(float) })]
    public static class Patch_AddCondAmount_Profile
    {
        static void Prefix() { SaveForcePlugin.sw_AddCond.Start(); }
        static void Postfix() { SaveForcePlugin.sw_AddCond.Stop(); SaveForcePlugin.s_addCondCalls++; }
    }
    /// <summary>v1.11.0: Skip Item.VisualizeOverlays during loading — purely visual overlay</summary>
    [HarmonyPatch(typeof(Item), "VisualizeOverlays")]
    public static class Patch_VisualizeOverlays_SkipDuringLoad
    {
        static bool Prefix()
        {
            if (SaveForcePlugin.s_isLoading)
            {
                SaveForcePlugin.s_visualizeOverlaysSkipped++;
                return false;
            }
            return true;
        }
    }

    /// <summary>v1.11.0: Profile Item.SetData (material, texture, block creation)</summary>
    [HarmonyPatch(typeof(Item), "SetData", new Type[] { typeof(string), typeof(float), typeof(float) })]
    public static class Patch_ItemSetData_Profile
    {
        static void Prefix() { SaveForcePlugin.sw_ItemSetData.Start(); }
        static void Postfix() { SaveForcePlugin.sw_ItemSetData.Stop(); SaveForcePlugin.s_itemSetDataCalls++; }
    }
}
