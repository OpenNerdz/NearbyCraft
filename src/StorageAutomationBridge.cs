using System;
using UnityEngine;

namespace NearbyCraft
{
    // Optional, narrow integration API. DeepBore binds this method only when this mod is loaded.
    // The network's existing chest limits, access rules, ranges and transaction validation apply.
    public static class StorageAutomationBridge
    {
        public const int MaxOutputLinkRange = 200;

        public static bool IsNetworkTerminal(Block block) { return StorageTerminalManager.IsTerminal(block); }

        public static int ExportFromStorage(World world, EntityPlayerLocal player, Vector3i terminal,
            TileEntityComposite source, int[] allowedTypes)
        {
            var sourceStorage = source?.GetFeature<TEFeatureStorage>();
            if (sourceStorage == null || !StorageOutputPlanner.HasExportable(sourceStorage.items,
                sourceStorage.HasSlotLocksSupport ? sourceStorage.SlotLocks : null, allowedTypes)) return 0;
            if (!NearbyCraftMod.CanUseLocalStorage || NearbyCraftMod.Config == null || !NearbyCraftMod.Config.Enabled
                || world == null || GameManager.Instance.World != world || GameManager.Instance.IsPaused()
                || player == null || player.IsDead() || world.GetPrimaryPlayer() != player
                || source == null || !source.LocalPlayerIsOwner || !source.PlayerPlaced || source.IsRemoving
                || source.IsUserAccessing() || world.GetTileEntity(source.ToWorldPos()) != source
                || (source.ToWorldPos().ToVector3() - terminal.ToVector3()).sqrMagnitude > MaxOutputLinkRange * MaxOutputLinkRange
                || world.GetChunkFromWorldPos(terminal) == null || world.IsWithinTraderArea(terminal)
                || !world.CanPlaceBlockAt(terminal, GameManager.Instance.GetPersistentLocalPlayer())
                || !IsNetworkTerminal(world.GetBlock(terminal).Block)
                || !(world.GetTileEntity(terminal) is TileEntityComposite console) || !console.LocalPlayerIsOwner
                || console.IsUserAccessing() || !WorkshopManager.Accessible(source) || !WorkshopManager.Accessible(console)
                || StorageTerminalManager.IsOpen || allowedTypes == null || allowedTypes.Length == 0) return 0;
            var ui = LocalPlayerUI.GetUIForPlayer(player);
            if (ui == null || ui.xui == null || !ui.xui.DragAndDropWindow.CurrentStack.IsEmpty()
                || ui.windowManager.IsWindowOpen(LoadoutLockerManager.WindowGroupId)) return 0;
            var network = new StorageNetworkSession(world, player, terminal, NearbyCraftMod.Config, source.ToWorldPos(), true);
            network.Rescan(includeItemCatalog: false); // Export needs containers, not a sorted/localized UI catalog.
            return network.CollectStorageOutput(source, allowedTypes);
        }
    }

    internal sealed partial class StorageNetworkSession
    {
        internal int CollectStorageOutput(TileEntityComposite origin, int[] allowedTypes)
        {
            var storage = origin.GetFeature<TEFeatureStorage>();
            if (storage == null || !storage.bPlayerStorage || storage.items == null || !IsAvailable || AutomationBusy) return 0;
            var live = storage.items;
            var before = ItemStack.Clone(live);
            var after = ItemStack.Clone(live);
            var locks = new bool[live.Length];
            for (int i = 0; i < locks.Length; i++)
                locks[i] = storage.HasSlotLocksSupport && storage.SlotLocks != null && i < storage.SlotLocks.Length && storage.SlotLocks[i];
            var transaction = BeginTransaction(origin); // Never deposit back into the miner being debited.
            int moved = StorageOutputPlanner.Collect(transaction.Plan, after, locks, allowedTypes);
            if (moved == 0 || !Commit(transaction, () =>
                {
                    if (origin.IsRemoving || origin.IsUserAccessing() || !origin.LocalPlayerIsOwner
                        || world.GetTileEntity(origin.ToWorldPos()) != origin || !ReferenceEquals(live, storage.items)
                        || !SameSlots(live, before) || !CanAccess(origin)
                        || world.GetTileEntity(terminalPosition)?.IsUserAccessing() != false) return false;
                    for (int i = 0; i < locks.Length; i++)
                        if (locks[i] != (storage.HasSlotLocksSupport && storage.SlotLocks != null
                            && i < storage.SlotLocks.Length && storage.SlotLocks[i])) return false;
                    return true;
                }, () => { for (int i = 0; i < live.Length; i++) live[i] = after[i]; })) return 0;
            try { origin.SetModified(); }
            catch (Exception e) { Log.Error("[NearbyCraft] Output committed; source notification failed: " + e); }
            StorageTerminalManager.RequestItemsRefresh();
            return moved;
        }
    }
}
