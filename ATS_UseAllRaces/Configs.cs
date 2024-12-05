
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Eremite;
using Eremite.Model;
using JamesGames;

namespace JLPlugin
{
    internal static class Configs
    {
        public enum State
        {
            Never,
            Optional,
            Always
        }
        
        public static int MaxRaces => maxRaces.Value;
        
        private static ConfigEntry<int> maxRaces;
        private static List<ConfigEntry<State>> raceConfigs = new List<ConfigEntry<State>>();

        public static void Initialize(ConfigFile config)
        {
            Plugin.Log.LogInfo($"Initialize");
            maxRaces = config.Bind("General", "Max Races", -1,
                $"Maximum amount of races settlements can have. -1 is use all the races.");

            raceConfigs.Clear();
            foreach (RaceModel race in SO.Settings.Races)
            {
                Plugin.Log.LogInfo($"Adding config for " + race.name);
                GetConfig(config, race, State.Always);
            }

            config.ConfigReloaded += (sender, args) => { Plugin.Log.LogInfo($"Config reloaded " + sender + " " + args); };
            config.SettingChanged += (sender, args) =>
            {
                Plugin.Log.LogInfo($"Setting changed " + sender + " " + args);
                if (maxRaces.Equals(sender))
                {
                    Plugin.Log.LogInfo($"Max Race Setting changed to " + maxRaces.Value);
                    Plugin.SetRaces();
                }
                else if (raceConfigs.Contains(sender as ConfigEntry<State>))
                {
                    Plugin.Log.LogInfo($"Race type changed");
                }
            };
        }

        private static void GetConfig(ConfigFile config, RaceModel race, State defaultState)
        {
            var entry = config.Bind("Races", race.name, defaultState, "How frequent should this race be chosen in new races.");
            raceConfigs.Add(entry);
        }

        public static State RaceState(string raceName)
        {
            foreach (ConfigEntry<State> entry in raceConfigs)
            {
                if (entry.Definition.Key == raceName)
                {
                    return entry.Value;
                }
            }
            
            Plugin.Log.LogWarning($"{raceName} not found in configs. Using Optional instead.\n" + Environment.StackTrace);
            
            return State.Optional;
        }
    }
}