// CourierBugfix v1.1.0 — Defensive patches for courier delivery jobs
// 
// ROOT CAUSE of courier delivery bug:
//   RUS_CoreForgeLabs mod jobitems.json had WRONG strLootPickup values
//   (e.g. "ItmElectricalBox01New" instead of "ItmElectricalBox01Loose").
//   These loot names dont exist in game -> items never spawn -> job fails.
//   FIX: Corrected jobitems.json to use original game loot/CT references.
//
// Defensive patches (prevent future issues):
//  1. GetCondTrigger  — Clone before returning (prevents shared state corruption)
//  2. TurnInJob       — Fresh TXTJobItemsTemplate each call (prevents template corruption)
//  3. GetItemQualityList — Show CT name instead of empty "{}" (better error display)

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Ostranauts.Core.Models;
using UnityEngine;

namespace CourierBugfix
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class CourierBugfixPlugin : BaseUnityPlugin
    {
        public const string GUID    = "com.coreforgelabs.courierbugfix";
        public const string NAME    = "CourierBugfix";
        public const string VERSION = "1.1.0";

        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            var harmony = new Harmony(GUID);
            harmony.PatchAll();
            Log.LogInfo(NAME + " v" + VERSION + " loaded — " +
                harmony.GetPatchedMethods().Count() + " patches applied");
        }
    }

    // PATCH 1: DataHandler.GetCondTrigger — clone on return
    // Original returns shared references from dictCTs.
    // Callers do condTrigger.fCount *= num3, corrupting shared state.
    [HarmonyPatch(typeof(DataHandler), "GetCondTrigger")]
    static class Patch_GetCondTrigger_Clone
    {
        static bool Prefix(string strName, ref CondTrigger __result)
        {
            CondTrigger value;
            if (strName != null && DataHandler.dictCTs.TryGetValue(strName, out value))
            {
                __result = value.Clone();
                return false;
            }
            if (!string.IsNullOrEmpty(strName))
                Debug.Log("No such CT: " + strName);

            CondTrigger blank;
            if (DataHandler.dictCTs.TryGetValue("Blank", out blank))
                __result = blank.Clone();
            else
                __result = new CondTrigger();
            return false;
        }
    }

    // PATCH 2: GigManager.TurnInJob — fresh TXTJobItemsTemplate
    // Original mutates shared Loot "TXTJobItemsTemplate" in-place.
    [HarmonyPatch(typeof(GigManager), "TurnInJob")]
    static class Patch_TurnInJob_Fix
    {
        static void Prefix(JsonJobSave jjs, CondOwner coTaker, CondOwner coKiosk)
        {
            if (jjs == null || coKiosk == null) return;
            if (jjs.strRegIDDropoff != null && jjs.strJobItems != null)
            {
                var fresh = new Loot();
                fresh.strType = "text";
                DataHandler.dictLoot["TXTJobItemsTemplate"] = fresh;
            }
        }
    }

    // PATCH 3: GUITooltip.GetItemQualityList — show CT name when empty
    // Blank CTs have RulesInfo="" -> shows "{}". Fix: show "{CTName}".
    [HarmonyPatch(typeof(GUITooltip), "GetItemQualityList",
        new Type[] { typeof(List<CondTrigger>) })]
    static class Patch_GetItemQualityList_ShowName
    {
        static void Postfix(List<CondTrigger> aCTs, ref List<string> __result)
        {
            if (aCTs == null || __result == null) return;
            for (int i = 0; i < __result.Count; i++)
            {
                string s = __result[i];
                if (s == "{}" || s.StartsWith("{} "))
                {
                    string ctName = "???";
                    if (i < aCTs.Count && aCTs[i] != null)
                        ctName = aCTs[i].strName ?? ctName;
                    __result[i] = s.Replace("{}", "{" + ctName + "}");
                }
            }
        }
    }
}