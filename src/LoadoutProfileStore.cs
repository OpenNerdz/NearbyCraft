using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NearbyCraft
{
    internal sealed class SerializedLoadoutItem
    {
        public string Name;
        public string Data;
    }

    internal sealed class LoadoutProfileData
    {
        public int Format = 1;
        public SerializedLoadoutItem[] Equipment;
        public SerializedLoadoutItem[] Toolbelt;
        public SerializedLoadoutItem[] Backpack;
        public bool[] BackpackLocks;
        public string SavedUtc;
    }

    internal sealed class LoadoutProfileFile
    {
        public int Format = 1;
        public Dictionary<string, LoadoutProfileData[]> Worlds = new Dictionary<string, LoadoutProfileData[]>();
    }

    internal static class LoadoutProfileStore
    {
        internal const int ProfileCount = 4;
        private static string path;
        private static LoadoutProfileFile data = new LoadoutProfileFile();

        internal static void Initialize(string modPath)
        {
            path = Path.Combine(modPath, "loadouts.json");
            try
            {
                data = File.Exists(path)
                    ? JsonConvert.DeserializeObject<LoadoutProfileFile>(File.ReadAllText(path)) ?? new LoadoutProfileFile()
                    : new LoadoutProfileFile();
                if (data.Worlds == null) data.Worlds = new Dictionary<string, LoadoutProfileData[]>();
            }
            catch (Exception exception)
            {
                data = new LoadoutProfileFile();
                Log.Warning("[NearbyCraft] Could not read loadouts.json; profiles start empty. {0}", exception.Message);
            }
        }

        internal static LoadoutProfileData Get(int index)
        {
            LoadoutProfileData[] profiles = GetProfiles(false);
            return profiles == null || index < 0 || index >= profiles.Length ? null : profiles[index];
        }

        internal static bool Save(int index, LoadoutProfileData profile, out string error)
        {
            error = null;
            if (index < 0 || index >= ProfileCount || profile == null)
            {
                error = "Invalid loadout profile.";
                return false;
            }

            LoadoutProfileData[] profiles = GetProfiles(true);
            LoadoutProfileData previous = profiles[index];
            profiles[index] = profile;
            try
            {
                string temporaryPath = path + ".tmp";
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporaryPath, path, path + ".bak");
                else File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception exception)
            {
                profiles[index] = previous;
                error = "The profile could not be written to loadouts.json.";
                Log.Warning("[NearbyCraft] Could not save a loadout profile: {0}", exception.Message);
                return false;
            }
        }

        internal static SerializedLoadoutItem Serialize(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty() || stack.count <= 0) return null;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                stack.Write(writer);
                writer.Flush();
                return new SerializedLoadoutItem
                {
                    Name = stack.itemValue.ItemClassOrMissing.GetItemName(),
                    Data = Convert.ToBase64String(stream.ToArray())
                };
            }
        }

        internal static bool TryDeserialize(SerializedLoadoutItem saved, out ItemStack stack)
        {
            stack = ItemStack.Empty.Clone();
            if (saved == null || string.IsNullOrEmpty(saved.Data)) return true;
            try
            {
                byte[] bytes = Convert.FromBase64String(saved.Data);
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream))
                {
                    ItemStack loaded = new ItemStack().Read(reader);
                    if (loaded == null || loaded.IsEmpty() || loaded.count <= 0
                        || !string.Equals(saved.Name, loaded.itemValue.ItemClassOrMissing.GetItemName(), StringComparison.Ordinal))
                    {
                        return false;
                    }
                    stack = loaded;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] A saved loadout item could not be read safely: {0}", exception.Message);
                return false;
            }
        }

        private static LoadoutProfileData[] GetProfiles(bool create)
        {
            string key = CurrentWorldKey();
            LoadoutProfileData[] profiles;
            if (data.Worlds.TryGetValue(key, out profiles) && profiles != null && profiles.Length == ProfileCount)
                return profiles;
            if (!create) return null;
            profiles = new LoadoutProfileData[ProfileCount];
            data.Worlds[key] = profiles;
            return profiles;
        }

        private static string CurrentWorldKey()
        {
            string world = GamePrefs.GetString(EnumGamePrefs.GameWorld) ?? string.Empty;
            string game = GamePrefs.GetString(EnumGamePrefs.GameName) ?? string.Empty;
            return world + "\n" + game;
        }
    }
}
