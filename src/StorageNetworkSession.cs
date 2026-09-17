using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;

namespace NearbyCraft
{
    internal enum StorageTerminalSort : byte
    {
        Name,
        Count,
        Type
    }

    internal sealed class StorageNetworkSession
    {
        private sealed class StorageSource
        {
            internal TileEntity Owner;
            internal ITileEntityLootable Storage;
            internal Vector3i Position;
            internal float DistanceSquared;
        }

        private sealed class AggregateBucket
        {
            internal ItemValue ItemValue;
            internal long Count;
        }

        private sealed class SourceDistanceComparer : IComparer<StorageSource>
        {
            internal static readonly SourceDistanceComparer Instance = new SourceDistanceComparer();

            public int Compare(StorageSource left, StorageSource right)
            {
                return left.DistanceSquared.CompareTo(right.DistanceSquared);
            }
        }

        internal const int VisibleColumns = 9;
        internal const int VisibleRows = 6;
        internal const int VisibleSlotCount = VisibleColumns * VisibleRows;

        private readonly World world;
        private readonly EntityPlayerLocal player;
        private readonly Vector3i terminalPosition;
        private readonly int range;
        private readonly bool respectLockedSlots;
        private readonly List<StorageSource> sources = new List<StorageSource>(32);
        private readonly List<AggregateBucket> buckets = new List<AggregateBucket>(128);
        private readonly Dictionary<int, List<AggregateBucket>> bucketsByType = new Dictionary<int, List<AggregateBucket>>(128);
        private readonly List<ItemStack> allItems = new List<ItemStack>(128);
        private readonly List<ItemStack> filteredItems = new List<ItemStack>(128);
        private readonly List<StorageSource> modifiedSources = new List<StorageSource>(8);

        private string search = string.Empty;
        private StorageTerminalSort sort = StorageTerminalSort.Name;
        private int scrollRow;

        internal int ConnectedStorageCount { get; private set; }
        internal int UsedSlotCount { get; private set; }
        internal int TotalSlotCount { get; private set; }
        internal long TotalItemCount { get; private set; }
        internal int ResultCount { get { return filteredItems.Count; } }
        internal int ScrollRow { get { return scrollRow; } }
        internal int TotalRows { get { return (filteredItems.Count + VisibleColumns - 1) / VisibleColumns; } }
        internal int MaxScrollRow { get { return Math.Max(0, TotalRows - VisibleRows); } }
        internal int FirstVisibleIndex { get { return Math.Min(filteredItems.Count, scrollRow * VisibleColumns); } }
        internal int LastVisibleIndex { get { return Math.Min(filteredItems.Count, FirstVisibleIndex + VisibleSlotCount); } }
        internal StorageTerminalSort Sort { get { return sort; } }

        internal StorageNetworkSession(World world, EntityPlayerLocal player, Vector3i terminalPosition, NearbyCraftConfig config)
        {
            this.world = world;
            this.player = player;
            this.terminalPosition = terminalPosition;
            range = config == null ? 15 : config.TerminalRange;
            respectLockedSlots = config == null || config.RespectLockedSlots;
            StorageTerminalSort configuredSort;
            if (config != null && Enum.TryParse(config.TerminalSort, true, out configuredSort)
                && Enum.IsDefined(typeof(StorageTerminalSort), configuredSort))
            {
                sort = configuredSort;
            }
        }

        internal void Rescan()
        {
            sources.Clear();
            if (world == null || player == null)
            {
                RebuildItems();
                return;
            }

            try
            {
                Vector3 center = terminalPosition.ToVector3() + Vector3.one * 0.5f;
                int minChunkX = Utils.Fastfloor((center.x - range) / 16f);
                int maxChunkX = Utils.Fastfloor((center.x + range) / 16f);
                int minChunkZ = Utils.Fastfloor((center.z - range) / 16f);
                int maxChunkZ = Utils.Fastfloor((center.z + range) / 16f);
                float maxDistanceSquared = range * range;

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

                            Vector3i position = tileEntity.ToWorldPos();
                            if (position == terminalPosition || IsTerminal(tileEntity))
                            {
                                continue;
                            }

                            float distanceSquared = (tileEntity.ToWorldCenterPos() - center).sqrMagnitude;
                            if (distanceSquared > maxDistanceSquared || !CanAccess(tileEntity))
                            {
                                continue;
                            }

                            ITileEntityLootable storage;
                            if (!tileEntity.TryGetSelfOrFeature<ITileEntityLootable>(out storage)
                                || storage == null
                                || !storage.bPlayerStorage
                                || storage.items == null
                                || storage.items.Length == 0)
                            {
                                continue;
                            }

                            sources.Add(new StorageSource
                            {
                                Owner = tileEntity,
                                Storage = storage,
                                Position = position,
                                DistanceSquared = distanceSquared
                            });
                        }
                    }
                }

                sources.Sort(SourceDistanceComparer.Instance);
            }
            catch (Exception exception)
            {
                sources.Clear();
                Log.Warning("[NearbyCraft] Storage terminal scan failed safely: {0}", exception.Message);
            }

            RebuildItems();
        }

        internal void RebuildItems()
        {
            buckets.Clear();
            bucketsByType.Clear();
            allItems.Clear();
            ConnectedStorageCount = 0;
            UsedSlotCount = 0;
            TotalSlotCount = 0;
            TotalItemCount = 0;

            for (int sourceIndex = sources.Count - 1; sourceIndex >= 0; sourceIndex--)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    sources.RemoveAt(sourceIndex);
                    continue;
                }

                ConnectedStorageCount++;
                ItemStack[] slots = source.Storage.items;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex))
                    {
                        continue;
                    }

                    TotalSlotCount++;
                    ItemStack stack = slots[slotIndex];
                    if (stack == null || stack.IsEmpty() || stack.count <= 0)
                    {
                        continue;
                    }

                    UsedSlotCount++;
                    TotalItemCount += stack.count;
                    AddToAggregate(stack);
                }
            }

            for (int i = 0; i < buckets.Count; i++)
            {
                AggregateBucket bucket = buckets[i];
                int maxCount = Math.Max(1, bucket.ItemValue.ItemClassOrMissing.MaxCount);
                long remaining = bucket.Count;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(maxCount, remaining);
                    allItems.Add(new ItemStack(bucket.ItemValue.Clone(), count));
                    remaining -= count;
                }
            }

            RebuildFilter();
        }

        internal ItemStack[] GetVisibleStacks()
        {
            var result = ItemStack.CreateArray(VisibleSlotCount);
            int start = FirstVisibleIndex;
            int count = Math.Min(VisibleSlotCount, filteredItems.Count - start);
            for (int i = 0; i < count; i++)
            {
                result[i] = filteredItems[start + i].Clone();
            }
            return result;
        }

        internal void SetSearch(string value)
        {
            string normalized = value == null ? string.Empty : value.Trim();
            if (string.Equals(search, normalized, StringComparison.Ordinal))
            {
                return;
            }
            search = normalized;
            scrollRow = 0;
            RebuildFilter();
        }

        internal void CycleSort()
        {
            sort = (StorageTerminalSort)(((int)sort + 1) % 3);
            scrollRow = 0;
            RebuildFilter();
        }

        internal bool ScrollRows(int rows)
        {
            return SetScrollRow(scrollRow + rows);
        }

        internal bool SetScrollRow(int row)
        {
            int clamped = Math.Max(0, Math.Min(MaxScrollRow, row));
            if (clamped == scrollRow)
            {
                return false;
            }
            scrollRow = clamped;
            return true;
        }

        internal int Deposit(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty() || stack.count <= 0 || !stack.itemValue.ItemClassOrMissing.CanPlaceInContainer())
            {
                return 0;
            }

            int requested = stack.count;
            modifiedSources.Clear();
            for (int sourceIndex = 0; sourceIndex < sources.Count && stack.count > 0; sourceIndex++)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    continue;
                }

                bool changed = false;
                ItemStack[] slots = source.Storage.items;
                for (int slotIndex = 0; slotIndex < slots.Length && stack.count > 0; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex))
                    {
                        continue;
                    }

                    ItemStack target = slots[slotIndex];
                    int transfer;
                    if (target != null && !target.IsEmpty() && target.CanStackPartlyWith(stack, out transfer))
                    {
                        target.count += transfer;
                        stack.count -= transfer;
                        changed = true;
                    }
                }

                if (changed)
                {
                    modifiedSources.Add(source);
                }
            }

            for (int sourceIndex = 0; sourceIndex < sources.Count && stack.count > 0; sourceIndex++)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    continue;
                }

                bool changed = false;
                ItemStack[] slots = source.Storage.items;
                int maxCount = Math.Max(1, stack.itemValue.ItemClassOrMissing.MaxCount);
                for (int slotIndex = 0; slotIndex < slots.Length && stack.count > 0; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex) || (slots[slotIndex] != null && !slots[slotIndex].IsEmpty()))
                    {
                        continue;
                    }

                    int transfer = Math.Min(maxCount, stack.count);
                    slots[slotIndex] = new ItemStack(stack.itemValue.Clone(), transfer);
                    stack.count -= transfer;
                    changed = true;
                }

                if (changed)
                {
                    if (!modifiedSources.Contains(source))
                    {
                        modifiedSources.Add(source);
                    }
                }
            }

            int moved = requested - stack.count;
            if (moved > 0)
            {
                for (int i = 0; i < modifiedSources.Count; i++)
                {
                    modifiedSources[i].Owner.SetModified();
                }
                StorageIndex.Invalidate();
            }
            return moved;
        }

        internal int Withdraw(ItemStack template, int requested)
        {
            if (template == null || template.IsEmpty() || requested <= 0)
            {
                return 0;
            }

            int remaining = requested;
            for (int sourceIndex = 0; sourceIndex < sources.Count && remaining > 0; sourceIndex++)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    continue;
                }

                bool changed = false;
                ItemStack[] slots = source.Storage.items;
                for (int slotIndex = 0; slotIndex < slots.Length && remaining > 0; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex))
                    {
                        continue;
                    }

                    ItemStack stored = slots[slotIndex];
                    if (!ItemsMatch(stored, template))
                    {
                        continue;
                    }

                    int transfer = Math.Min(stored.count, remaining);
                    stored.count -= transfer;
                    remaining -= transfer;
                    if (stored.count <= 0)
                    {
                        stored.Clear();
                    }
                    changed = true;
                }

                if (changed)
                {
                    source.Owner.SetModified();
                }
            }

            int moved = requested - remaining;
            if (moved > 0)
            {
                StorageIndex.Invalidate();
            }
            return moved;
        }

        internal int DepositBackpack(XUiM_PlayerInventory inventory)
        {
            if (inventory == null || player == null)
            {
                return 0;
            }

            ItemStack[] current = inventory.GetBackpackItemStacks();
            ItemStack[] updated = ItemStack.Clone(current);
            PackedBoolArray locked = inventory.Backpack.LockedSlots;
            int slotLimit = Math.Min(updated.Length, player.CarryCapacity);
            int moved = 0;

            for (int i = 0; i < slotLimit; i++)
            {
                if ((locked != null && i < locked.Length && locked[i]) || updated[i] == null || updated[i].IsEmpty())
                {
                    continue;
                }

                ItemStack remainder = updated[i].Clone();
                int deposited = Deposit(remainder);
                if (deposited <= 0)
                {
                    continue;
                }

                moved += deposited;
                updated[i] = remainder.count > 0 ? remainder : ItemStack.Empty;
            }

            if (moved > 0)
            {
                inventory.SetBackpackItemStacks(updated);
                RebuildItems();
            }
            return moved;
        }

        internal bool CanDeposit(ItemStack stack)
        {
            return stack != null
                && !stack.IsEmpty()
                && stack.itemValue.ItemClassOrMissing.CanPlaceInContainer()
                && GetDepositCapacity(stack) >= stack.count;
        }

        internal bool CanDepositAfterWithdraw(ItemStack removed, ItemStack added)
        {
            if (CanDeposit(added))
            {
                return true;
            }
            if (removed == null || removed.IsEmpty() || added == null || added.IsEmpty())
            {
                return false;
            }

            long capacity = GetDepositCapacity(added);
            int remainingRemoval = removed.count;
            int addedMax = Math.Max(1, added.itemValue.ItemClassOrMissing.MaxCount);
            for (int sourceIndex = 0; sourceIndex < sources.Count && remainingRemoval > 0; sourceIndex++)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    continue;
                }

                ItemStack[] slots = source.Storage.items;
                for (int slotIndex = 0; slotIndex < slots.Length && remainingRemoval > 0; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex) || !ItemsMatch(slots[slotIndex], removed))
                    {
                        continue;
                    }

                    int taken = Math.Min(slots[slotIndex].count, remainingRemoval);
                    remainingRemoval -= taken;
                    if (taken == slots[slotIndex].count)
                    {
                        capacity += addedMax;
                    }
                }
            }
            return capacity >= added.count;
        }

        internal bool ApplyDisplayChange(ItemStack previous, ItemStack current)
        {
            previous = previous ?? ItemStack.Empty;
            current = current ?? ItemStack.Empty;
            bool changed = false;

            if (ItemsMatch(previous, current))
            {
                int delta = current.count - previous.count;
                if (delta > 0)
                {
                    ItemStack addition = current.Clone();
                    addition.count = delta;
                    changed = Deposit(addition) > 0;
                }
                else if (delta < 0)
                {
                    changed = Withdraw(previous, -delta) > 0;
                }
            }
            else
            {
                if (!previous.IsEmpty())
                {
                    changed |= Withdraw(previous, previous.count) > 0;
                }
                if (!current.IsEmpty())
                {
                    ItemStack addition = current.Clone();
                    changed |= Deposit(addition) > 0;
                }
            }
            return changed;
        }

        internal bool TrySwap(ItemStack displayed, ItemStack held, out ItemStack newHeld)
        {
            displayed = displayed ?? ItemStack.Empty;
            held = held ?? ItemStack.Empty;
            newHeld = held.Clone();

            if (held.IsEmpty())
            {
                int withdrawn = Withdraw(displayed, displayed.count);
                if (withdrawn <= 0)
                {
                    return false;
                }
                newHeld = displayed.Clone();
                newHeld.count = withdrawn;
                return true;
            }

            if (displayed.IsEmpty())
            {
                ItemStack remainder = held.Clone();
                if (Deposit(remainder) <= 0)
                {
                    return false;
                }
                newHeld = remainder.count > 0 ? remainder : ItemStack.Empty;
                return true;
            }

            if (!CanDepositAfterWithdraw(displayed, held))
            {
                return false;
            }

            int removed = Withdraw(displayed, displayed.count);
            if (removed != displayed.count)
            {
                if (removed > 0)
                {
                    ItemStack rollback = displayed.Clone();
                    rollback.count = removed;
                    Deposit(rollback);
                }
                return false;
            }

            ItemStack incoming = held.Clone();
            int deposited = Deposit(incoming);
            if (deposited != held.count)
            {
                if (deposited > 0)
                {
                    Withdraw(held, deposited);
                }
                ItemStack rollback = displayed.Clone();
                Deposit(rollback);
                return false;
            }

            newHeld = displayed.Clone();
            return true;
        }

        internal static bool ItemsMatch(ItemStack left, ItemStack right)
        {
            if (left == null || right == null || left.IsEmpty() || right.IsEmpty())
            {
                return (left == null || left.IsEmpty()) && (right == null || right.IsEmpty());
            }
            if (left.itemValue.type != right.itemValue.type)
            {
                return false;
            }
            if (!left.itemValue.ItemClassOrMissing.CanStack())
            {
                return left.itemValue.Equals(right.itemValue);
            }

            ItemStack leftOne = left.Clone();
            ItemStack rightOne = right.Clone();
            leftOne.count = 1;
            rightOne.count = 1;
            return leftOne.CanStackWith(rightOne, true);
        }

        private void AddToAggregate(ItemStack stack)
        {
            List<AggregateBucket> typeBuckets;
            if (!bucketsByType.TryGetValue(stack.itemValue.type, out typeBuckets))
            {
                typeBuckets = new List<AggregateBucket>(1);
                bucketsByType.Add(stack.itemValue.type, typeBuckets);
            }

            if (stack.itemValue.ItemClassOrMissing.CanStack())
            {
                for (int i = 0; i < typeBuckets.Count; i++)
                {
                    AggregateBucket existing = typeBuckets[i];
                    if (ItemsMatch(new ItemStack(existing.ItemValue, 1), stack))
                    {
                        existing.Count += stack.count;
                        return;
                    }
                }
            }

            var bucket = new AggregateBucket
            {
                ItemValue = stack.itemValue.Clone(),
                Count = stack.count
            };
            typeBuckets.Add(bucket);
            buckets.Add(bucket);
        }

        private void RebuildFilter()
        {
            filteredItems.Clear();
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemStack stack = allItems[i];
                if (MatchesSearch(stack))
                {
                    filteredItems.Add(stack);
                }
            }

            filteredItems.Sort(CompareItems);
            scrollRow = Math.Max(0, Math.Min(MaxScrollRow, scrollRow));
        }

        private int CompareItems(ItemStack left, ItemStack right)
        {
            int result;
            switch (sort)
            {
                case StorageTerminalSort.Count:
                    result = right.count.CompareTo(left.count);
                    if (result != 0)
                    {
                        return result;
                    }
                    return string.Compare(GetDisplayName(left), GetDisplayName(right), StringComparison.OrdinalIgnoreCase);
                case StorageTerminalSort.Type:
                    result = left.itemValue.type.CompareTo(right.itemValue.type);
                    if (result != 0)
                    {
                        return result;
                    }
                    return right.count.CompareTo(left.count);
                default:
                    result = string.Compare(GetDisplayName(left), GetDisplayName(right), StringComparison.OrdinalIgnoreCase);
                    if (result != 0)
                    {
                        return result;
                    }
                    return right.count.CompareTo(left.count);
            }
        }

        private bool MatchesSearch(ItemStack stack)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }

            ItemClass itemClass = stack.itemValue.ItemClassOrMissing;
            string internalName = itemClass.GetItemName() ?? string.Empty;
            string displayName = GetDisplayName(stack);
            return internalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetDisplayName(ItemStack stack)
        {
            ItemClass itemClass = stack.itemValue.ItemClassOrMissing;
            string name = itemClass.GetLocalizedItemName();
            if (string.IsNullOrEmpty(name))
            {
                name = Localization.Get(itemClass.GetItemName());
            }
            return name ?? string.Empty;
        }

        private long GetDepositCapacity(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty() || !stack.itemValue.ItemClassOrMissing.CanPlaceInContainer())
            {
                return 0;
            }

            long capacity = 0;
            int maxCount = Math.Max(1, stack.itemValue.ItemClassOrMissing.MaxCount);
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                StorageSource source = sources[sourceIndex];
                if (!IsSourceValid(source))
                {
                    continue;
                }

                ItemStack[] slots = source.Storage.items;
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    if (IsSlotLocked(source, slotIndex))
                    {
                        continue;
                    }

                    ItemStack target = slots[slotIndex];
                    if (target == null || target.IsEmpty())
                    {
                        capacity += maxCount;
                    }
                    else
                    {
                        int transfer;
                        if (target.CanStackPartlyWith(stack, out transfer))
                        {
                            capacity += transfer;
                        }
                    }

                    if (capacity >= stack.count)
                    {
                        return capacity;
                    }
                }
            }
            return capacity;
        }

        private bool IsSourceValid(StorageSource source)
        {
            if (source == null || source.Owner == null || source.Storage == null || source.Owner.IsRemoving || source.Owner.IsUserAccessing())
            {
                return false;
            }
            TileEntity current = world == null ? null : world.GetTileEntity(source.Position);
            return current == source.Owner && CanAccess(source.Owner) && source.Storage.bPlayerStorage;
        }

        private bool IsSlotLocked(StorageSource source, int index)
        {
            PackedBoolArray locked = respectLockedSlots && source.Storage.HasSlotLocksSupport ? source.Storage.SlotLocks : null;
            return locked != null && index < locked.Length && locked[index];
        }

        private static bool CanAccess(TileEntity tileEntity)
        {
            ILockable lockable;
            return !tileEntity.TryGetSelfOrFeature<ILockable>(out lockable)
                || lockable == null
                || !lockable.IsLocked()
                || lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier);
        }

        private static bool IsTerminal(TileEntity tileEntity)
        {
            Block block = tileEntity.block;
            return block != null && string.Equals(block.GetBlockName(), StorageTerminalManager.BlockName, StringComparison.Ordinal);
        }
    }
}
