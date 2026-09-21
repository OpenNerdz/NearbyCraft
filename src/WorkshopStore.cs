using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NearbyCraft
{
    internal sealed class WorkshopTarget
    {
        public string Item;
        public int Target = 100;
        public bool Enabled = true;
        // One-shot orders count newly queued products, not existing stock.
        public bool Once;
        public int Remaining;
        public bool TrackDelivery;
        public int Queued, Returned;
        public long CompletedUtcTicks;
    }

    internal sealed class WorkshopCompletion
    {
        public string Item;
        public int Requested, Produced;
        public long UtcTicks;
        public bool VerifiedDelivery;
    }

    internal sealed class WorkshopControllerData
    {
        public int X, Y, Z;
        public bool Enabled;
        public bool Linked;
        public int ConsoleX, ConsoleY, ConsoleZ;
        public List<WorkshopTarget> Targets = new List<WorkshopTarget>();
        public List<WorkshopCompletion> Completed = new List<WorkshopCompletion>();
        public bool AutoFuel = true;
        public bool AutoCraft = true;
        public List<string> ExcludedStations = new List<string>();
        // Reservations for future native forge batches, never virtual inventory.
        // Saved so a reload does not forget what raw material is already smelting for.
        public List<WorkshopSmeltAssignment> Smelting = new List<WorkshopSmeltAssignment>();
        [JsonIgnore] internal Vector3i Position { get { return new Vector3i(X, Y, Z); } }
        [JsonIgnore] internal Vector3i Console { get { return new Vector3i(ConsoleX, ConsoleY, ConsoleZ); } }
    }

    internal sealed class WorkshopSmeltAssignment
    {
        public int X, Y, Z;
        public string Item, Owner, RecipeKey;
        public int Batches, Count;
        [JsonIgnore] internal Vector3i Position { get { return new Vector3i(X, Y, Z); } }
    }

    internal sealed class WorkshopFile
    {
        public int Format = 1;
        public Dictionary<string, List<WorkshopControllerData>> Worlds = new Dictionary<string, List<WorkshopControllerData>>();
    }

    internal static class WorkshopStore
    {
        private static string path;
        private static WorkshopFile data = new WorkshopFile();
        internal static bool Writable { get; private set; }

        internal static void Initialize(string modPath)
        {
            path = Path.Combine(modPath, "workshops.json");
            try
            {
                data = File.Exists(path) ? JsonConvert.DeserializeObject<WorkshopFile>(File.ReadAllText(path)) : new WorkshopFile();
                if (data == null || data.Format != 1 || data.Worlds == null)
                    throw new InvalidDataException("Unsupported workshop settings format");
                foreach (var world in data.Worlds.Values)
                {
                    if (world == null || world.Count > 128) throw new InvalidDataException("Invalid workshop list");
                    var positions = new HashSet<string>();
                    foreach (var controller in world)
                    {
                        if (controller == null || controller.Targets == null || controller.Targets.Count > WorkshopRules.MaximumTargets
                            || !positions.Add(controller.X + ":" + controller.Y + ":" + controller.Z))
                            throw new InvalidDataException("Invalid controller settings");
                        var items = new HashSet<string>();
                        if (controller.Completed == null) controller.Completed = new List<WorkshopCompletion>();
                        if (controller.Completed.Count > WorkshopRules.MaximumHistory) throw new InvalidDataException("Too much completion history");
                        foreach (var entry in controller.Completed)
                            if (entry == null || string.IsNullOrEmpty(entry.Item) || entry.Requested < 1
                                || entry.Requested > WorkshopRules.MaximumTarget || entry.Produced < 0
                                || entry.UtcTicks < DateTime.MinValue.Ticks || entry.UtcTicks > DateTime.MaxValue.Ticks)
                                throw new InvalidDataException("Invalid completion history");
                        if (controller.ExcludedStations == null) controller.ExcludedStations = new List<string>();
                        if (controller.ExcludedStations.Count > 256) throw new InvalidDataException("Invalid excluded station list");
                        if (controller.Smelting == null) controller.Smelting = new List<WorkshopSmeltAssignment>();
                        if (controller.Smelting.Count > WorkshopRules.MaximumStations) throw new InvalidDataException("Too many smelting assignments");
                        var assigned = new HashSet<string>();
                        foreach (var job in controller.Smelting)
                            if (job == null || string.IsNullOrEmpty(job.Item) || string.IsNullOrEmpty(job.Owner)
                                || string.IsNullOrEmpty(job.RecipeKey) || job.RecipeKey.Length > 4096
                                || job.Batches < 1 || job.Batches > WorkshopRules.BatchLimit || job.Count < 1
                                || job.Count > WorkshopRules.MaximumTarget || !assigned.Add(job.X + ":" + job.Y + ":" + job.Z))
                                throw new InvalidDataException("Invalid smelting assignment");
                        foreach (var target in controller.Targets)
                        {
                            if (target == null || string.IsNullOrEmpty(target.Item) || !items.Add(target.Item))
                                throw new InvalidDataException("Invalid production target");
                            target.Target = WorkshopRules.ClampTarget(target.Target);
                            target.Remaining = Math.Max(0, Math.Min(WorkshopRules.MaximumTarget, target.Remaining));
                            if (target.Queued < 0 || target.Returned < 0 || target.Returned > target.Queued
                                || target.CompletedUtcTicks < 0 || target.CompletedUtcTicks > DateTime.MaxValue.Ticks)
                                throw new InvalidDataException("Invalid delivery progress");
                        }
                    }
                }
                Writable = true;
            }
            catch (Exception e)
            {
                data = new WorkshopFile();
                Writable = false;
                Log.Error("[NearbyCraft] Workshop automation disabled; workshops.json was not overwritten: {0}", e.Message);
            }
        }

        internal static List<WorkshopControllerData> Controllers
        {
            get
            {
                string key = GamePrefs.GetString(EnumGamePrefs.GameWorld) + "\n" + GamePrefs.GetString(EnumGamePrefs.GameName);
                List<WorkshopControllerData> controllers;
                if (!data.Worlds.TryGetValue(key, out controllers))
                    data.Worlds[key] = controllers = new List<WorkshopControllerData>();
                return controllers;
            }
        }

        internal static WorkshopControllerData Get(Vector3i position)
        {
            return Controllers.Find(c => c.Position == position);
        }

        internal static bool CreditDelivery(WorkshopControllerData controller, string item, int count)
        {
            var target = controller.Targets.Find(t => t.Item == item && t.Once && t.TrackDelivery && t.CompletedUtcTicks == 0);
            if (target == null || count <= 0) return false;
            int credited = Math.Min(count, target.Queued - target.Returned);
            target.Returned += credited;
            return credited > 0;
        }

        internal static void RecordCompletion(WorkshopControllerData controller, WorkshopTarget target, bool verified)
        {
            if (target.CompletedUtcTicks != 0) return;
            target.CompletedUtcTicks = DateTime.UtcNow.Ticks;
            controller.Completed.Insert(0, new WorkshopCompletion { Item = target.Item, Requested = target.Target,
                Produced = target.Queued, UtcTicks = target.CompletedUtcTicks, VerifiedDelivery = verified });
            if (controller.Completed.Count > WorkshopRules.MaximumHistory)
                controller.Completed.RemoveRange(WorkshopRules.MaximumHistory, controller.Completed.Count - WorkshopRules.MaximumHistory);
        }

        internal static bool Edit(Vector3i position, Action<WorkshopControllerData> edit, out string error)
        {
            error = null;
            if (!Writable) { error = "Workshop settings could not be read. Check workshops.json and the game log."; return false; }
            var list = Controllers;
            var previous = Get(position);
            if (previous == null && list.Count >= 128) { error = "Workshop controller limit reached."; return false; }
            var changed = previous == null
                ? new WorkshopControllerData { X = position.x, Y = position.y, Z = position.z }
                : JsonConvert.DeserializeObject<WorkshopControllerData>(JsonConvert.SerializeObject(previous));
            edit(changed);
            if (previous != null) list.Remove(previous);
            list.Add(changed);
            if (Save(out error)) return true;
            list.Remove(changed);
            if (previous != null) list.Add(previous);
            return false;
        }

        internal static bool Remove(Vector3i position)
        {
            var list = Controllers;
            var controller = Get(position);
            if (controller == null) return true;
            int index = list.IndexOf(controller);
            list.RemoveAt(index);
            string error;
            if (Save(out error)) return true;
            list.Insert(index, controller);
            return false;
        }

        // Called AFTER a native queue commits. Rolling memory back would submit
        // paid-for work twice. A disk error stops automation for this session.
        internal static bool SaveProgress(out string error)
        {
            if (Save(out error)) return true;
            Writable = false;
            foreach (var controller in Controllers) controller.Enabled = false;
            return false;
        }

        private static bool Save(out string error)
        {
            error = null;
            if (!Writable) { error = "Workshop settings are read-only after a load error."; return false; }
            try
            {
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception e)
            {
                error = "Could not save workshop settings; the change was cancelled.";
                Log.Error("[NearbyCraft] Workshop save failed: {0}", e.Message);
                return false;
            }
        }
    }
}
