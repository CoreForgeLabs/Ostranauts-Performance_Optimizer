// OstronautsOptimizer v8.1.0 — Heap Pre-Expansion + Memory Ceiling
// Key insight: Boehm GC stop-the-world ~800ms on 2.3GB heap every ~5s
// because free space is only 3-28MB and alloc rate is ~5MB/s.
// Solution: after load, allocate+free ~256MB to permanently expand heap.
// Result: GC free space ~256MB → GC every ~50s instead of 5s.
// v8.0: Added memory ceiling to prevent unbounded heap growth (7GB+ leak).
// v8.1: Adaptive ceiling — auto-raises if Boehm can't compact below target.
//   Periodic forced GC when heap exceeds ceiling or free space is too low.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OstronautsOptimizer
{
    [BepInPlugin("com.perf.ostranauts.optimizer",
        "Ostranauts Performance Optimizer", "8.1.0")]
    public class OptimizerPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static bool GameLoaded = false;
        internal static ConfigEntry<bool> CfgFirstOrDefault;
        internal static ConfigEntry<bool> CfgInteractionLog;
        internal static ConfigEntry<int> CfgFrameBudgetMs;
        internal static ConfigEntry<int> CfgMaxSimSteps;
        internal static ConfigEntry<float> CfgMaxDeltaTime;
        internal static ConfigEntry<int> CfgHeapExpansionMB;
        internal static ConfigEntry<int> CfgMemCeilingMB;
        internal static ConfigEntry<int> CfgGCIntervalSec;
        internal static ConfigEntry<int> CfgMinFreeMB;

        // GC ceiling state
        private static float s_lastForcedGC = 0f;
        private static int s_forcedGCCount = 0;
        private static long s_lastHeapAfterGC = 0;
        private static bool s_ceilingWarned = false;
        private static int s_ceilingFailStreak = 0;
        private static long s_effectiveCeilingMB = 0; // 0=use config

        // P/Invoke for Mono GC diagnostics
        [DllImport("mono", EntryPoint="mono_gc_get_heap_size")]
        internal static extern long MonoGCHeapSize();
        [DllImport("mono", EntryPoint="mono_gc_get_used_size")]
        internal static extern long MonoGCUsedSize();
        internal static bool s_pinvokeOk = false;

        // Frame-level alloc tracking
        public static long aFrameTotal;
        // Per-method alloc tracking (AS and SS only, low freq)
        public static long aAllocAS;
        public static long aAllocSS;

        private static FieldInfo f_fTimeCoeffPause;
        private static FieldInfo f_finishedLoading;
        private float _lastDiag = 0f;

        // Heap expansion state
        private static bool s_heapExpanded = false;
        private static float s_loadedTime = -1f;
        private static string s_heapExpandResult = "";

        private static readonly Stopwatch s_frameSW =
            new Stopwatch();
        private static int s_frameCount;
        private static float s_reportTimer;
        private static float s_totalFrameMs, s_worstFrameMs;
        private static int s_spikeCount, s_gcSpikeCount;
        private static long s_memAtReport;
        private static int s_gcAtReport;
        private static long s_fMemStart;
        private static int s_fGCStart;
        private static int s_pausedFrames;

        public static int simStepsTotal, simStepsMax;
        public static int fSimSteps;

        public static long tAdvanceSim; public static int cAdvanceSim;
        public static long tICO;    public static int cICO;
        public static long tEndTurn; public static int cEndTurn;
        public static long tGetMove2; public static int cGetMove2;
        public static long tGetWork; public static int cGetWork;
        public static long tParseCL; public static int cParseCL;
        public static long tStarSys; public static int cStarSys;
        public static long tCleanup; public static int cCleanup;
        public static long tUpdateStats; public static int cUpdateStats;
        public static int cICOSkipped;
        public static int cIAcache;

        private static readonly StringBuilder SB =
            new StringBuilder(4096);

        private void Awake()
        {
            Log = Logger;
            f_fTimeCoeffPause = AccessTools.Field(
                typeof(CrewSim), "fTimeCoeffPause");
            f_finishedLoading = AccessTools.Field(
                typeof(CrewSim), "_finishedLoading");

            CfgFirstOrDefault = Config.Bind("Optimizations",
                "OptFirstOrDefault", true,
                "Replace UniqueList.FirstOrDefault");
            CfgInteractionLog = Config.Bind("Optimizations",
                "SuppressInteractionLog", true,
                "Cache missing interaction lookups");
            CfgFrameBudgetMs = Config.Bind("SimLoop",
                "FrameBudgetMs", 12,
                "Time budget for UpdateICOs ms. 0=no limit.");
            CfgMaxSimSteps = Config.Bind("SimLoop",
                "MaxSimStepsPerFrame", 50,
                "Hard cap on AdvanceSim. 0=no limit.");
            CfgMaxDeltaTime = Config.Bind("SimLoop",
                "MaxDeltaTime", 0.1f,
                "Clamp Time.maximumDeltaTime to reduce " +
                "post-GC burst. 0=no change.");
            CfgHeapExpansionMB = Config.Bind("GC",
                "HeapExpansionMB", 1024,
                "MB to pre-expand Mono heap after load. " +
                "Reduces GC frequency dramatically. " +
                "0=disabled. 256=moderate (GC every ~25s). " +
                "512=good (GC every ~50s). " +
                "1024=default (GC every ~100s).");
            CfgMemCeilingMB = Config.Bind("GC",
                "MemCeilingMB", 3072,
                "Max allowed heap size in MB. When exceeded, " +
                "forced GC runs to reclaim memory. " +
                "Prevents unbounded heap growth (7GB+ leak). " +
                "0=disabled. 2048=aggressive. " +
                "3072=balanced (default). 4096=relaxed.");
            CfgGCIntervalSec = Config.Bind("GC",
                "GCIntervalSec", 120,
                "Min seconds between forced GC runs. " +
                "Lower=more responsive but more pauses. " +
                "0=disabled periodic GC. 60=aggressive. " +
                "120=default. 300=relaxed.");
            CfgMinFreeMB = Config.Bind("GC",
                "MinFreeMB", 256,
                "If free heap space drops below this, " +
                "force GC even before interval expires. " +
                "Keeps a cushion of free memory. " +
                "0=disabled. 128=tight. 256=default.");

            if (CfgMaxDeltaTime.Value > 0f)
            {
                float old = Time.maximumDeltaTime;
                Time.maximumDeltaTime = CfgMaxDeltaTime.Value;
                Log.LogInfo("[CFG] maxDT: " +
                    old + "->" + CfgMaxDeltaTime.Value);
            }

            try
            {
                long h = MonoGCHeapSize();
                long u = MonoGCUsedSize();
                s_pinvokeOk = true;
                Log.LogInfo("[MONO] heap=" +
                    (h / 1048576) + "MB used=" +
                    (u / 1048576) + "MB");
            }
            catch (Exception ex)
            {
                Log.LogWarning("[MONO] P/Invoke: " +
                    ex.Message);
            }

            Harmony harmony = new Harmony(
                "com.perf.ostranauts.optimizer");
            int ok = 0;
            Type[] patches = new Type[]
            {
                typeof(Patch_AdvanceSim),
                typeof(Patch_UpdateICOs),
                typeof(Patch_EndTurn),
                typeof(Patch_GetMove2),
                typeof(Patch_GetWork),
                typeof(Patch_ParseCondLoot),
                typeof(Patch_StarSystemUpdate),
                typeof(Patch_Cleanup),
                typeof(Patch_FirstOrDefault),
                typeof(Patch_UpdateStats),
                typeof(Patch_SuppressInteractionLog)
            };

            for (int i = 0; i < patches.Length; i++)
            {
                try
                {
                    harmony.CreateClassProcessor(
                        patches[i]).Patch();
                    ok++;
                    Log.LogInfo("  [OK] " + patches[i].Name);
                }
                catch (Exception ex)
                {
                    Log.LogWarning("  [FAIL] " +
                        patches[i].Name + ": " + ex.Message);
                }
            }

            Log.LogInfo("=== Optimizer v8.1.0 (" +
                ok + "/" + patches.Length + " patches) ===");
            Log.LogInfo("  FirstOrDefault=" +
                CfgFirstOrDefault.Value +
                " InteractionLog=" +
                CfgInteractionLog.Value +
                " FrameBudget=" + CfgFrameBudgetMs.Value +
                " MaxSimSteps=" + CfgMaxSimSteps.Value +
                " MaxDeltaTime=" + CfgMaxDeltaTime.Value +
                " HeapExpansion=" +
                CfgHeapExpansionMB.Value + "MB" +
                " MemCeiling=" +
                CfgMemCeilingMB.Value + "MB" +
                " GCInterval=" +
                CfgGCIntervalSec.Value + "s" +
                " MinFree=" +
                CfgMinFreeMB.Value + "MB");
        }

        private void Update()
        {
            s_fMemStart = GC.GetTotalMemory(false);
            s_fGCStart = GC.CollectionCount(0);
            fSimSteps = 0;
            s_frameSW.Reset();
            s_frameSW.Start();

            // Delayed heap expansion: 3s after load detected
            if (GameLoaded && !s_heapExpanded)
            {
                if (s_loadedTime < 0f)
                    s_loadedTime =
                        Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup -
                    s_loadedTime >= 3f)
                {
                    s_heapExpanded = true;
                    ExpandHeap(CfgHeapExpansionMB.Value);
                }
            }

            // === MEMORY CEILING: prevent unbounded growth ===
            if (GameLoaded && s_heapExpanded &&
                s_pinvokeOk)
            {
                CheckMemoryCeiling();
            }
        }

        /// <summary>
        /// Monitor heap size and force GC when:
        /// 1) Heap exceeds ceiling (e.g. 3072MB)
        /// 2) Free space drops below minimum (e.g. 256MB)
        /// 3) Periodic interval expires (e.g. 120s)
        /// This prevents the 7GB+ leak seen in long sessions.
        /// </summary>
        private void CheckMemoryCeiling()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - s_lastForcedGC;

            // Don't check too frequently (min 10s between)
            if (elapsed < 10f) return;

            long heap = 0, used = 0, free = 0;
            try
            {
                heap = MonoGCHeapSize();
                used = MonoGCUsedSize();
                free = heap - used;
            }
            catch { return; }

            long heapMB = heap / 1048576;
            long freeMB = free / 1048576;
            int cfgCeiling = CfgMemCeilingMB.Value;
            // Use adaptive ceiling if raised
            long ceiling = s_effectiveCeilingMB > 0
                ? s_effectiveCeilingMB : cfgCeiling;
            int minFree = CfgMinFreeMB.Value;
            int interval = CfgGCIntervalSec.Value;

            bool shouldGC = false;
            string reason = "";

            // Trigger 1: heap exceeds ceiling
            if (ceiling > 0 && heapMB > ceiling)
            {
                shouldGC = true;
                reason = "CEILING(" + heapMB + ">" +
                    ceiling + "MB)";
            }
            // Trigger 2: free space too low
            else if (minFree > 0 && freeMB < minFree &&
                elapsed >= 30f)
            {
                shouldGC = true;
                reason = "LOW_FREE(" + freeMB + "<" +
                    minFree + "MB)";
            }
            // Trigger 3: periodic interval
            else if (interval > 0 && elapsed >= interval)
            {
                shouldGC = true;
                reason = "PERIODIC(" +
                    elapsed.ToString("F0") + "s)";
            }

            if (!shouldGC) return;

            // Execute forced GC
            long hBefore = heap;
            long uBefore = used;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long hAfter = MonoGCHeapSize();
            long uAfter = MonoGCUsedSize();
            long fAfter = hAfter - uAfter;
            long reclaimed = uBefore - uAfter;

            s_lastForcedGC = now;
            s_forcedGCCount++;
            s_lastHeapAfterGC = hAfter;

            long hAfterMB = hAfter / 1048576;

            Log.LogInfo("[GC-CEIL] #" + s_forcedGCCount +
                " " + reason +
                " H:" + (hBefore / 1048576) + ">" +
                hAfterMB + "MB" +
                " U:" + (uBefore / 1048576) + ">" +
                (uAfter / 1048576) + "MB" +
                " Free:" + (fAfter / 1048576) + "MB" +
                " Reclaimed:" + (reclaimed / 1048576) +
                "MB");

            // Adaptive ceiling: if heap still above
            // ceiling after GC, Boehm can't shrink it.
            // After 3 failures, raise ceiling to avoid
            // repeated useless GC pauses.
            if (ceiling > 0 && hAfterMB > ceiling)
            {
                s_ceilingFailStreak++;
                if (s_ceilingFailStreak >= 3)
                {
                    long newCeiling = hAfterMB + 512;
                    Log.LogWarning(
                        "[GC-CEIL] Heap " + hAfterMB +
                        "MB stuck above ceiling " +
                        ceiling + "MB after " +
                        s_ceilingFailStreak +
                        " GCs. Boehm can't compact." +
                        " Auto-raising ceiling to " +
                        newCeiling + "MB");
                    s_effectiveCeilingMB = newCeiling;
                    s_ceilingFailStreak = 0;
                }
            }
            else
            {
                s_ceilingFailStreak = 0;
            }
        }

        /// <summary>
        /// Expand the Mono/Boehm heap by allocating and
        /// freeing byte arrays. Boehm keeps the expanded
        /// heap, creating a large free block pool.
        /// Future allocations use free blocks without GC.
        /// </summary>
        private static void ExpandHeap(int targetMB)
        {
            if (targetMB <= 0)
            {
                s_heapExpandResult = "disabled";
                Log.LogInfo("[HEAP] Expansion disabled");
                return;
            }

            long hBefore = 0, uBefore = 0;
            if (s_pinvokeOk)
            {
                hBefore = MonoGCHeapSize();
                uBefore = MonoGCUsedSize();
            }

            Log.LogInfo("[HEAP] Expanding by ~" +
                targetMB + "MB...");

            try
            {
                // Use 64KB blocks for efficiency.
                // 256MB / 64KB = 4096 blocks.
                // Also add some smaller blocks to populate
                // small-object free lists.
                int largeBlockSize = 65536; // 64KB
                int largeCount = (targetMB * 16); // 16 per MB
                // Small blocks: ~10% of target in 1KB chunks
                int smallCount = (targetMB * 1024 / 10) / 1;
                // = targetMB * ~102

                int totalCount = largeCount + smallCount;
                object[] blocks = new object[totalCount];
                int idx = 0;

                // Large blocks (90% of expansion)
                for (int i = 0; i < largeCount; i++)
                    blocks[idx++] = new byte[largeBlockSize];

                // Small blocks (10% of expansion)
                // Mix of common sizes
                for (int i = 0; i < smallCount; i++)
                {
                    switch (i % 5)
                    {
                        case 0: blocks[idx++] =
                            new byte[32]; break;
                        case 1: blocks[idx++] =
                            new byte[64]; break;
                        case 2: blocks[idx++] =
                            new byte[128]; break;
                        case 3: blocks[idx++] =
                            new byte[256]; break;
                        case 4: blocks[idx++] =
                            new byte[512]; break;
                    }
                }

                // Release everything
                blocks = null;

                // Double GC to ensure all freed
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long hAfter = 0, uAfter = 0, fAfter = 0;
                if (s_pinvokeOk)
                {
                    hAfter = MonoGCHeapSize();
                    uAfter = MonoGCUsedSize();
                    fAfter = hAfter - uAfter;
                }

                long deltaH = hAfter - hBefore;
                s_heapExpandResult =
                    "H:" + (hBefore / 1048576) + ">" +
                    (hAfter / 1048576) + "MB +" +
                    (deltaH / 1048576) + "MB Free=" +
                    (fAfter / 1048576) + "MB";

                Log.LogInfo("[HEAP] Before: H=" +
                    (hBefore / 1048576) + "MB U=" +
                    (uBefore / 1048576) + "MB F=" +
                    ((hBefore - uBefore) / 1048576) + "MB");
                Log.LogInfo("[HEAP] After:  H=" +
                    (hAfter / 1048576) + "MB U=" +
                    (uAfter / 1048576) + "MB Free=" +
                    (fAfter / 1048576) + "MB");
                Log.LogInfo("[HEAP] Expanded by +" +
                    (deltaH / 1048576) + "MB. " +
                    "Expected GC interval: ~" +
                    ((fAfter / 1048576) / 5) +
                    "s at 5MB/s alloc rate");
            }
            catch (Exception ex)
            {
                s_heapExpandResult = "FAIL: " +
                    ex.Message;
                Log.LogError("[HEAP] Expansion failed: " +
                    ex.ToString());
            }
        }

        private void LateUpdate()
        {
            s_frameSW.Stop();
            float ms = (float)
                s_frameSW.Elapsed.TotalMilliseconds;
            s_frameCount++;
            s_totalFrameMs += ms;
            if (ms > s_worstFrameMs)
                s_worstFrameMs = ms;

            bool paused = false;
            try
            {
                if (f_fTimeCoeffPause != null)
                    paused = (float)f_fTimeCoeffPause
                        .GetValue(null) == 0f;
            }
            catch {}
            if (paused) s_pausedFrames++;

            simStepsTotal += fSimSteps;
            if (fSimSteps > simStepsMax)
                simStepsMax = fSimSteps;

            long memAfter = GC.GetTotalMemory(false);
            int gcDelta = GC.CollectionCount(0) - s_fGCStart;

            // Track frame alloc (skip GC frames)
            long fAlloc = memAfter - s_fMemStart;
            if (fAlloc > 0 && gcDelta == 0)
                aFrameTotal += fAlloc;

            if (ms > 33f)
            {
                s_spikeCount++;
                if (gcDelta > 0) s_gcSpikeCount++;
                SB.Length = 0;
                SB.Append("[SPIKE] ");
                SB.Append(ms.ToString("F1"));
                SB.Append("ms");
                if (gcDelta > 0)
                    SB.Append(" [GCx")
                        .Append(gcDelta).Append("]");
                SB.Append(" Sim:").Append(fSimSteps);
                if (paused) SB.Append(" [PAUSED]");
                Log.LogWarning(SB.ToString());
            }

            if (Time.realtimeSinceStartup - _lastDiag >= 5f)
            {
                _lastDiag = Time.realtimeSinceStartup;
                bool loaded = false;
                try
                {
                    CrewSim cs = UnityEngine.Object
                        .FindObjectOfType<CrewSim>();
                    if (cs != null &&
                        f_finishedLoading != null)
                        loaded = (bool)f_finishedLoading
                            .GetValue(cs);
                }
                catch {}
                Log.LogInfo("[DIAG] loaded=" + loaded +
                    " paused=" + paused +
                    " TimeScale=" + Time.timeScale +
                    " Sim=" + fSimSteps);
            }

            s_reportTimer += Time.unscaledDeltaTime;
            if (s_reportTimer >= 5f)
            {
                s_reportTimer = 0f;
                LogReport();
            }
        }

        private void LogReport()
        {
            long mem = GC.GetTotalMemory(false);
            int gc = GC.CollectionCount(0);
            float avgMs = s_frameCount > 0 ?
                s_totalFrameMs / s_frameCount : 0;
            float fps = avgMs > 0 ? 1000f / avgMs : 0;
            int totalGC = gc - s_gcAtReport;
            int active = s_frameCount - s_pausedFrames;
            float avgSim = s_frameCount > 0 ?
                (float)simStepsTotal / s_frameCount : 0;

            SB.Length = 0;
            SB.Append("\n=== PERF v7.5 (5s) ===\n");
            SB.Append("  Fr: ").Append(s_frameCount);
            SB.Append(" (").Append(active).Append("act)");
            SB.Append(" FPS:").Append(fps.ToString("F0"));
            SB.Append(" Worst:").Append(
                s_worstFrameMs.ToString("F1"))
                .Append("ms\n");
            SB.Append("  Sp:").Append(s_spikeCount);
            SB.Append(" (").Append(s_gcSpikeCount)
                .Append("gc)");
            SB.Append(" GC:").Append(totalGC);
            SB.Append(" M:").Append(
                (mem / 1048576.0).ToString("F0"))
                .Append("MB\n");

            if (s_pinvokeOk)
            {
                try
                {
                    long mh = MonoGCHeapSize();
                    long mu = MonoGCUsedSize();
                    SB.Append("  MH:").Append(mh / 1048576)
                        .Append("MB MU:")
                        .Append(mu / 1048576)
                        .Append("MB F:")
                        .Append((mh - mu) / 1048576)
                        .Append("MB\n");
                }
                catch {}
            }

            SB.Append("  Sim:").Append(simStepsTotal);
            SB.Append(" (").Append(avgSim.ToString("F1"));
            SB.Append("/f) Max:")
                .Append(simStepsMax).Append("\n");

            A("  AS ", tAdvanceSim, cAdvanceSim);
            A("  ICO", tICO, cICO);
            A("  ET ", tEndTurn, cEndTurn);
            A("  GM2", tGetMove2, cGetMove2);
            A("  GW ", tGetWork, cGetWork);
            A("  PCL", tParseCL, cParseCL);
            A("  CU ", tCleanup, cCleanup);
            A("  US ", tUpdateStats, cUpdateStats);
            A("  SS ", tStarSys, cStarSys);

            if (cICOSkipped > 0)
                SB.Append("  ICOskip:")
                    .Append(cICOSkipped);
            if (cIAcache > 0)
                SB.Append(" ia:").Append(cIAcache);

            // Allocation report
            SB.Append("\n  Alloc:")
                .Append((aFrameTotal / 1024.0)
                    .ToString("F0"))
                .Append("KB/5s (")
                .Append((aFrameTotal / 5120.0)
                    .ToString("F0"))
                .Append("KB/s)");

            // Per-method alloc breakdown
            if (aAllocAS > 0 || aAllocSS > 0)
            {
                SB.Append("\n  A.AS:")
                    .Append((aAllocAS / 1024.0)
                        .ToString("F0"))
                    .Append("KB A.SS:")
                    .Append((aAllocSS / 1024.0)
                        .ToString("F0"))
                    .Append("KB A.Oth:")
                    .Append(((aFrameTotal -
                        aAllocAS - aAllocSS) / 1024.0)
                        .ToString("F0"))
                    .Append("KB");
            }

            // Heap expansion status
            if (s_heapExpandResult.Length > 0)
                SB.Append("\n  Heap: ")
                    .Append(s_heapExpandResult);

            // GC ceiling status
            if (s_forcedGCCount > 0)
            {
                SB.Append("\n  GC-Ceil: ")
                    .Append(s_forcedGCCount)
                    .Append("x forced");
                if (s_pinvokeOk)
                {
                    try
                    {
                        long lh = MonoGCHeapSize();
                        long lu = MonoGCUsedSize();
                        long eCeil = s_effectiveCeilingMB > 0
                            ? s_effectiveCeilingMB
                            : CfgMemCeilingMB.Value;
                        SB.Append(" cur:")
                            .Append(lh / 1048576)
                            .Append("/")
                            .Append(eCeil)
                            .Append("MB");
                        if (s_effectiveCeilingMB > 0)
                            SB.Append("(auto)");
                    }
                    catch {}
                }
            }

            SB.Append("\n=================");
            Log.LogInfo(SB.ToString());

            // Reset counters
            s_memAtReport = mem;
            s_gcAtReport = gc;
            s_frameCount = 0; s_totalFrameMs = 0;
            s_worstFrameMs = 0;
            s_spikeCount = 0; s_gcSpikeCount = 0;
            s_pausedFrames = 0;
            simStepsTotal = 0; simStepsMax = 0;
            tAdvanceSim = 0; cAdvanceSim = 0;
            tICO = 0; cICO = 0;
            tEndTurn = 0; cEndTurn = 0;
            tGetMove2 = 0; cGetMove2 = 0;
            tGetWork = 0; cGetWork = 0;
            tParseCL = 0; cParseCL = 0;
            tStarSys = 0; cStarSys = 0;
            tCleanup = 0; cCleanup = 0;
            tUpdateStats = 0; cUpdateStats = 0;
            cICOSkipped = 0; cIAcache = 0;
            aFrameTotal = 0;
            aAllocAS = 0; aAllocSS = 0;
        }

        private void A(string l, long t, int c)
        {
            SB.Append(l);
            if (c == 0) { SB.Append(" --\n"); return; }
            float ms = (float)((double)t /
                Stopwatch.Frequency * 1000.0);
            SB.Append(" ").Append(c).Append("x ");
            SB.Append(ms.ToString("F1")).Append("ms (");
            float a = ms / c;
            SB.Append(a < 1f ? a.ToString("F3") :
                a.ToString("F1")).Append("ms/c)\n");
        }
    }

    // ======== PROFILING PATCHES ========

    [HarmonyPatch]
    public static class Patch_AdvanceSim
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CrewSim), "AdvanceSim");
        }

        private static int s_lastFrame = -1;
        private static int s_count = 0;
        // Per-method alloc tracking
        private static long s_memBefore;
        private static int s_gcBefore;

        static bool Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
            s_memBefore = GC.GetTotalMemory(false);
            s_gcBefore = GC.CollectionCount(0);
            OptimizerPlugin.fSimSteps++;

            int max = OptimizerPlugin.CfgMaxSimSteps.Value;
            if (max > 0)
            {
                int fc = Time.frameCount;
                if (fc != s_lastFrame)
                {
                    s_lastFrame = fc;
                    s_count = 0;
                }
                s_count++;
                if (s_count > max) return false;
            }
            return true;
        }

        static void Postfix(long __state)
        {
            OptimizerPlugin.tAdvanceSim +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cAdvanceSim++;

            // Alloc tracking: only if no GC during call
            if (GC.CollectionCount(0) == s_gcBefore)
            {
                long d = GC.GetTotalMemory(false) -
                    s_memBefore;
                if (d > 0) OptimizerPlugin.aAllocAS += d;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_UpdateICOs
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CrewSim), "UpdateICOs");
        }

        private static double s_simMs = 0;
        private static int s_lastFC = -1;

        static bool Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
            int b = OptimizerPlugin.CfgFrameBudgetMs.Value;
            if (b > 0)
            {
                int fc = Time.frameCount;
                if (fc != s_lastFC)
                {
                    s_lastFC = fc;
                    s_simMs = 0;
                }
                if (s_simMs > b)
                {
                    OptimizerPlugin.cICOSkipped++;
                    return false;
                }
            }
            return true;
        }

        static void Postfix(long __state)
        {
            long e =
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.tICO += e;
            OptimizerPlugin.cICO++;
            double ms = (double)e /
                Stopwatch.Frequency * 1000.0;
            int fc = Time.frameCount;
            if (fc == s_lastFC) s_simMs += ms;
            else { s_lastFC = fc; s_simMs = ms; }
        }
    }

    [HarmonyPatch]
    public static class Patch_EndTurn
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "EndTurn");
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tEndTurn +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cEndTurn++;
        }
    }

    [HarmonyPatch]
    public static class Patch_GetMove2
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "GetMove2");
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tGetMove2 +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cGetMove2++;
        }
    }

    [HarmonyPatch]
    public static class Patch_GetWork
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "GetWork");
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tGetWork +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cGetWork++;
        }
    }

    [HarmonyPatch]
    public static class Patch_ParseCondLoot
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "ParseCondLoot",
                new Type[] { typeof(string),
                    typeof(double) });
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tParseCL +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cParseCL++;
        }
    }

    [HarmonyPatch]
    public static class Patch_StarSystemUpdate
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(StarSystem), "Update");
        }

        private static long s_memBefore;
        private static int s_gcBefore;

        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
            s_memBefore = GC.GetTotalMemory(false);
            s_gcBefore = GC.CollectionCount(0);

            if (!OptimizerPlugin.GameLoaded)
            {
                OptimizerPlugin.GameLoaded = true;
                OptimizerPlugin.Log.LogInfo(
                    "[GAME] StarSystem.Update — loaded");
            }
        }

        static void Postfix(long __state)
        {
            OptimizerPlugin.tStarSys +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cStarSys++;

            if (GC.CollectionCount(0) == s_gcBefore)
            {
                long d = GC.GetTotalMemory(false) -
                    s_memBefore;
                if (d > 0)
                    OptimizerPlugin.aAllocSS += d;
            }
        }
    }

    // ======== OPTIMIZATION PATCHES ========

    [HarmonyPatch]
    public static class Patch_Cleanup
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "Cleanup");
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tCleanup +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cCleanup++;
        }
    }

    [HarmonyPatch]
    public static class Patch_FirstOrDefault
    {
        static MethodBase TargetMethod()
        {
            Type ulist = typeof(
                Ostranauts.Core.Models
                    .UniqueList<CondOwner>);
            return AccessTools.Method(
                ulist, "FirstOrDefault");
        }

        private static readonly FieldInfo f_list =
            AccessTools.Field(
                typeof(Ostranauts.Core.Models
                    .UniqueList<CondOwner>), "_list");

        static bool Prefix(object __instance,
            ref object __result)
        {
            if (!OptimizerPlugin.CfgFirstOrDefault.Value)
                return true;
            try
            {
                var list = f_list != null ?
                    f_list.GetValue(__instance)
                        as IList : null;
                __result = (list != null &&
                    list.Count > 0) ?
                        list[0] : null;
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch]
    public static class Patch_UpdateStats
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(CondOwner), "UpdateStats");
        }
        static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }
        static void Postfix(long __state)
        {
            OptimizerPlugin.tUpdateStats +=
                Stopwatch.GetTimestamp() - __state;
            OptimizerPlugin.cUpdateStats++;
        }
    }

    [HarmonyPatch]
    public static class Patch_SuppressInteractionLog
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(DataHandler), "GetInteraction");
        }

        private static readonly HashSet<string> _missing =
            new HashSet<string>();
        private static readonly FieldInfo f_dict =
            AccessTools.Field(
                typeof(DataHandler), "dictInteractions");

        static bool Prefix(string strName,
            ref object __result)
        {
            if (!OptimizerPlugin.CfgInteractionLog.Value)
                return true;

            if (strName == null)
            {
                __result = null;
                return false;
            }
            if (_missing.Contains(strName))
            {
                __result = null;
                OptimizerPlugin.cIAcache++;
                return false;
            }
            IDictionary dict = f_dict != null ?
                f_dict.GetValue(null) as IDictionary
                : null;
            if (dict != null &&
                !dict.Contains(strName))
            {
                _missing.Add(strName);
                __result = null;
                OptimizerPlugin.cIAcache++;
                return false;
            }
            return true;
        }
    }
}
