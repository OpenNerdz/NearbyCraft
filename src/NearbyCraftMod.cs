using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace NearbyCraft
{
    public sealed class NearbyCraftMod : IModApi
    {
        internal const string ModName = "NearbyCraft";
        internal const string ModVersion = "1.2.0";

        internal static NearbyCraftConfig Config { get; private set; }
        private static string configPath;

        public void InitMod(Mod mod)
        {
            Config = LoadConfig(mod.Path);
            StorageIndex.Configure(Config);

            try
            {
                WarnAboutConflicts();
                var harmony = new Harmony("bradhosk.nearbycraft");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Out("[NearbyCraft] v{0} loaded for {1}. Craft range: {2}; terminal range: {3}; snapshot cache: {4} ms.",
                    ModVersion, Constants.cVersionInformation.LongString, Config.Range, Config.TerminalRange, Config.CacheMilliseconds);
            }
            catch (Exception exception)
            {
                Log.Error("[NearbyCraft] Harmony patches could not be applied: {0}", exception);
            }
        }

        private static NearbyCraftConfig LoadConfig(string modPath)
        {
            string path = Path.Combine(modPath, "config.json");
            configPath = path;
            var defaults = new NearbyCraftConfig();

            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                    return defaults;
                }

                var loaded = JsonConvert.DeserializeObject<NearbyCraftConfig>(File.ReadAllText(path)) ?? defaults;
                loaded.Validate();
                return loaded;
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Could not read config.json; using safe defaults. {0}", exception.Message);
                return defaults;
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            if (Config == null || Config.Enabled == enabled)
            {
                return;
            }

            Config.Enabled = enabled;
            StorageIndex.Configure(Config);
            SaveConfig();
        }

        internal static void SetTerminalSort(StorageTerminalSort sort)
        {
            string value = sort.ToString();
            if (Config == null || string.Equals(Config.TerminalSort, value, StringComparison.Ordinal))
            {
                return;
            }

            Config.TerminalSort = value;
            SaveConfig();
        }

        internal static void SetTerminalAutoFocus(bool enabled)
        {
            if (Config == null || Config.TerminalAutoFocusSearch == enabled)
            {
                return;
            }

            Config.TerminalAutoFocusSearch = enabled;
            SaveConfig();
        }

        private static void SaveConfig()
        {
            try
            {
                if (!string.IsNullOrEmpty(configPath))
                {
                    File.WriteAllText(configPath, JsonConvert.SerializeObject(Config, Formatting.Indented));
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] A UI setting changed for this session but could not be saved: {0}", exception.Message);
            }
        }

        private static void WarnAboutConflicts()
        {
            string[] conflicts = { "BeyondStorage", "ProxiCraft", "CraftFromContainers", "CraftFromContainerPlus" };
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                string name = assemblies[i].GetName().Name ?? string.Empty;
                for (int j = 0; j < conflicts.Length; j++)
                {
                    if (name.IndexOf(conflicts[j], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log.Warning("[NearbyCraft] '{0}' provides similar behavior. Remove one of the mods to prevent duplicate counts or consumption.", name);
                        return;
                    }
                }
            }
        }
    }
}
