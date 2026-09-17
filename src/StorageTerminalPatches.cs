using System;
using Audio;
using HarmonyLib;
using Platform;

namespace NearbyCraft
{
    [HarmonyPatch(typeof(BlockCompositeTileEntity), nameof(BlockCompositeTileEntity.OnBlockActivated))]
    internal static class StorageTerminalActivationPatch
    {
        private const string StorageSearchCommand = "TEFeatureStorage:Search";

        [HarmonyPrefix]
        private static bool Prefix(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            if (_blockValue.Block == null
                || !string.Equals(_blockValue.Block.GetBlockName(), StorageTerminalManager.BlockName, StringComparison.Ordinal)
                || (!string.Equals(_commandName, StorageSearchCommand, StringComparison.Ordinal)
                    && !string.Equals(_commandName, "Search", StringComparison.Ordinal)))
            {
                return true;
            }

            TileEntity tileEntity = _world.GetTileEntity(_blockPos);
            ILockable lockable;
            if (tileEntity != null
                && tileEntity.TryGetSelfOrFeature<ILockable>(out lockable)
                && lockable != null
                && lockable.IsLocked()
                && !lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier))
            {
                Manager.BroadcastPlayByLocalPlayer(_blockPos.ToVector3() + UnityEngine.Vector3.one * 0.5f, "Misc/locked");
                __result = false;
                return false;
            }

            LocalPlayerUI playerUi = LocalPlayerUI.GetUIForPlayer(_player);
            XUiC_StorageTerminalWindowGroup group = playerUi == null
                ? null
                : playerUi.xui.FindWindowGroupByName(StorageTerminalManager.WindowGroupId) as XUiC_StorageTerminalWindowGroup;
            if (group == null)
            {
                Log.Error("[NearbyCraft] Storage terminal XUi group was not found.");
                __result = false;
                return false;
            }

            _player.AimingGun = false;
            Log.Out("[NearbyCraft] Opening storage terminal at {0}.", _blockPos);
            group.SetTerminal(_blockPos);
            playerUi.windowManager.Open(StorageTerminalManager.WindowGroupId, true);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.HandleMoveToPreferredLocation))]
    internal static class StorageTerminalShiftClickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(XUiC_ItemStack __instance)
        {
            if (!StorageTerminalManager.IsOpen || __instance == null || __instance.ItemStack == null || __instance.ItemStack.IsEmpty())
            {
                return true;
            }

            StorageNetworkSession session = StorageTerminalManager.ActiveSession;
            if (__instance is XUiC_StorageTerminalItemStack)
            {
                ItemStack requested = __instance.ItemStack.Clone();
                int withdrawn = session.Withdraw(requested, requested.count);
                if (withdrawn <= 0)
                {
                    Manager.PlayInsidePlayerHead("ui_denied");
                    return false;
                }

                ItemStack moved = requested.Clone();
                moved.count = withdrawn;
                __instance.xui.PlayerInventory.AddItem(moved);
                if (moved.count > 0)
                {
                    session.Deposit(moved);
                }
                __instance.PlayPickupSound(requested);
                StorageTerminalManager.RequestItemsRefresh();
                return false;
            }

            if (__instance.StackLocation != XUiC_ItemStack.StackLocationTypes.Backpack
                && __instance.StackLocation != XUiC_ItemStack.StackLocationTypes.ToolBelt)
            {
                return true;
            }

            ItemStack depositedStack = __instance.ItemStack.Clone();
            ItemStack remainder = depositedStack.Clone();
            if (__instance.StackLocation == XUiC_ItemStack.StackLocationTypes.ToolBelt)
            {
                remainder.Deactivate();
            }
            int deposited = session.Deposit(remainder);
            if (deposited <= 0)
            {
                Manager.PlayInsidePlayerHead("ui_denied");
                GameManager.ShowTooltip(__instance.xui.playerUI.entityPlayer, "Storage network is full");
                return false;
            }

            __instance.ItemStack = remainder.count > 0 ? remainder : ItemStack.Empty;
            __instance.PlayPlaceSound(depositedStack);
            StorageTerminalManager.RequestItemsRefresh();
            return false;
        }
    }
}
