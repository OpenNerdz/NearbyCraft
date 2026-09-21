using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;

namespace NearbyCraft
{
    internal sealed partial class StorageNetworkSession
    {
        private sealed class StorageSource
        {
            internal TileEntity Owner;
            internal ITileEntityLootable Storage;
            internal Vector3i Position;
            internal float DistanceSquared;
        }

        private sealed class SourceDistanceComparer : IComparer<StorageSource>
        {
            internal static readonly SourceDistanceComparer Instance = new SourceDistanceComparer();

            public int Compare(StorageSource left, StorageSource right)
            {
                int order = left.DistanceSquared.CompareTo(right.DistanceSquared);
                if (order != 0) return order;
                order = left.Position.x.CompareTo(right.Position.x);
                if (order != 0) return order;
                order = left.Position.y.CompareTo(right.Position.y);
                return order != 0 ? order : left.Position.z.CompareTo(right.Position.z);
            }
        }

        internal const int VisibleColumns = 9;
        internal const int VisibleRows = 6;
        internal const int VisibleSlotCount = VisibleColumns * VisibleRows;

        private readonly World world;
        private readonly EntityPlayerLocal player;
        private readonly Vector3i terminalPosition;
        private readonly Vector3i interactionPosition;
        private readonly int range;
        private readonly bool respectLockedSlots;
        private readonly bool automation;
        internal bool AutomationBusy { get; private set; }
        private readonly List<StorageSource> sources = new List<StorageSource>(32);
        private TerminalCatalog catalog; // Created only for a UI/counting session, never ore-only export.
        private readonly List<TerminalCatalog.Entry> filteredItems = new List<TerminalCatalog.Entry>(128);
        internal int Tier { get; private set; }
        internal int ChestLimit { get { return TerminalRules.ChestLimit(Tier); } }
        internal int OverflowCount { get; private set; }
        internal bool IsAvailable
        {
            get
            {
                return NearbyCraftMod.CanUseLocalStorage && world != null && player != null
                    && StorageTerminalManager.IsTerminal(world.GetBlock(terminalPosition).Block)
                    && world.GetTileEntity(terminalPosition) != null
                    && CanAccess(world.GetTileEntity(terminalPosition))
                    && (automation || (player.position - interactionPosition.ToVector3()).sqrMagnitude <= 64f);
            }
        }

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

        internal StorageNetworkSession(World world, EntityPlayerLocal player, Vector3i terminalPosition, NearbyCraftConfig config,
            Vector3i? interactionPosition = null, bool automation = false)
        {
            this.world = world;
            this.player = player;
            this.terminalPosition = terminalPosition;
            this.interactionPosition = interactionPosition ?? terminalPosition;
            this.automation = automation;
            Tier = StorageTerminalManager.GetTier(world.GetBlock(terminalPosition).Block);
            range = config == null ? 15 : config.TerminalRange;
            respectLockedSlots = config == null || config.RespectLockedSlots;
            StorageTerminalSort configuredSort;
            if (config != null && Enum.TryParse(config.TerminalSort, true, out configuredSort)
                && Enum.IsDefined(typeof(StorageTerminalSort), configuredSort))
            {
                sort = configuredSort;
            }
        }

        internal void Rescan(bool includeItemCatalog = true)
        {
            sources.Clear();
            OverflowCount = 0;
            AutomationBusy = false;
            if (!IsAvailable)
            {
                if (includeItemCatalog) RebuildItems();
                else ConnectedStorageCount = 0;
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
                            if (tileEntity == null || tileEntity.IsRemoving || (!automation && tileEntity.IsUserAccessing()))
                            {
                                continue;
                            }

                            Vector3i position = tileEntity.ToWorldPos();
                            TEFeatureLandClaim landClaim;
                            if (position == terminalPosition || IsTerminal(tileEntity)
                                || LoadoutLockerManager.IsLocker(world.GetBlock(position).Block)
                                || WorkshopManager.IsController(world.GetBlock(position).Block)
                                || (tileEntity.TryGetSelfOrFeature<TEFeatureLandClaim>(out landClaim) && landClaim != null))
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

                            if (automation && tileEntity.IsUserAccessing()) AutomationBusy = true;
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
                OverflowCount = Math.Max(0, sources.Count - ChestLimit);
                if (sources.Count > ChestLimit) sources.RemoveRange(ChestLimit, sources.Count - ChestLimit);
            }
            catch (Exception exception)
            {
                sources.Clear();
                Log.Warning("[NearbyCraft] Storage terminal scan failed safely: {0}", exception.Message);
            }

            if (includeItemCatalog) RebuildItems();
            else ConnectedStorageCount = sources.Count;
        }

        internal void RebuildItems()
        {
            if (catalog == null) catalog = new TerminalCatalog(GetDisplayName, s => s.itemValue.ItemClassOrMissing.GetItemName());
            catalog.Clear();
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
                    catalog.Add(stack);
                }
            }

            RebuildFilter();
        }

        internal ItemStack[] GetVisibleStacks(out long[] totals)
        {
            var result = ItemStack.CreateArray(VisibleSlotCount);
            totals = new long[VisibleSlotCount];
            int start = FirstVisibleIndex;
            int count = Math.Min(VisibleSlotCount, filteredItems.Count - start);
            for (int i = 0; i < count; i++)
            {
                var entry = filteredItems[start + i];
                result[i] = entry.TransferStack();
                totals[i] = entry.Count;
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

        internal void SetSort(StorageTerminalSort value)
        {
            sort = value;
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

        private sealed class Transaction
        {
            internal readonly StorageTransferPlan Plan = new StorageTransferPlan();
            internal readonly List<StorageSource> Sources = new List<StorageSource>();
            internal readonly List<ItemStack[]> Slots = new List<ItemStack[]>();
            internal readonly List<bool[]> Locks = new List<bool[]>();
        }

        private Transaction BeginTransaction(TileEntity excludedSource = null)
        {
            var transaction = new Transaction();
            if (!IsAvailable) return transaction;
            foreach (StorageSource source in sources)
            {
                if (source.Owner == excludedSource || !IsSourceValid(source)) continue;
                ItemStack[] slots = source.Storage.items;
                var locks = new bool[slots.Length];
                for (int i = 0; i < locks.Length; i++) locks[i] = IsSlotLocked(source, i);
                transaction.Sources.Add(source);
                transaction.Slots.Add(slots);
                transaction.Locks.Add(locks);
                transaction.Plan.Add(slots, locks);
            }
            return transaction;
        }

        private bool Commit(Transaction transaction, Func<bool> extraValidation = null, Action applyOtherSlots = null)
        {
            if (!CanCommit(transaction, extraValidation)) return false;
            try
            {
                if (!transaction.Plan.TryCommit(index => IsTransactionSourceValid(transaction, index))) return false;
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Storage transaction commit validation failed safely: {0}", exception.Message);
                return false;
            }
            // Workshop callers supply only prevalidated array/field assignments.
            // Finish both inventories before any SetModified callbacks can run.
            if (applyOtherSlots != null) applyOtherSlots();
            for (int i = 0; i < transaction.Sources.Count; i++)
            {
                if (!transaction.Plan.Changed(i)) continue;
                try { transaction.Sources[i].Owner.SetModified(); }
                catch (Exception exception)
                {
                    // Live counts have committed. Still complete the cursor/player
                    // side of the transfer, even if a notification fails.
                    Log.Error("[NearbyCraft] Storage transfer committed but a change notification failed at {0}: {1}",
                        transaction.Sources[i].Position, exception);
                }
            }
            StorageIndex.Invalidate();
            return true;
        }

        private bool CanCommit(Transaction transaction, Func<bool> extraValidation = null)
        {
            try
            {
                return transaction != null && IsAvailable && (!automation || !AutomationBusy)
                    && (extraValidation == null || extraValidation())
                    && transaction.Plan.CanCommit(index => IsTransactionSourceValid(transaction, index));
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Storage transaction validation failed safely: {0}", exception.Message);
                return false;
            }
        }

        private bool IsTransactionSourceValid(Transaction transaction, int index)
        {
            StorageSource source = transaction.Sources[index];
            if (!IsSourceValid(source) || !ReferenceEquals(source.Storage.items, transaction.Slots[index])) return false;
            for (int i = 0; i < transaction.Locks[index].Length; i++)
                if (IsSlotLocked(source, i) != transaction.Locks[index][i]) return false;
            return true;
        }

        internal bool TryExchangeLoadout(IList<ItemStack> current, IList<ItemStack> target,
            Func<bool> playerStillValid, Action applyPlayer, Action rollbackPlayer, out LoadoutSwapResult result)
        {
            Transaction transaction = BeginTransaction();
            if (!LoadoutSwapPlanner.TryPlan(current, target, transaction.Plan, out result)) return false;
            if (!CanCommit(transaction, playerStillValid))
            {
                result.Error = "The player or storage network changed during the swap. Nothing was moved.";
                return false;
            }
            try { applyPlayer(); }
            catch (Exception exception)
            {
                bool restored = TryRollback(rollbackPlayer, "loadout apply", exception);
                result.Error = restored ? "The player inventory rejected the loadout. Nothing was moved."
                    : "The player inventory rejected the loadout and could not be restored. Check the game log.";
                return false;
            }
            if (!Commit(transaction))
            {
                bool restored = TryRollback(rollbackPlayer, "loadout commit", null);
                result.Error = restored ? "The storage network changed during the swap. The player inventory was restored."
                    : "The storage network changed and the player inventory could not be restored. Check the game log.";
                return false;
            }
            RebuildItems();
            return true;
        }

        private static bool TryRollback(Action rollback, string operation, Exception cause)
        {
            if (cause != null) Log.Error("[NearbyCraft] {0} failed before storage commit: {1}", operation, cause);
            try
            {
                rollback();
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("[NearbyCraft] {0} rollback failed: {1}", operation, exception);
                return false;
            }
        }

        internal int Deposit(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty() || !stack.CanMoveTo(XUiC_ItemStack.StackLocationTypes.LootContainer)) return 0;
            Transaction transaction = BeginTransaction();
            int moved = transaction.Plan.Deposit(stack);
            if (moved <= 0 || !Commit(transaction)) return 0;
            stack.count -= moved;
            return moved;
        }

        internal int Withdraw(ItemStack template, int requested)
        {
            Transaction transaction = BeginTransaction();
            int moved = transaction.Plan.Withdraw(template, requested);
            return moved > 0 && Commit(transaction) ? moved : 0;
        }

        // Smart matching is evaluated against the ORIGINAL contents, not items just deposited.
        // Deposit All includes backpack locks; Matching Only respects them.
        // Reserves count eligible backpack items; toolbelt contents are never touched.
        internal int DepositBackpack(XUiM_PlayerInventory inventory, bool matchingOnly)
        {
            if (inventory == null || player == null) return 0;
            ItemStack[] current = ItemStack.Clone(inventory.GetBackpackItemStacks());
            ItemStack[] updated = ItemStack.Clone(current);
            PackedBoolArray locked = inventory.Backpack.LockedSlots;
            int slotLimit = Math.Min(updated.Length, player.CarryCapacity);
            var retained = new Dictionary<string, int>();
            Transaction transaction = BeginTransaction();
            int moved = 0;
            for (int i = 0; i < slotLimit; i++)
            {
                ItemStack stack = updated[i];
                bool slotLocked = locked != null && i < locked.Length && locked[i];
                if (!TerminalRules.CanBulkDepositSlot(matchingOnly, slotLocked) || stack == null || stack.IsEmpty()
                    || !stack.CanMoveTo(XUiC_ItemStack.StackLocationTypes.LootContainer)) continue;
                string name = stack.itemValue.ItemClassOrMissing.GetItemName();
                int amount = TerminalRules.Depositable(name, stack.count,
                    NearbyCraftMod.Config == null ? null : NearbyCraftMod.Config.PersonalReserves, retained);
                if (amount == 0 || (matchingOnly && !transaction.Plan.Contains(stack))) continue;
                ItemStack request = stack.Clone();
                request.count = amount;
                int deposited = transaction.Plan.Deposit(request);
                moved += deposited;
                stack.count -= deposited;
                if (stack.count == 0) updated[i] = ItemStack.Empty.Clone();
            }
            if (moved == 0 || !SameSlots(current, inventory.GetBackpackItemStacks()) || !CanCommit(transaction)) return 0;
            try { inventory.SetBackpackItemStacks(ItemStack.Clone(updated)); }
            catch (Exception exception)
            {
                TryRollback(() => inventory.SetBackpackItemStacks(ItemStack.Clone(current)), "backpack deposit apply", exception);
                return 0;
            }
            if (!Commit(transaction))
            {
                TryRollback(() => inventory.SetBackpackItemStacks(ItemStack.Clone(current)), "backpack deposit commit", null);
                return 0;
            }
            RebuildItems();
            return moved;
        }

        internal bool CanDepositAfterWithdraw(ItemStack removed, ItemStack added)
        {
            if (added == null || added.IsEmpty()) return true;
            Transaction transaction = BeginTransaction();
            if (removed != null && !removed.IsEmpty()
                && transaction.Plan.Withdraw(removed, removed.count) != removed.count) return false;
            return transaction.Plan.Deposit(added) == added.count;
        }

        internal bool TrySwap(ItemStack displayed, ItemStack held, out ItemStack newHeld)
        {
            displayed = displayed ?? ItemStack.Empty;
            held = held ?? ItemStack.Empty;
            newHeld = held.Clone();
            Transaction transaction = BeginTransaction();
            if (held.IsEmpty())
            {
                int amount = transaction.Plan.Withdraw(displayed, displayed.count);
                if (amount <= 0 || !Commit(transaction)) return false;
                newHeld = displayed.Clone();
                newHeld.count = amount;
                return true;
            }
            if (displayed.IsEmpty() || ItemsMatch(displayed, held))
            {
                int amount = transaction.Plan.Deposit(held);
                if (amount <= 0 || !Commit(transaction)) return false;
                newHeld.count -= amount;
                if (newHeld.count == 0) newHeld = ItemStack.Empty;
                return true;
            }
            // A swap is all-or-nothing. Failed planning never touches live containers.
            if (transaction.Plan.Withdraw(displayed, displayed.count) != displayed.count
                || transaction.Plan.Deposit(held) != held.count || !Commit(transaction)) return false;
            newHeld = displayed.Clone();
            return true;
        }

        internal static bool ItemsMatch(ItemStack left, ItemStack right)
        {
            return StorageTransferPlan.Matches(left, right);
        }

        private void RebuildFilter()
        {
            if (catalog == null) { filteredItems.Clear(); scrollRow = 0; return; }
            catalog.FilterAndSort(search, sort, filteredItems);
            scrollRow = Math.Max(0, Math.Min(MaxScrollRow, scrollRow));
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

        private bool IsSourceValid(StorageSource source)
        {
            if (source == null || source.Owner == null || source.Storage == null || source.Owner.IsRemoving || source.Owner.IsUserAccessing())
            {
                return false;
            }
            TileEntity current = world == null ? null : world.GetTileEntity(source.Position);
            return current == source.Owner && CanAccess(source.Owner) && source.Storage.bPlayerStorage
                && source.Storage.items != null;
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
            return StorageTerminalManager.IsTerminal(block);
        }
    }
}
