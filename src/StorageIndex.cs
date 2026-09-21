using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;

namespace NearbyCraft
{
    internal static class StorageIndex
    {
        private enum SourceKind : byte
        {
            PlayerStorage,
            WorkstationOutput,
            Collector,
            Vehicle,
            Drone
        }

        private struct StorageSource
        {
            internal SourceKind Kind;
            internal object Owner;
            internal ItemStack[] Slots;
            internal PackedBoolArray LockedSlots;
            internal float DistanceSquared;
        }

        private sealed class DistanceComparer : IComparer<StorageSource>
        {
            internal static readonly DistanceComparer Instance = new DistanceComparer();

            public int Compare(StorageSource left, StorageSource right)
            {
                return left.DistanceSquared.CompareTo(right.DistanceSquared);
            }
        }

        private static readonly List<StorageSource> Sources = new List<StorageSource>(32);
        private static readonly Dictionary<int, int> ItemCounts = new Dictionary<int, int>(128);
        private static readonly List<Entity> NearbyVehicles = new List<Entity>(8);
        private static readonly List<Entity> NearbyDrones = new List<Entity>(4);

        private static NearbyCraftConfig config = new NearbyCraftConfig();
        private static float expiresAt;
        private static Vector3 lastPlayerPosition;
        private static int lastPlayerId = -1;
        private static bool rebuilding;
        private static bool Enabled { get { return config.Enabled && NearbyCraftMod.CanUseLocalStorage; } }

        internal static void Configure(NearbyCraftConfig value)
        {
            config = value ?? new NearbyCraftConfig();
            config.Validate();
            Invalidate();
        }

        internal static void Invalidate()
        {
            expiresAt = -1f;
        }

        internal static int GetCount(ItemValue itemValue)
        {
            if (!CanUse(itemValue))
            {
                return 0;
            }

            EnsureFresh(false);
            int count;
            return ItemCounts.TryGetValue(itemValue.type, out count) ? count : 0;
        }

        internal static List<ItemStack> AppendAvailableStacks(List<ItemStack> destination)
        {
            if (destination == null || !Enabled)
            {
                return destination;
            }

            EnsureFresh(false);
            for (int sourceIndex = 0; sourceIndex < Sources.Count; sourceIndex++)
            {
                StorageSource source = Sources[sourceIndex];
                ItemStack[] slots = source.Slots;
                if (slots == null)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    ItemStack stack = slots[slotIndex];
                    if (IsConsumable(stack) && !IsSlotLocked(source, slotIndex))
                    {
                        destination.Add(stack);
                    }
                }
            }

            return destination;
        }

        internal static ItemStack[] AppendAvailableStacks(ItemStack[] existing)
        {
            if (!Enabled)
            {
                return existing;
            }

            EnsureFresh(false);
            if (Sources.Count == 0)
            {
                return existing;
            }

            int extraCapacity = 0;
            for (int sourceIndex = 0; sourceIndex < Sources.Count; sourceIndex++)
            {
                ItemStack[] slots = Sources[sourceIndex].Slots;
                extraCapacity += slots == null ? 0 : slots.Length;
            }

            var combined = new List<ItemStack>((existing == null ? 0 : existing.Length) + extraCapacity);
            if (existing != null)
            {
                combined.AddRange(existing);
            }
            AppendAvailableStacks(combined);
            return combined.ToArray();
        }

        internal static bool HasItems(XUiM_PlayerInventory inventory, IList<ItemStack> required, int multiplier)
        {
            if (inventory == null || required == null)
            {
                return false;
            }

            if (!Enabled)
            {
                return inventory.HasItems(required, multiplier);
            }

            EnsureFresh(false);
            var plan = BeginCraftPlan(ItemStack.Clone(inventory.GetBackpackItemStacks()),
                ItemStack.Clone(inventory.GetToolbeltItemStacks()));
            return CanConsume(plan.Plan, required, multiplier);
        }

        internal static bool HasItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> required, int multiplier)
        {
            if (grid == null || required == null)
            {
                return false;
            }

            if (!Enabled)
            {
                return grid.HasItems(required, multiplier);
            }

            EnsureFresh(false);
            var plan = BeginCraftPlan(ItemStack.Clone(grid.GetSlots()));
            return CanConsume(plan.Plan, required, multiplier);
        }

        internal static void RemoveItems(XUiM_PlayerInventory inventory, IList<ItemStack> required, int multiplier, IList<ItemStack> removedItems)
        {
            if (!Enabled)
            {
                inventory.RemoveItems(required, multiplier, removedItems);
                return;
            }

            EnsureFresh(false);
            ItemStack[] backpackBefore = ItemStack.Clone(inventory.GetBackpackItemStacks());
            ItemStack[] toolbeltBefore = ItemStack.Clone(inventory.GetToolbeltItemStacks());
            var craft = BeginCraftPlan(ItemStack.Clone(backpackBefore), ItemStack.Clone(toolbeltBefore));
            if (!CanConsume(craft.Plan, required, multiplier))
            {
                Log.Error("[NearbyCraft] Craft payment changed before removal; no inventory was modified.");
                return;
            }

            ItemStack[] backpackAfter = craft.Plan.GetAfter(0);
            ItemStack[] toolbeltAfter = craft.Plan.GetAfter(1);
            if (!SameSlots(inventory.GetBackpackItemStacks(), backpackBefore)
                || !SameSlots(inventory.GetToolbeltItemStacks(), toolbeltBefore)
                || !CanCommitCraftPlan(craft))
            {
                Log.Error("[NearbyCraft] Player inventory changed before craft payment; no inventory was modified.");
                return;
            }

            try
            {
                inventory.SetBackpackItemStacks(ItemStack.Clone(backpackAfter));
                inventory.SetToolbeltItemStacks(ItemStack.Clone(toolbeltAfter));
            }
            catch (Exception exception)
            {
                RestorePlayerInventory(inventory, backpackBefore, toolbeltBefore);
                Log.Error("[NearbyCraft] Could not stage craft payment; player inventory was restored: {0}", exception);
                return;
            }

            if (!CommitCraftPlan(craft))
            {
                RestorePlayerInventory(inventory, backpackBefore, toolbeltBefore);
                Log.Error("[NearbyCraft] Storage changed before craft payment; player inventory was restored.");
                return;
            }
            AppendRemoved(craft.Plan, removedItems);
        }

        internal static void RemoveItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> required, int multiplier, IList<ItemStack> removedItems)
        {
            if (!Enabled)
            {
                grid.RemoveItems(required, multiplier, removedItems);
                return;
            }

            EnsureFresh(false);
            ItemStack[] inputBefore = ItemStack.Clone(grid.GetSlots());
            var craft = BeginCraftPlan(ItemStack.Clone(inputBefore));
            if (!CanConsume(craft.Plan, required, multiplier))
            {
                Log.Error("[NearbyCraft] Workstation craft payment changed before removal; no inventory was modified.");
                return;
            }

            ItemStack[] inputAfter = craft.Plan.GetAfter(0);
            if (!SameSlots(grid.GetSlots(), inputBefore) || !CanCommitCraftPlan(craft))
            {
                Log.Error("[NearbyCraft] Workstation input changed before craft payment; no inventory was modified.");
                return;
            }
            try { grid.SetStacks(ItemStack.Clone(inputAfter)); }
            catch (Exception exception)
            {
                TryRestoreGrid(grid, inputBefore);
                Log.Error("[NearbyCraft] Could not stage workstation craft payment; inputs were restored: {0}", exception);
                return;
            }
            if (!CommitCraftPlan(craft))
            {
                TryRestoreGrid(grid, inputBefore);
                Log.Error("[NearbyCraft] Storage changed before workstation craft payment; inputs were restored.");
                return;
            }
            AppendRemoved(craft.Plan, removedItems);
        }

        private sealed class CraftPlan
        {
            internal readonly StorageTransferPlan Plan = new StorageTransferPlan();
            internal readonly List<StorageSource> Storage = new List<StorageSource>();
            internal readonly List<bool[]> Locks = new List<bool[]>();
            internal int LocalInventories;
        }

        private static CraftPlan BeginCraftPlan(params ItemStack[][] localInventories)
        {
            var craft = new CraftPlan { LocalInventories = localInventories.Length };
            for (int i = 0; i < localInventories.Length; i++)
                craft.Plan.Add(localInventories[i], new bool[localInventories[i].Length]);
            for (int sourceIndex = 0; sourceIndex < Sources.Count; sourceIndex++)
            {
                StorageSource source = Sources[sourceIndex];
                if (!SourceStillValid(source)) continue;
                var locks = new bool[source.Slots.Length];
                for (int i = 0; i < locks.Length; i++) locks[i] = IsSlotLocked(source, i);
                craft.Storage.Add(source);
                craft.Locks.Add(locks);
                craft.Plan.Add(source.Slots, locks);
            }
            return craft;
        }

        private static bool CanConsume(StorageTransferPlan plan, IList<ItemStack> required, int multiplier)
        {
            foreach (ItemStack needed in AggregateRequirements(required, multiplier))
                if (plan.Withdraw(needed, needed.count) != needed.count) return false;
            return true;
        }

        private static List<ItemStack> AggregateRequirements(IList<ItemStack> required, int multiplier)
        {
            var result = new List<ItemStack>();
            if (required == null) return result;
            for (int i = 0; i < required.Count; i++)
            {
                ItemStack source = required[i];
                if (!IsRequirement(source)) continue;
                int count = SafeRequiredCount(source.count, multiplier);
                ItemStack existing = result.Find(s => StorageTransferPlan.Matches(s, source));
                if (existing == null)
                {
                    existing = source.Clone();
                    existing.count = count;
                    result.Add(existing);
                }
                else existing.count = existing.count > int.MaxValue - count
                    ? int.MaxValue
                    : existing.count + count;
            }
            return result;
        }

        private static bool CommitCraftPlan(CraftPlan craft)
        {
            bool committed;
            try { committed = craft.Plan.TryCommit(index => CraftInventoryStillValid(craft, index)); }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Craft payment validation failed safely: {0}", exception.Message);
                return false;
            }
            if (!committed) return false;
            for (int i = 0; i < craft.Storage.Count; i++)
            {
                if (!craft.Plan.Changed(craft.LocalInventories + i)) continue;
                try { MarkModified(craft.Storage[i]); }
                catch (Exception exception)
                {
                    Log.Error("[NearbyCraft] Craft payment committed but notification failed: {0}", exception);
                }
            }
            Invalidate();
            return true;
        }

        private static bool CanCommitCraftPlan(CraftPlan craft)
        {
            try { return craft.Plan.CanCommit(index => CraftInventoryStillValid(craft, index)); }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Craft payment prevalidation failed safely: {0}", exception.Message);
                return false;
            }
        }

        private static bool CraftInventoryStillValid(CraftPlan craft, int index)
        {
            if (index < craft.LocalInventories) return true; // Native setters are validated separately against live player/UI slots.
            int sourceIndex = index - craft.LocalInventories;
            StorageSource source = craft.Storage[sourceIndex];
            if (!SourceStillValid(source)) return false;
            bool[] locks = craft.Locks[sourceIndex];
            for (int i = 0; i < locks.Length; i++) if (IsSlotLocked(source, i) != locks[i]) return false;
            return true;
        }

        private static void AppendRemoved(StorageTransferPlan plan, IList<ItemStack> removedItems)
        {
            if (removedItems == null) return;
            for (int inventory = 0; inventory < plan.InventoryCount; inventory++)
            {
                ItemStack[] before = plan.GetBefore(inventory), after = plan.GetAfter(inventory);
                for (int slot = 0; slot < before.Length; slot++)
                {
                    ItemStack previous = before[slot];
                    if (previous == null || previous.IsEmpty()) continue;
                    int remaining = after[slot] != null && !after[slot].IsEmpty()
                        && StorageTransferPlan.Matches(previous, after[slot]) ? after[slot].count : 0;
                    int removed = previous.count - remaining;
                    if (removed > 0) removedItems.Add(new ItemStack(previous.itemValue.Clone(), removed));
                }
            }
        }

        private static void RestorePlayerInventory(XUiM_PlayerInventory inventory, ItemStack[] backpack, ItemStack[] toolbelt)
        {
            try
            {
                inventory.SetBackpackItemStacks(ItemStack.Clone(backpack));
                inventory.SetToolbeltItemStacks(ItemStack.Clone(toolbelt));
            }
            catch (Exception exception) { Log.Error("[NearbyCraft] Player inventory rollback failed: {0}", exception); }
        }

        private static void TryRestoreGrid(XUiC_WorkstationInputGrid grid, ItemStack[] slots)
        {
            try { grid.SetStacks(ItemStack.Clone(slots)); }
            catch (Exception exception) { Log.Error("[NearbyCraft] Workstation input rollback failed: {0}", exception); }
        }

        private static bool SameSlots(ItemStack[] left, ItemStack[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (!StorageTransferPlan.ExactEquals(left[i], right[i])) return false;
            return true;
        }

        private static ItemStack[] CurrentSlots(StorageSource source)
        {
            switch (source.Kind)
            {
                case SourceKind.WorkstationOutput:
                    var workstation = source.Owner as TileEntityWorkstation;
                    return workstation == null ? null : workstation.Output;
                case SourceKind.Collector:
                    var collector = source.Owner as TileEntityCollector;
                    return collector == null ? null : collector.Items;
                case SourceKind.Vehicle:
                    var vehicle = source.Owner as EntityVehicle;
                    return vehicle == null || vehicle.bag == null ? null : vehicle.bag.GetSlots();
                case SourceKind.Drone:
                    var drone = source.Owner as EntityDrone;
                    return drone == null || drone.bag == null ? null : drone.bag.GetSlots();
                default:
                    var tile = source.Owner as TileEntity;
                    ITileEntityLootable storage;
                    return tile != null && tile.TryGetSelfOrFeature<ITileEntityLootable>(out storage) ? storage.items : null;
            }
        }

        private static bool SourceStillValid(StorageSource source)
        {
            try
            {
                if (source.Slots == null || !ReferenceEquals(source.Slots, CurrentSlots(source))) return false;
                var tile = source.Owner as TileEntity;
                if (tile != null)
                {
                    World world = GameManager.Instance == null ? null : GameManager.Instance.World;
                    return world != null && !tile.IsRemoving && !tile.IsUserAccessing()
                        && world.GetTileEntity(tile.ToWorldPos()) == tile && CanAccess(tile);
                }
                var vehicle = source.Owner as EntityVehicle;
                if (vehicle != null) return vehicle.bag != null && CanAccess(vehicle);
                var drone = source.Owner as EntityDrone;
                return drone != null && drone.bag != null && CanAccess(drone);
            }
            catch (Exception) { return false; }
        }

        internal static void ForceFreshForCraft()
        {
            Invalidate();
            EnsureFresh(true);
        }

        private static void EnsureFresh(bool force)
        {
            if (!Enabled || rebuilding)
            {
                return;
            }

            GameManager gameManager = GameManager.Instance;
            World world = gameManager == null ? null : gameManager.World;
            EntityPlayerLocal player = world == null ? null : world.GetPrimaryPlayer();
            if (player == null)
            {
                Sources.Clear();
                ItemCounts.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            Vector3 playerPosition = player.position;
            bool moved = (playerPosition - lastPlayerPosition).sqrMagnitude > 1f;
            if (!force && player.entityId == lastPlayerId && !moved && now < expiresAt)
            {
                return;
            }

            rebuilding = true;
            try
            {
                Sources.Clear();
                ItemCounts.Clear();
                ScanTileEntities(world, playerPosition);
                ScanEntityStorage(world, playerPosition);
                Sources.Sort(DistanceComparer.Instance);
                BuildCounts();
                lastPlayerPosition = playerPosition;
                lastPlayerId = player.entityId;
                expiresAt = now + config.CacheMilliseconds / 1000f;
                Debug("Indexed " + Sources.Count + " nearby storage sources and " + ItemCounts.Count + " item types.");
            }
            catch (Exception exception)
            {
                Sources.Clear();
                ItemCounts.Clear();
                expiresAt = now + 1f;
                Log.Warning("[NearbyCraft] Storage scan failed safely: {0}", exception.Message);
            }
            finally
            {
                rebuilding = false;
            }
        }

        private static void ScanTileEntities(World world, Vector3 playerPosition)
        {
            int minChunkX = Utils.Fastfloor((playerPosition.x - config.Range) / 16f);
            int maxChunkX = Utils.Fastfloor((playerPosition.x + config.Range) / 16f);
            int minChunkZ = Utils.Fastfloor((playerPosition.z - config.Range) / 16f);
            int maxChunkZ = Utils.Fastfloor((playerPosition.z + config.Range) / 16f);
            float maxDistanceSquared = config.Range * config.Range;

            for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    Chunk chunk = world.GetChunkSync(chunkX, chunkZ) as Chunk;
                    List<TileEntity> tileEntities = chunk == null || chunk.tileEntities == null ? null : chunk.tileEntities.list;
                    if (tileEntities == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < tileEntities.Count; i++)
                    {
                        TileEntity tileEntity = tileEntities[i];
                        if (tileEntity == null || tileEntity.IsRemoving || tileEntity.IsUserAccessing()
                            || StorageTerminalManager.IsTerminal(tileEntity.block)
                            || LoadoutLockerManager.IsLocker(tileEntity.block)
                            || WorkshopManager.IsController(tileEntity.block))
                        {
                            continue;
                        }

                        float distanceSquared = (tileEntity.ToWorldCenterPos() - playerPosition).sqrMagnitude;
                        if (distanceSquared > maxDistanceSquared || !CanAccess(tileEntity))
                        {
                            continue;
                        }

                        TEFeatureLandClaim landClaim;
                        if (tileEntity.TryGetSelfOrFeature<TEFeatureLandClaim>(out landClaim) && landClaim != null)
                        {
                            continue;
                        }

                        AddTileEntitySource(tileEntity, distanceSquared);
                    }
                }
            }
        }

        private static void AddTileEntitySource(TileEntity tileEntity, float distanceSquared)
        {
            if (config.IncludeCollectors && tileEntity is TileEntityCollector)
            {
                var collector = (TileEntityCollector)tileEntity;
                AddSource(SourceKind.Collector, collector, collector.Items, null, distanceSquared);
                return;
            }

            if (config.IncludeWorkstationOutputs && tileEntity is TileEntityWorkstation)
            {
                var workstation = (TileEntityWorkstation)tileEntity;
                AddSource(SourceKind.WorkstationOutput, workstation, workstation.Output, null, distanceSquared);
                return;
            }

            if (!config.IncludePlayerStorage)
            {
                return;
            }

            ITileEntityLootable lootable;
            if (tileEntity.TryGetSelfOrFeature<ITileEntityLootable>(out lootable) && lootable != null && lootable.bPlayerStorage)
            {
                PackedBoolArray lockedSlots = config.RespectLockedSlots && lootable.HasSlotLocksSupport ? lootable.SlotLocks : null;
                AddSource(SourceKind.PlayerStorage, tileEntity, lootable.items, lockedSlots, distanceSquared);
            }
        }

        private static void ScanEntityStorage(World world, Vector3 playerPosition)
        {
            Vector3 size = Vector3.one * config.Range * 2f;
            Bounds bounds = new Bounds(playerPosition, size);
            float maxDistanceSquared = config.Range * config.Range;

            if (config.IncludeVehicles)
            {
                NearbyVehicles.Clear();
                world.GetEntitiesInBounds(typeof(EntityVehicle), bounds, NearbyVehicles);
                for (int i = 0; i < NearbyVehicles.Count; i++)
                {
                    var vehicle = NearbyVehicles[i] as EntityVehicle;
                    if (vehicle == null || vehicle.bag == null || !CanAccess(vehicle))
                    {
                        continue;
                    }
                    float distanceSquared = (vehicle.position - playerPosition).sqrMagnitude;
                    if (distanceSquared <= maxDistanceSquared)
                    {
                        AddSource(SourceKind.Vehicle, vehicle, vehicle.bag.GetSlots(),
                            config.RespectLockedSlots ? vehicle.bag.LockedSlots : null, distanceSquared);
                    }
                }
            }

            if (config.IncludeDrones)
            {
                NearbyDrones.Clear();
                world.GetEntitiesInBounds(typeof(EntityDrone), bounds, NearbyDrones);
                for (int i = 0; i < NearbyDrones.Count; i++)
                {
                    var drone = NearbyDrones[i] as EntityDrone;
                    if (drone == null || drone.bag == null || !CanAccess(drone))
                    {
                        continue;
                    }
                    float distanceSquared = (drone.position - playerPosition).sqrMagnitude;
                    if (distanceSquared <= maxDistanceSquared)
                    {
                        AddSource(SourceKind.Drone, drone, drone.bag.GetSlots(),
                            config.RespectLockedSlots ? drone.bag.LockedSlots : null, distanceSquared);
                    }
                }
            }
        }

        private static bool CanAccess(TileEntity tileEntity)
        {
            ILockable lockable;
            return !tileEntity.TryGetSelfOrFeature<ILockable>(out lockable)
                || lockable == null
                || !lockable.IsLocked()
                || lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier);
        }

        private static bool CanAccess(ILockable lockable)
        {
            return lockable == null
                || !lockable.IsLocked()
                || lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier);
        }

        private static void AddSource(SourceKind kind, object owner, ItemStack[] slots, PackedBoolArray lockedSlots, float distanceSquared)
        {
            if (slots == null || slots.Length == 0)
            {
                return;
            }

            Sources.Add(new StorageSource
            {
                Kind = kind,
                Owner = owner,
                Slots = slots,
                LockedSlots = lockedSlots,
                DistanceSquared = distanceSquared
            });
        }

        private static void BuildCounts()
        {
            for (int sourceIndex = 0; sourceIndex < Sources.Count; sourceIndex++)
            {
                StorageSource source = Sources[sourceIndex];
                for (int slotIndex = 0; slotIndex < source.Slots.Length; slotIndex++)
                {
                    ItemStack stack = source.Slots[slotIndex];
                    if (!IsConsumable(stack) || IsSlotLocked(source, slotIndex))
                    {
                        continue;
                    }

                    int current;
                    ItemCounts.TryGetValue(stack.itemValue.type, out current);
                    long total = (long)current + stack.count;
                    ItemCounts[stack.itemValue.type] = total > int.MaxValue ? int.MaxValue : (int)total;
                }
            }
        }

        private static bool IsSlotLocked(StorageSource source, int index)
        {
            PackedBoolArray locked = source.LockedSlots;
            return config.RespectLockedSlots && locked != null && index < locked.Length && locked[index];
        }

        private static bool IsConsumable(ItemStack stack)
        {
            return stack != null
                && !stack.IsEmpty()
                && stack.count > 0
                && (!stack.itemValue.HasModSlots || !stack.itemValue.HasMods());
        }

        private static bool IsRequirement(ItemStack stack)
        {
            return stack != null && !stack.IsEmpty() && stack.count > 0;
        }

        private static bool CanUse(ItemValue itemValue)
        {
            return Enabled && itemValue != null && !itemValue.IsEmpty();
        }

        private static int SafeRequiredCount(int count, int multiplier)
        {
            long result = (long)Math.Max(0, count) * Math.Max(1, multiplier);
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }

        private static void MarkModified(StorageSource source)
        {
            switch (source.Kind)
            {
                case SourceKind.WorkstationOutput:
                    ((TileEntityWorkstation)source.Owner).Output = source.Slots;
                    break;
                case SourceKind.Collector:
                    ((TileEntityCollector)source.Owner).SetModified();
                    break;
                case SourceKind.Vehicle:
                    ((EntityVehicle)source.Owner).bag.SetSlots(source.Slots);
                    ((EntityVehicle)source.Owner).SetBagModified();
                    break;
                case SourceKind.Drone:
                    ((EntityDrone)source.Owner).bag.SetSlots(source.Slots);
                    ((EntityDrone)source.Owner).SendSyncData(8);
                    break;
                default:
                    ((TileEntity)source.Owner).SetModified();
                    break;
            }
        }

        private static void Debug(string message)
        {
            if (config.DebugLogging)
            {
                Log.Out("[NearbyCraft] " + message);
            }
        }
    }
}
