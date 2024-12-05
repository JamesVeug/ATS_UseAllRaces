using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using Eremite;
using Eremite.Controller;
using Eremite.Model;
using Eremite.Services;
using Eremite.Services.Meta;
using Eremite.View.HUD;
using Eremite.View.HUD.Monitors;
using HarmonyLib;
using JLPlugin;
using UnityEngine;
using UnityEngine.Bindings;
using UnityEngine.SceneManagement;

namespace JamesGames;

[HarmonyPatch]
[BepInPlugin(GUID, NAME, VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static int MaxRaces() {
        int max = Configs.MaxRaces;
        if (max == -1)
        {
            return Serviceable.Settings.gameplayRaces;
        }

        if (max == 0)
        {
            return MB.Settings.Races.Length;
        }
        else
        {
            return max;
        }
    }
    
    private const string GUID = "ATS_UseAllRaces";
    private const string NAME = "ATS_UseAllRaces";
    private const string VERSION = "1.0.0";

    public static Plugin Instance;
    public static ManualLogSource Log;
    private static Action OnRealignAlerts;

    private static int DefaultMaxRaces;
    private static Vector2? DefaultAlertsPosition = null;
    private static bool InitializedConfigs = false;
    
    private void Awake()
    {
        Log = Logger;
        Instance = this;
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, GUID);

        Log.LogInfo($"{NAME} v{VERSION} Plugin loaded");
    }

    [HarmonyPatch(typeof(MainController), nameof(MainController.OnServicesReady))]
    [HarmonyPostfix]
    private static void HookMainControllerSetup()
    {
        InitializedConfigs = true;
        DefaultMaxRaces = Serviceable.Settings.gameplayRaces;
        Configs.Initialize(Instance.Config);
        SetRaces();
    }

    public static void SetRaces()
    {
        int max = Configs.MaxRaces;
        if (max == -1)
        {
            Serviceable.Settings.gameplayRaces = MB.Settings.Races.Length;
            return;
        }

        Serviceable.Settings.gameplayRaces = Mathf.Clamp(max, 1, MB.Settings.Races.Length);
    }

    [HarmonyPatch(typeof(RacesHUD), nameof(RacesHUD.SetUpSlots))]
    [HarmonyPostfix]
    private static void RacesHUD_SetUpSlots(RacesHUD __instance)
    {
        // Plugin.Log.LogInfo("RacesHUD.SetUpSlots");
        RealignAlerts(__instance);
        OnRealignAlerts = () => RealignAlerts(__instance);
    }

    [HarmonyPatch(typeof(RacesHUDSlot), nameof(RacesHUDSlot.Enable))]
    [HarmonyPostfix]
    private static void RacesHUDSlot_Enable(RacesHUD __instance)
    {
        // Plugin.Log.LogInfo("RacesHUDSlot_Enable");
        OnRealignAlerts?.Invoke();
    }

    private static void RealignAlerts(RacesHUD hud)
    {
        // Plugin.Log.LogInfo("RealignAlerts");
        RacesHUDSlot lastSlotActive = null;
        for (int i = 0; i < hud.slots.Length; i++)
        {
            RacesHUDSlot slot = hud.slots[i];
            if (slot.gameObject.activeSelf)
            {
                lastSlotActive = slot;
            }
        }
        
        if (lastSlotActive == null)
        {
            // Log.LogWarning("No active slot found");
            return;
        }
        
        MonitorsHUD monitorsHUD = GetMonitorsHUD();
        if (monitorsHUD == null)
        {
            // Log.LogWarning("MonitorsHUD not found");
            return;
        }

        if (DefaultAlertsPosition == null)
        {
            DefaultAlertsPosition = monitorsHUD.transform.localPosition;
        }

        Vector3 position = monitorsHUD.transform.localPosition;
        position.y = lastSlotActive.transform.localPosition.y + lastSlotActive.GetRectSize().y - 20;
        
        if(position.y > DefaultAlertsPosition.Value.y)
        {
            // Log.LogWarning("Alerts position is too low");
            return;
        }
        
        monitorsHUD.transform.localPosition = position;
        // Log.LogInfo("MonitorsHUD position set to " + position);
    }

    private static MonitorsHUD m_MonitorsHUD = null;
    private static MonitorsHUD GetMonitorsHUD()
    {
        if (m_MonitorsHUD == null)
        {
            m_MonitorsHUD = FindObjectOfType<MonitorsHUD>(true);
        }

        return m_MonitorsHUD;
    }

    [HarmonyPatch(typeof(RacesHUD), nameof(RacesHUD.SetUp))]
    [HarmonyPrefix]
    private static bool RacesHUD_SetUp(RacesHUD __instance)
    {
        while (__instance.slots.Length < GameMB.RacesService.Races.Length)
        {
            int i = __instance.slots.Length - 1;
            RacesHUDSlot slot = __instance.slots[i];
            RacesHUDSlot racesHUDSlot = Instantiate(slot, slot.transform.parent);
            racesHUDSlot.transform.localPosition += slot.transform.localPosition - __instance.slots[i - 1].transform.localPosition;
            racesHUDSlot.gameObject.name = $"RaceHUDSlot ({i+1})";

            __instance.slots = __instance.slots.ForceAdd(racesHUDSlot);
        }

        return true;
    }

    [HarmonyPatch(typeof(CaravanGenerator), nameof(CaravanGenerator.GetRandomRaces))]
    [HarmonyPrefix]
    private static bool CaravanGenerator_DefineNewSettlementRaces(CaravanGenerator __instance, ref List<string> __result)
    {
        // Plugin.Log.LogInfo("CaravanGenerator.GetRandomRaces");
        if (!InitializedConfigs)
        {
            // Plugin.Log.LogWarning("Not initialized");
            return true;
        }
        // return (from r in Serviceable.Settings.Races.ShuffleToNew(rng).Where(Serviceable.MetaConditionsService.IsUnlocked).Take(Serviceable.Settings.gameplayRaces)
        // orderby r.order
        // select r.Name).ToList();

        // Get all races to choose from
        var allRaces = Serviceable.Settings.Races.ShuffleToNew(__instance.rng).Where(Serviceable.MetaConditionsService.IsUnlocked).ToList();
        allRaces.RemoveAll(a => Configs.RaceState(a.name) == Configs.State.Never);
        
        // Add races we always want
        var chosenRaces = allRaces.Where(a=> Configs.RaceState(a.name) == Configs.State.Always).ToList();
        
        // Add more if we need more
        int extras = MaxRaces() - chosenRaces.Count;
        if (extras > 0)
        {
            var optionalRaces = allRaces.Where(a => Configs.RaceState(a.name) == Configs.State.Optional).ToList();
            chosenRaces.AddRange(optionalRaces.Take(extras));
        }
        
        __result = chosenRaces.Select(a => a.name).ToList();
        return false;
    }
}