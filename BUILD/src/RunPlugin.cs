// RunPlugin — Auto-load last save on game startup
// Separate from SaveForce optimization patches for clean modularity
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Events;
using Ostranauts.UI.Loading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveForce
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class RunPlugin : BaseUnityPlugin
    {
        public const string GUID = "com.coreforgelabs.run";
        public const string NAME = "Run";
        public const string VERSION = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CfgAutoLoad;
        internal static ConfigEntry<bool> CfgKillDuplicates;
        internal static ConfigEntry<string> CfgSaveName;

        private static bool s_autoLoadTriggered = false;
        private static bool s_waitingForLoadManager = false;

        // Flag file: when this exists, auto-load is triggered once then file is deleted
        private static string FlagFilePath
        {
            get
            {
                string dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.Combine(dir, "autoload.flag");
            }
        }

        private void Awake()
        {
            Log = Logger;

            CfgKillDuplicates = Config.Bind("General", "KillDuplicates", true,
                "Kill other Ostranauts.exe processes on startup");
            CfgAutoLoad = Config.Bind("General", "AutoLoadLastSave", false,
                "Always auto-load on startup (true=always, false=only via flag file/shortcut)");
            CfgSaveName = Config.Bind("General", "SaveName", "",
                "Specific save name to load (empty = most recent)");

            if (CfgKillDuplicates.Value) KillDuplicateInstances();

            // Auto-load if config says so OR flag file exists (one-shot)
            bool flagFileExists = System.IO.File.Exists(FlagFilePath);
            bool shouldAutoLoad = CfgAutoLoad.Value || flagFileExists;

            if (flagFileExists)
            {
                try { System.IO.File.Delete(FlagFilePath); } catch { }
                Log.LogInfo("Flag file detected — auto-load triggered");
            }

            if (shouldAutoLoad)
            {
                s_waitingForLoadManager = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            Log.LogInfo("=== Run v" + VERSION + " === AutoLoad=" + shouldAutoLoad +
                " (config=" + CfgAutoLoad.Value + " flag=" + flagFileExists + ")" +
                " KillDuplicates=" + CfgKillDuplicates.Value);
        }

        private void KillDuplicateInstances()
        {
            try
            {
                int myPid = Process.GetCurrentProcess().Id;
                Process[] procs = Process.GetProcessesByName("Ostranauts");
                int killed = 0;
                for (int i = 0; i < procs.Length; i++)
                    if (procs[i].Id != myPid)
                    { try { procs[i].Kill(); killed++; } catch { } }
                if (killed > 0) Log.LogInfo("Killed " + killed + " duplicate(s)");
            }
            catch { }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu2" && s_waitingForLoadManager && !s_autoLoadTriggered)
            {
                Log.LogInfo("MainMenu2 loaded, scheduling auto-load...");
                StartCoroutine(WaitAndAutoLoad());
            }
        }

        private IEnumerator WaitAndAutoLoad()
        {
            yield return new WaitUntil(() => DataHandler.bLoaded);
            Log.LogInfo("DataHandler loaded, waiting for LoadManager...");
            yield return new WaitForSeconds(0.5f);

            LoadManager lm = null;
            try { lm = MonoSingleton<LoadManager>.Instance; }
            catch (Exception ex) { Log.LogError("Cannot get LoadManager: " + ex.Message); yield break; }
            if (lm == null) { Log.LogError("LoadManager is null"); yield break; }

            List<SaveInfo> saves = lm.GetSaveInfos();
            if (saves == null || saves.Count == 0) { Log.LogWarning("No saves found"); yield break; }

            SaveInfo target = null;
            string name = CfgSaveName.Value;
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < saves.Count; i++)
                    if (saves[i].SaveName == name || saves[i].PlayerName == name)
                    { target = saves[i]; break; }
            }
            if (target == null)
                target = saves.OrderByDescending(s => s.EpochTimeStamp).First();

            Log.LogInfo("Auto-loading: '" + target.SaveName + "' (player: " + target.PlayerName + ")");

            s_autoLoadTriggered = true;
            s_waitingForLoadManager = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (GUILoadMenu.OnLoadSelected == null)
                GUILoadMenu.OnLoadSelected = new LoadSelectedEvent();
            GUILoadMenu.OnLoadSelected.Invoke(target);
        }

        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    }
}