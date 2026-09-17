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
            if (destination == null || !config.Enabled)
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
            if (!config.Enabled)
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

            if (!config.Enabled)
            {
                return inventory.HasItems(required, multiplier);
            }

            EnsureFresh(false);
            for (int i = 0; i < required.Count; i++)
            {
                ItemStack neededStack = required[i];
                if (!IsRequirement(neededStack))
                {
                    continue;
                }

                long needed = (long)neededStack.count * Math.Max(1, multiplier);
                long available = inventory.Backpack.GetItemCount(neededStack.itemValue)
                    + inventory.Toolbelt.GetItemCount(neededStack.itemValue);
                int storageCount;
                if (ItemCounts.TryGetValue(neededStack.itemValue.type, out storageCount))
                {
                    available += storageCount;
                }

                if (available < needed)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool HasItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> required, int multiplier)
        {
            if (grid == null || required == null)
            {
                return false;
            }

            if (!config.Enabled)
            {
                return grid.HasItems(required, multiplier);
            }

            EnsureFresh(false);
            for (int i = 0; i < required.Count; i++)
            {
                ItemStack neededStack = required[i];
                if (!IsRequirement(neededStack))
                {
                    continue;
                }

                long needed = (long)neededStack.count * Math.Max(1, multiplier);
                long available = grid.GetItemCount(neededStack.itemValue);
                int storageCount;
                if (ItemCounts.TryGetValue(neededStack.itemValue.type, out storageCount))
                {
                    available += storageCount;
                }

                if (available < needed)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void RemoveItems(XUiM_PlayerInventory inventory, IList<ItemStack> required, int multiplier, IList<ItemStack> removedItems)
        {
            if (!config.Enabled)
            {
                inventory.RemoveItems(required, multiplier, removedItems);
                return;
            }

            for (int i = 0; i < required.Count; i++)
            {
                ItemStack requiredStack = required[i];
                if (!IsRequirement(requiredStack))
                {
                    continue;
                }

                int remaining = SafeRequiredCount(requiredStack.count, multiplier);
                remaining -= inventory.Backpack.DecItem(requiredStack.itemValue, remaining, true, removedItems);
                if (remaining > 0)
                {
                    remaining -= inventory.Toolbelt.DecItem(requiredStack.itemValue, remaining, true, removedItems);
                }
                if (remaining > 0)
                {
                    RemoveFromStorage(requiredStack.itemValue, remaining, removedItems);
                }
            }

            inventory.dispatchBackpackItemsChanged();
            inventory.dispatchToolbeltItemsChanged();
        }

        internal static void RemoveItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> required, int multiplier, IList<ItemStack> removedItems)
        {
            if (!config.Enabled)
            {
                grid.RemoveItems(required, multiplier, removedItems);
                return;
            }

            for (int i = 0; i < required.Count; i++)
            {
                ItemStack requiredStack = required[i];
                if (!IsRequirement(requiredStack))
                {
                    continue;
                }

                int remaining = SafeRequiredCount(requiredStack.count, multiplier);
                remaining -= grid.DecItem(requiredStack.itemValue, remaining, removedItems);
                if (remaining > 0)
                {
                    RemoveFromStorage(requiredStack.itemValue, remaining, removedItems);
                }
            }
        }

        internal static void ForceFreshForCraft()
        {
            Invalidate();
            EnsureFresh(true);
        }

        private static int RemoveFromStorage(ItemValue itemValue, int requested, IList<ItemStack> removedItems)
        {
            EnsureFresh(false);
            int remaining = requested;

            for (int sourceIndex = 0; sourceIndex < Sources.Count && remaining > 0; sourceIndex++)
            {
                StorageSource source = Sources[sourceIndex];
                bool changed = false;
                ItemStack[] slots = source.Slots;
                if (slots == null)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < slots.Length && remaining > 0; slotIndex++)
                {
                    ItemStack stack = slots[slotIndex];
                    if (!IsConsumable(stack) || IsSlotLocked(source, slotIndex) || stack.itemValue.type != itemValue.type)
                    {
                        continue;
                    }

                    if (stack.itemValue.ItemClass.CanStack())
                    {
                        int amount = Math.Min(stack.count, remaining);
                        if (removedItems != null)
                        {
                            removedItems.Add(new ItemStack(stack.itemValue.Clone(), amount));
                        }
                        stack.count -= amount;
                        remaining -= amount;
                        if (stack.count <= 0)
                        {
                            stack.Clear();
                        }
                    }
                    else
                    {
                        if (removedItems != null)
                        {
                            removedItems.Add(stack.Clone());
                        }
                        stack.Clear();
                        remaining--;
                    }

                    changed = true;
                }

                if (changed)
                {
                    MarkModified(source);
                }
            }

            int removed = requested - remaining;
            if (removed > 0)
            {
                Invalidate();
                Debug("Removed " + removed + " of item type " + itemValue.type + " from nearby storage.");
            }
            return removed;
        }

        private static void EnsureFresh(bool force)
        {
            if (!config.Enabled || rebuilding)
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
                        if (tileEntity == null || tileEntity.IsRemoving || tileEntity.IsUserAccessing())
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
            return config.Enabled && itemValue != null && !itemValue.IsEmpty();
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
