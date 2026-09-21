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
            if (!StorageTerminalManager.IsTerminal(_blockValue.Block)
                || (!string.Equals(_commandName, StorageSearchCommand, StringComparison.Ordinal)
                    && !string.Equals(_commandName, "Search", StringComparison.Ordinal)))
            {
                return true;
            }

            if (!NearbyCraftMod.CanUseLocalStorage)
            {
                GameManager.ShowTooltip(_player, "NearbyCraft storage is available in solo worlds only. Multiplayer needs server-authoritative transfers.");
                __result = false;
                return false;
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
            if (__instance.StackLock || !__instance.xui.DragAndDropWindow.CurrentStack.IsEmpty()) return false;
            if (__instance is XUiC_StorageTerminalItemStack)
            {
                ItemStack requested = __instance.ItemStack.Clone();
                if (!TerminalRules.CanShiftToInventory(
                    requested.CanMoveTo(XUiC_ItemStack.StackLocationTypes.Backpack),
                    requested.CanMoveTo(XUiC_ItemStack.StackLocationTypes.ToolBelt))) return false;
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
                    // If a source disappeared during an inventory event, retain the
                    // real remainder on the cursor instead of discarding it.
                    if (moved.count > 0) __instance.xui.DragAndDropWindow.CurrentStack = moved;
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
            if (!depositedStack.CanMoveTo(XUiC_ItemStack.StackLocationTypes.LootContainer)) return false;
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

    [HarmonyPatch(typeof(XUiC_ItemStack), "HandleStackSwap")]
    internal static class StorageTerminalStackPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(XUiC_ItemStack __instance)
        {
            var terminal = __instance as XUiC_StorageTerminalItemStack;
            if (terminal == null) return true;
            terminal.SwapItem();
            terminal.HandleClickComplete();
            return false;
        }
    }

    [HarmonyPatch(typeof(XUiC_ItemStack), "HandlePartialStackPickup")]
    internal static class StorageTerminalHalfStackPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(XUiC_ItemStack __instance)
        {
            var terminal = __instance as XUiC_StorageTerminalItemStack;
            if (terminal == null) return true;
            terminal.PickUpHalf();
            terminal.HandleClickComplete();
            return false;
        }
    }

    // ItemActionEntryUse assumes its item-stack controller owns a real inventory
    // slot. Terminal cells are projections, so vanilla would apply food, drink,
    // medicine, book, or bundle effects while only decrementing the projection.
    [HarmonyPatch(typeof(ItemActionEntryUse))]
    internal static class StorageTerminalUseActionPatch
    {
        private const string MoveToInventoryMessage = "Move this item to your backpack before using it.";

        [HarmonyPatch(nameof(ItemActionEntryUse.RefreshEnabled))]
        [HarmonyPostfix]
        private static void RefreshEnabledPostfix(ItemActionEntryUse __instance)
        {
            if (IsTerminalItem(__instance)) __instance.Enabled = false;
        }

        [HarmonyPatch(nameof(ItemActionEntryUse.OnActivated))]
        [HarmonyPrefix]
        private static bool OnActivatedPrefix(ItemActionEntryUse __instance)
        {
            if (!IsTerminalItem(__instance)) return true;
            ShowMoveToInventory(__instance);
            return false;
        }

        [HarmonyPatch(nameof(ItemActionEntryUse.OnDisabledActivate))]
        [HarmonyPrefix]
        private static bool OnDisabledActivatePrefix(ItemActionEntryUse __instance)
        {
            if (!IsTerminalItem(__instance)) return true;
            ShowMoveToInventory(__instance);
            return false;
        }

        private static bool IsTerminalItem(ItemActionEntryUse action)
        {
            return action != null && action.ItemController is XUiC_StorageTerminalItemStack;
        }

        private static void ShowMoveToInventory(ItemActionEntryUse action)
        {
            XUiController controller = action.ItemController;
            EntityPlayerLocal player = controller == null || controller.xui == null || controller.xui.playerUI == null
                ? null
                : controller.xui.playerUI.entityPlayer;
            if (player != null) GameManager.ShowTooltip(player, MoveToInventoryMessage);
        }
    }

    // The standalone console does not open the vanilla window selector, which
    // normally owns InMenu. Set it before the cursor's own Update regardless of UI order.
    [HarmonyPatch(typeof(XUiC_DragAndDropWindow), nameof(XUiC_DragAndDropWindow.Update))]
    internal static class StorageTerminalCursorPatch
    {
        [HarmonyPrefix]
        private static void Prefix(XUiC_DragAndDropWindow __instance)
        {
            if (StorageTerminalManager.IsOpen && StorageTerminalManager.ActiveWindow.xui == __instance.xui)
                __instance.InMenu = true;
        }
    }
}
