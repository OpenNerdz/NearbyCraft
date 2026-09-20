using System;
using Audio;
using UnityEngine;
using UnityEngine.Scripting;

namespace NearbyCraft
{
    internal static class StorageTerminalManager
    {
        internal const string BlockName = TerminalRules.BlockName;
        internal const string WindowGroupId = "nearbycraft_storage_terminal";

        internal static int GetTier(Block block)
        {
            return block == null ? 0 : TerminalRules.GetTier(block.GetBlockName());
        }

        internal static bool IsTerminal(Block block)
        {
            return GetTier(block) > 0;
        }

        internal static StorageNetworkSession ActiveSession { get; private set; }
        internal static XUiC_StorageTerminalWindowGroup ActiveWindow { get; private set; }

        internal static bool IsOpen
        {
            get
            {
                return ActiveSession != null
                    && ActiveWindow != null
                    && ActiveWindow.xui != null
                    && ActiveWindow.xui.playerUI.windowManager.IsWindowOpen(WindowGroupId);
            }
        }

        internal static void Begin(XUiC_StorageTerminalWindowGroup window, StorageNetworkSession session)
        {
            ActiveWindow = window;
            ActiveSession = session;
        }

        internal static void End(XUiC_StorageTerminalWindowGroup window)
        {
            if (ActiveWindow == window)
            {
                ActiveWindow = null;
                ActiveSession = null;
            }
        }

        internal static void RequestItemsRefresh()
        {
            if (ActiveWindow != null)
            {
                ActiveWindow.RequestItemsRefresh();
            }
        }
    }

    [Preserve]
    public sealed class XUiC_StorageTerminalWindowGroup : XUiController
    {
        private Vector3i terminalPosition;
        private bool hasTerminalPosition;
        private StorageNetworkSession session;
        private XUiC_StorageTerminalGrid grid;
        private XUiC_TextInput searchInput;
        private XUiV_ScrollBar scrollBar;
        private float nextRefresh;
        private bool previousCursorMenu;
        private string actionMessage;
        private float actionMessageUntil;

        public override void Init()
        {
            base.Init();
            grid = GetChildByType<XUiC_StorageTerminalGrid>();
            searchInput = GetChildById("nearbyCraftTerminalSearch") as XUiC_TextInput;
            if (searchInput != null)
            {
                searchInput.OnChangeHandler += SearchChanged;
                searchInput.OnSubmitHandler += SearchSubmitted;
            }

            BindButton("nearbyCraftTerminalSort", SortPressed);
            BindButton("nearbyCraftTerminalAutoFocus", AutoFocusPressed);
            BindButton("nearbyCraftTerminalDeposit", DepositAllPressed);
            BindButton("nearbyCraftTerminalDepositMatching", DepositMatchingPressed);
            BindButton("nearbyCraftTerminalReserve", ReservePressed, true);
            BindButton("nearbyCraftTerminalClearReserve", ClearReservePressed);
            BindButton("nearbyCraftTerminalRefresh", RefreshPressed);
            BindButton("nearbyCraftTerminalScrollUp", ScrollUpPressed);
            BindButton("nearbyCraftTerminalScrollDown", ScrollDownPressed);

            XUiController scrollBarController = GetChildById("nearbyCraftTerminalScrollbar");
            scrollBar = scrollBarController == null ? null : scrollBarController.ViewComponent as XUiV_ScrollBar;
            if (scrollBar != null)
            {
                scrollBar.ScrollBar.fillDirection = UIProgressBar.FillDirection.TopToBottom;
                scrollBar.Connect(ScrollWheel);
            }
            XUiController scrollArea = GetChildById("nearbyCraftTerminalScrollArea");
            if (scrollArea != null)
            {
                BindScrollTree(scrollArea);
            }
        }

        public override void Update(float dt)
        {
            if (session != null)
            {
                if (!session.IsAvailable)
                {
                    xui.playerUI.windowManager.Close(StorageTerminalManager.WindowGroupId);
                    return;
                }
                xui.DragAndDropWindow.InMenu = true;
                // Don't move cells underneath a held mouse button or carried stack.
                if (Time.realtimeSinceStartup >= nextRefresh && xui.DragAndDropWindow.CurrentStack.IsEmpty()
                    && !Input.GetMouseButton(0) && !Input.GetMouseButton(1))
                {
                    nextRefresh = Time.realtimeSinceStartup + 2f;
                    session.Rescan();
                    grid.RefreshFromSession();
                    SetAllChildrenDirty();
                }
            }
            base.Update(dt);
            if (session == null || scrollBar == null || session.MaxScrollRow <= 0)
            {
                return;
            }

            int requestedRow = Mathf.RoundToInt(scrollBar.ScrollPosition * session.MaxScrollRow);
            if (session.SetScrollRow(requestedRow) && grid != null)
            {
                grid.RefreshFromSession();
                SetAllChildrenDirty();
            }
        }

        internal void SetTerminal(Vector3i position)
        {
            terminalPosition = position;
            hasTerminalPosition = true;
        }

        public override void OnOpen()
        {
            World world = GameManager.Instance == null ? null : GameManager.Instance.World;
            EntityPlayerLocal player = xui == null || xui.playerUI == null ? null : xui.playerUI.entityPlayer;
            session = hasTerminalPosition && world != null && player != null
                ? new StorageNetworkSession(world, player, terminalPosition, NearbyCraftMod.Config)
                : null;

            if (session != null)
            {
                session.Rescan();
            }
            StorageTerminalManager.Begin(this, session);
            if (grid != null)
            {
                grid.SetSession(session);
            }
            if (searchInput != null)
            {
                searchInput.Text = string.Empty;
                searchInput.FocusOnOpen = NearbyCraftMod.Config == null || NearbyCraftMod.Config.TerminalAutoFocusSearch;
            }

            base.OnOpen();
            actionMessage = null;
            previousCursorMenu = xui.DragAndDropWindow.InMenu;
            xui.DragAndDropWindow.InMenu = true;
            nextRefresh = Time.realtimeSinceStartup + 2f;
            if (grid != null)
            {
                grid.RefreshFromSession();
            }
            xui.playerUI.windowManager.Open("backpack", false);
            xui.RecenterWindowGroup(windowGroup);
            SetAllChildrenDirty();
            Manager.BroadcastPlayByLocalPlayer(terminalPosition.ToVector3() + Vector3.one * 0.5f, "open_workbench");
        }

        public override void OnClose()
        {
            if (xui != null && xui.DragAndDropWindow != null)
            {
                xui.DragAndDropWindow.PlaceItemBackInInventory();
                xui.DragAndDropWindow.InMenu = previousCursorMenu;
            }
            base.OnClose();
            if (xui != null && xui.playerUI != null)
            {
                xui.playerUI.windowManager.Close("backpack");
            }
            if (hasTerminalPosition)
            {
                Manager.BroadcastPlayByLocalPlayer(terminalPosition.ToVector3() + Vector3.one * 0.5f, "close_workbench");
            }
            StorageTerminalManager.End(this);
            session = null;
            hasTerminalPosition = false;
            if (grid != null)
            {
                grid.SetSession(null);
            }
        }

        internal void RequestItemsRefresh()
        {
            if (grid != null)
            {
                grid.RequestRefresh();
            }
        }

        private void BindButton(string id, XUiEvent_OnPressEventHandler handler, bool rightClick = false)
        {
            XUiController button = GetChildById(id);
            if (button != null)
            {
                button.OnPress -= handler;
                button.OnPress += handler;
                if (rightClick)
                {
                    button.OnRightPress -= handler;
                    button.OnRightPress += handler;
                }
            }
            else Log.Error("[NearbyCraft] Terminal button was not found: {0}", id);
        }

        private void SearchChanged(XUiController sender, string text, bool changeFromCode)
        {
            if (session == null)
            {
                return;
            }
            session.SetSearch(text);
            grid.RefreshFromSession();
            SetAllChildrenDirty();
        }

        private void SearchSubmitted(XUiController sender, string text)
        {
            SearchChanged(sender, text, false);
        }

        private void SortPressed(XUiController sender, int mouseButton)
        {
            if (session == null)
            {
                return;
            }
            session.CycleSort();
            NearbyCraftMod.SetTerminalSort(session.Sort);
            grid.RefreshFromSession();
            SetAllChildrenDirty();
        }

        private void AutoFocusPressed(XUiController sender, int mouseButton)
        {
            bool enabled = NearbyCraftMod.Config == null || NearbyCraftMod.Config.TerminalAutoFocusSearch;
            NearbyCraftMod.SetTerminalAutoFocus(!enabled);
            if (searchInput != null)
            {
                searchInput.FocusOnOpen = !enabled;
                if (!enabled)
                {
                    searchInput.SetSelected(true, true);
                }
            }
            SetAllChildrenDirty();
        }

        private void DepositAllPressed(XUiController sender, int mouseButton)
        {
            DepositBackpack(false);
        }

        private void DepositMatchingPressed(XUiController sender, int mouseButton)
        {
            DepositBackpack(true);
        }

        private void DepositBackpack(bool matchingOnly)
        {
            if (session == null) return;
            int moved = session.DepositBackpack(xui.PlayerInventory, matchingOnly);
            Log.Out("[NearbyCraft] {0}: moved {1} items across {2} connected chests.",
                matchingOnly ? "Matching deposit" : "Deposit all", moved, session.ConnectedStorageCount);
            actionMessage = moved > 0 ? "DEPOSITED " + moved + " ITEMS"
                : matchingOnly ? "NO MATCHES / SPACE; CHECK LOCKS & RESERVES"
                : "NOTHING MOVED; CHECK CHEST SPACE & RESERVES";
            actionMessageUntil = Time.realtimeSinceStartup + 6f;
            grid.RefreshFromSession();
            SetAllChildrenDirty();
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, actionMessage);
        }

        private void ReservePressed(XUiController sender, int mouseButton)
        {
            SetReserve(TerminalRules.IsRightClick(mouseButton));
        }

        private void ClearReservePressed(XUiController sender, int mouseButton)
        {
            SetReserve(true);
        }

        private void SetReserve(bool clear)
        {
            ItemStack held = xui.DragAndDropWindow.CurrentStack;
            if (held == null || held.IsEmpty())
            {
                GameManager.ShowTooltip(xui.playerUI.entityPlayer, "Hold an item stack, then click KEEP to reserve that amount or CLEAR KEEP to clear its reserve.");
                return;
            }
            NearbyCraftMod.SetReserve(held, clear);
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, clear ? "Reserve cleared for this item"
                : "Bulk deposit will keep " + held.count + " of this item in eligible backpack slots.");
        }

        private void RefreshPressed(XUiController sender, int mouseButton)
        {
            if (session == null)
            {
                return;
            }
            session.Rescan();
            grid.RefreshFromSession();
            SetAllChildrenDirty();
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, "Storage network refreshed");
        }

        private void ScrollUpPressed(XUiController sender, int mouseButton)
        {
            ScrollByRows(-1);
        }

        private void ScrollDownPressed(XUiController sender, int mouseButton)
        {
            ScrollByRows(1);
        }

        private void ScrollWheel(XUiController sender, float delta)
        {
            if (Mathf.Approximately(delta, 0f))
            {
                return;
            }
            ScrollByRows(delta > 0f ? -1 : 1);
        }

        private void ScrollByRows(int rows)
        {
            if (session != null && session.ScrollRows(rows) && grid != null)
            {
                grid.RefreshFromSession();
                SetAllChildrenDirty();
            }
        }

        private void BindScrollTree(XUiController controller)
        {
            if (controller.ViewComponent != null)
            {
                controller.ViewComponent.EventOnScroll = true;
                controller.OnScroll -= ScrollWheel;
                controller.OnScroll += ScrollWheel;
            }
            for (int i = 0; i < controller.Children.Count; i++)
            {
                BindScrollTree(controller.Children[i]);
            }
        }

        internal void SyncScrollBar()
        {
            if (scrollBar == null)
            {
                return;
            }

            int totalRows = session == null ? 0 : session.TotalRows;
            int maxScrollRow = session == null ? 0 : session.MaxScrollRow;
            float size = totalRows <= 0 ? 1f : Mathf.Min(1f, StorageNetworkSession.VisibleRows / (float)totalRows);
            float position = maxScrollRow <= 0 ? 0f : session.ScrollRow / (float)maxScrollRow;
            if (!Mathf.Approximately(scrollBar.ScrollBar.barSize, size))
            {
                scrollBar.ScrollBar.barSize = size;
            }
            if (!Mathf.Approximately(scrollBar.ScrollPosition, position))
            {
                scrollBar.ScrollPosition = position;
            }
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            switch (bindingName)
            {
                case "terminal_status":
                    if (!string.IsNullOrEmpty(actionMessage) && Time.realtimeSinceStartup < actionMessageUntil)
                    {
                        value = actionMessage;
                        return true;
                    }
                    if (session == null)
                    {
                        int radius = NearbyCraftMod.Config == null ? 15 : NearbyCraftMod.Config.TerminalRange;
                        value = "0 CONNECTED  •  Place storage within " + radius + " blocks";
                    }
                    else
                    {
                        value = "T" + session.Tier + "  •  " + session.ConnectedStorageCount + "/" + session.ChestLimit + " CHESTS  •  "
                            + (session.OverflowCount > 0 ? "+" + session.OverflowCount + " OUT OF CAP"
                                : session.ConnectedStorageCount == 0 ? "PLACE CHESTS IN RANGE" : session.TotalItemCount + " ITEMS");
                    }
                    return true;
                case "terminal_sort":
                    value = session == null ? "NAME" : session.Sort.ToString().ToUpperInvariant();
                    return true;
                case "terminal_autofocus_enabled":
                    value = (NearbyCraftMod.Config == null || NearbyCraftMod.Config.TerminalAutoFocusSearch).ToString();
                    return true;
                case "terminal_autofocus_color":
                    value = NearbyCraftMod.Config == null || NearbyCraftMod.Config.TerminalAutoFocusSearch
                        ? "70,190,90,255"
                        : "125,125,125,255";
                    return true;
                case "terminal_autofocus_tooltip":
                    value = NearbyCraftMod.Config == null || NearbyCraftMod.Config.TerminalAutoFocusSearch
                        ? "Search is focused automatically when the console opens"
                        : "Click to focus search automatically when the console opens";
                    return true;
                case "terminal_scroll_range":
                    value = session == null || session.ResultCount == 0
                        ? "0 ITEMS"
                        : (session.FirstVisibleIndex + 1) + "-" + session.LastVisibleIndex + " / " + session.ResultCount;
                    return true;
                case "terminal_can_scroll_up":
                    value = (session != null && session.ScrollRow > 0).ToString();
                    return true;
                case "terminal_can_scroll_down":
                    value = (session != null && session.ScrollRow < session.MaxScrollRow).ToString();
                    return true;
                case "terminal_result_count":
                    value = session == null ? "0" : session.ResultCount.ToString();
                    return true;
                default:
                    return base.GetBindingValueInternal(ref value, bindingName);
            }
        }
    }

    [Preserve]
    public sealed class XUiC_StorageTerminalGrid : XUiC_ItemStackGrid
    {
        private StorageNetworkSession session;
        private ItemStack[] displayed = ItemStack.CreateArray(StorageNetworkSession.VisibleSlotCount);
        private bool refreshRequested;

        public override XUiC_ItemStack.StackLocationTypes StackLocation
        {
            get { return XUiC_ItemStack.StackLocationTypes.LootContainer; }
        }

        internal void SetSession(StorageNetworkSession value)
        {
            session = value;
            refreshRequested = false;
        }

        internal void RequestRefresh()
        {
            refreshRequested = true;
        }

        internal void RefreshFromSession()
        {
            refreshRequested = false;
            displayed = session == null
                ? ItemStack.CreateArray(StorageNetworkSession.VisibleSlotCount)
                : session.GetVisibleStacks();
            SetStacks(displayed);
            IsDirty = true;
            XUiC_StorageTerminalWindowGroup owner = GetParentByType<XUiC_StorageTerminalWindowGroup>();
            if (owner != null)
            {
                owner.SyncScrollBar();
            }
        }

        public override void Update(float dt)
        {
            base.Update(dt);
            if (refreshRequested && session != null)
            {
                session.RebuildItems();
                RefreshFromSession();
                windowGroup.Controller.SetAllChildrenDirty();
            }
        }

        public override void HandleSlotChangedEvent(int slotNumber, ItemStack stack)
        {
            if (session == null || slotNumber < 0 || slotNumber >= displayed.Length)
            {
                return;
            }

            // This grid is a projection, never an inventory. All mutations must go
            // through the validated cursor/shift-click transaction paths.
            refreshRequested = true;
        }

        public override ItemStack[] GetSlots()
        {
            return ItemStack.Clone(displayed);
        }

        public override void UpdateBackend(ItemStack[] stackList)
        {
        }
    }

    [Preserve]
    public sealed class XUiC_StorageTerminalItemStack : XUiC_ItemStack
    {
        public override bool CanSwap(ItemStack stack)
        {
            StorageNetworkSession session = StorageTerminalManager.ActiveSession;
            return !StackLock && session != null && (stack.IsEmpty()
                || (stack.CanMoveTo(StackLocation, SlotNumber) && session.CanDepositAfterWithdraw(ItemStack, stack)));
        }

        public override void SwapItem()
        {
            StorageNetworkSession session = StorageTerminalManager.ActiveSession;
            if (session == null || StackLock)
            {
                return;
            }

            ItemStack held = xui.DragAndDropWindow.CurrentStack;
            if (!held.IsEmpty() && (!held.CanMoveTo(StackLocation, SlotNumber) || !held.itemValue.ItemClassOrMissing.CanPlaceInContainer()))
            {
                Manager.PlayInsidePlayerHead("ui_denied");
                GameManager.ShowTooltip(xui.playerUI.entityPlayer, "Quest Items cannot be placed in containers.");
                return;
            }

            ItemStack newHeld;
            if (!session.TrySwap(ItemStack, held, out newHeld))
            {
                Manager.PlayInsidePlayerHead("ui_denied");
                return;
            }

            xui.DragAndDropWindow.CurrentStack = newHeld;
            xui.DragAndDropWindow.PickUpType = StackLocationTypes.LootContainer;
            if (held.IsEmpty())
            {
                PlayPickupSound(newHeld);
            }
            else
            {
                PlayPlaceSound(held);
            }
            StorageTerminalManager.RequestItemsRefresh();
        }

        internal void PickUpHalf()
        {
            StorageNetworkSession session = StorageTerminalManager.ActiveSession;
            if (session == null || StackLock || ItemStack.IsEmpty() || !xui.DragAndDropWindow.CurrentStack.IsEmpty()) return;
            int amount = session.Withdraw(ItemStack, Math.Max(1, ItemStack.count / 2));
            if (amount <= 0) return;
            ItemStack held = ItemStack.Clone();
            held.count = amount;
            xui.DragAndDropWindow.CurrentStack = held;
            xui.DragAndDropWindow.PickUpType = StackLocationTypes.LootContainer;
            StorageTerminalManager.RequestItemsRefresh();
        }

        public override void HandleDropOne()
        {
            StorageNetworkSession session = StorageTerminalManager.ActiveSession;
            ItemStack held = xui.DragAndDropWindow.CurrentStack;
            if (session == null || StackLock || held == null || held.IsEmpty() || !held.CanMoveTo(StackLocation, SlotNumber))
            {
                return;
            }
            if (!ItemStack.IsEmpty() && !StorageNetworkSession.ItemsMatch(ItemStack, held))
            {
                return;
            }

            ItemStack one = held.Clone();
            one.count = 1;
            if (session.Deposit(one) != 1)
            {
                Manager.PlayInsidePlayerHead("ui_denied");
                return;
            }

            ItemStack remainder = held.Clone();
            remainder.count--;
            xui.DragAndDropWindow.CurrentStack = remainder.count > 0 ? remainder : ItemStack.Empty;
            xui.DragAndDropWindow.PickUpType = StackLocationTypes.LootContainer;
            PlayPlaceSound(held);
            StorageTerminalManager.RequestItemsRefresh();
        }
    }
}
