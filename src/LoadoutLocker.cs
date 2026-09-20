using System;
using System.Collections.Generic;
using Audio;
using HarmonyLib;
using Platform;
using UnityEngine;
using UnityEngine.Scripting;

namespace NearbyCraft
{
    internal static class LoadoutLockerManager
    {
        internal const string BlockName = "nearbyCraftLoadoutLocker";
        internal const string WindowGroupId = "nearbycraft_loadout_locker";

        internal static bool IsLocker(Block block)
        {
            return block != null && string.Equals(block.GetBlockName(), BlockName, StringComparison.Ordinal);
        }

        internal static bool TryFindTerminal(World world, Vector3i lockerPosition, int range, out Vector3i terminalPosition)
        {
            terminalPosition = default(Vector3i);
            if (world == null) return false;
            try
            {
                Vector3 center = lockerPosition.ToVector3() + Vector3.one * 0.5f;
                float maximum = range * range;
                float best = float.MaxValue;
                bool found = false;
                int minChunkX = Utils.Fastfloor((center.x - range) / 16f);
                int maxChunkX = Utils.Fastfloor((center.x + range) / 16f);
                int minChunkZ = Utils.Fastfloor((center.z - range) / 16f);
                int maxChunkZ = Utils.Fastfloor((center.z + range) / 16f);

                for (int chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
                for (int chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
                {
                    Chunk chunk = world.GetChunkSync(chunkX, chunkZ) as Chunk;
                    List<TileEntity> entities = chunk == null || chunk.tileEntities == null ? null : chunk.tileEntities.list;
                    if (entities == null) continue;
                    for (int i = 0; i < entities.Count; i++)
                    {
                        TileEntity entity = entities[i];
                        if (entity == null || entity.IsRemoving || entity.IsUserAccessing()
                            || !StorageTerminalManager.IsTerminal(entity.block) || !CanAccess(entity)) continue;
                        float distance = (entity.ToWorldCenterPos() - center).sqrMagnitude;
                        Vector3i candidate = entity.ToWorldPos();
                        if (distance > maximum || (found && distance > best)) continue;
                        if (found && Mathf.Approximately(distance, best) && Compare(candidate, terminalPosition) >= 0) continue;
                        terminalPosition = candidate;
                        best = distance;
                        found = true;
                    }
                }
                return found;
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Loadout locker link scan failed safely: {0}", exception.Message);
                return false;
            }
        }

        internal static bool CanAccess(TileEntity tileEntity)
        {
            ILockable lockable;
            return !tileEntity.TryGetSelfOrFeature<ILockable>(out lockable) || lockable == null
                || !lockable.IsLocked() || lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier);
        }

        private static int Compare(Vector3i left, Vector3i right)
        {
            int order = left.x.CompareTo(right.x);
            if (order != 0) return order;
            order = left.y.CompareTo(right.y);
            return order != 0 ? order : left.z.CompareTo(right.z);
        }
    }

    internal sealed class PlayerLoadoutState
    {
        internal ItemStack[] Equipment;
        internal ItemStack[] Toolbelt;
        internal ItemStack[] Backpack;
        internal bool[] BackpackLocks;
        internal global::Equipment PreparedEquipment;

        internal static PlayerLoadoutState Capture(EntityPlayerLocal player, XUiM_PlayerInventory inventory)
        {
            if (player == null || player.equipment == null || inventory == null) return null;
            int equipmentSlots = player.equipment.GetSlotCount();
            var state = new PlayerLoadoutState
            {
                Equipment = ItemStack.CreateArray(equipmentSlots),
                Toolbelt = ItemStack.Clone(inventory.GetToolbeltItemStacks()),
                Backpack = ItemStack.Clone(inventory.GetBackpackItemStacks())
            };
            for (int i = 0; i < equipmentSlots; i++)
            {
                ItemValue value = player.equipment.GetSlotItem(i);
                ItemStack equipped = value == null ? ItemStack.Empty.Clone() : new ItemStack(value.Clone(), 1);
                state.Equipment[i] = equipped.IsEmpty() ? ItemStack.Empty.Clone() : equipped;
            }
            PackedBoolArray locks = inventory.Backpack.LockedSlots;
            state.BackpackLocks = new bool[state.Backpack.Length];
            for (int i = 0; i < state.BackpackLocks.Length; i++)
                state.BackpackLocks[i] = locks != null && i < locks.Length && locks[i];
            return state;
        }

        internal bool MatchesLive(EntityPlayerLocal player, XUiM_PlayerInventory inventory)
        {
            PlayerLoadoutState live = Capture(player, inventory);
            return live != null && Equal(Equipment, live.Equipment) && Equal(Toolbelt, live.Toolbelt)
                && Equal(Backpack, live.Backpack) && Equal(BackpackLocks, live.BackpackLocks);
        }

        internal void Apply(EntityPlayerLocal player, XUiM_PlayerInventory inventory)
        {
            PackedBoolArray locks = inventory.Backpack.LockedSlots;
            if (locks != null)
                for (int i = 0; i < Math.Min(locks.Length, BackpackLocks.Length); i++) locks[i] = BackpackLocks[i];
            inventory.SetToolbeltItemStacks(ItemStack.Clone(Toolbelt));
            inventory.SetBackpackItemStacks(ItemStack.Clone(Backpack));
            player.equipment.Apply(PreparedEquipment, true);
        }

        private static bool Equal(ItemStack[] left, ItemStack[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (!StorageTransferPlan.ExactEquals(left[i], right[i])) return false;
            return true;
        }

        private static bool Equal(bool[] left, bool[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }

    internal static class PlayerLoadoutBuilder
    {
        internal static LoadoutProfileData CaptureProfile(PlayerLoadoutState state)
        {
            var profile = new LoadoutProfileData
            {
                Equipment = Serialize(state.Equipment),
                Toolbelt = Serialize(state.Toolbelt),
                Backpack = new SerializedLoadoutItem[state.Backpack.Length],
                BackpackLocks = (bool[])state.BackpackLocks.Clone(),
                SavedUtc = DateTime.UtcNow.ToString("o")
            };
            for (int i = 0; i < state.Backpack.Length; i++)
                if (state.BackpackLocks[i]) profile.Backpack[i] = LoadoutProfileStore.Serialize(state.Backpack[i]);
            return profile;
        }

        internal static bool TryBuildTarget(LoadoutProfileData profile, PlayerLoadoutState current,
            EntityPlayerLocal player, XUiM_PlayerInventory inventory, out PlayerLoadoutState target,
            out List<ItemStack> currentManaged, out List<ItemStack> targetManaged, out string error)
        {
            target = null;
            currentManaged = null;
            targetManaged = null;
            error = null;
            if (profile == null || profile.Format != 1 || profile.Equipment == null || profile.Toolbelt == null
                || profile.Backpack == null || profile.BackpackLocks == null
                || profile.Equipment.Length != current.Equipment.Length
                || profile.Toolbelt.Length != current.Toolbelt.Length
                || profile.Backpack.Length != current.Backpack.Length
                || profile.BackpackLocks.Length != current.Backpack.Length)
            {
                error = "This profile uses a different inventory layout. Save it again.";
                return false;
            }

            ItemStack[] equipment = Deserialize(profile.Equipment, out error);
            if (equipment == null) return false;
            ItemStack[] toolbelt = Deserialize(profile.Toolbelt, out error);
            if (toolbelt == null) return false;
            target = new PlayerLoadoutState
            {
                Equipment = equipment,
                Toolbelt = toolbelt,
                Backpack = ItemStack.Clone(current.Backpack),
                BackpackLocks = (bool[])profile.BackpackLocks.Clone()
            };

            for (int i = 0; i < target.Backpack.Length; i++)
            {
                if (target.BackpackLocks[i])
                {
                    ItemStack stack;
                    if (!LoadoutProfileStore.TryDeserialize(profile.Backpack[i], out stack))
                    {
                        error = "A saved backpack item is no longer valid. Save this profile again.";
                        return false;
                    }
                    target.Backpack[i] = stack;
                }
                else if (current.BackpackLocks[i]) target.Backpack[i] = ItemStack.Empty.Clone();
            }

            for (int i = 0; i < target.Equipment.Length; i++)
            {
                ItemStack stack = target.Equipment[i];
                if (stack.IsEmpty()) continue;
                ItemClassArmor armor = stack.itemValue.ItemClassOrMissing as ItemClassArmor;
                if (stack.count != 1 || armor == null || !armor.CanEquip() || (int)armor.EquipSlot != i)
                {
                    error = "A saved equipment item no longer fits its slot. Save this profile again.";
                    return false;
                }
            }
            for (int i = 0; i < target.Toolbelt.Length; i++)
                if (!target.Toolbelt[i].IsEmpty() && !inventory.Toolbelt.CanMoveToSlot(target.Toolbelt[i], i))
                {
                    error = "A saved toolbelt item no longer fits its slot.";
                    return false;
                }
            for (int i = 0; i < target.Backpack.Length; i++)
                if (target.BackpackLocks[i] && !target.Backpack[i].IsEmpty()
                    && !target.Backpack[i].CanMoveTo(XUiC_ItemStack.StackLocationTypes.Backpack, i))
                {
                    error = "A saved supply item no longer fits its backpack slot.";
                    return false;
                }

            try
            {
                target.PreparedEquipment = player.equipment.Clone();
                for (int i = 0; i < target.Equipment.Length; i++)
                    target.PreparedEquipment.SetSlotItemRaw(i, target.Equipment[i].IsEmpty()
                        ? ItemStack.Empty.itemValue.Clone() : target.Equipment[i].itemValue.Clone());
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Could not stage equipment safely: {0}", exception.Message);
                error = "The equipment change could not be prepared safely.";
                return false;
            }

            currentManaged = new List<ItemStack>();
            targetManaged = new List<ItemStack>();
            Add(currentManaged, current.Equipment);
            Add(targetManaged, target.Equipment);
            Add(currentManaged, current.Toolbelt);
            Add(targetManaged, target.Toolbelt);
            for (int i = 0; i < current.Backpack.Length; i++)
                if (current.BackpackLocks[i] || target.BackpackLocks[i])
                {
                    currentManaged.Add(current.Backpack[i]);
                    targetManaged.Add(target.Backpack[i]);
                }
            return true;
        }

        private static SerializedLoadoutItem[] Serialize(ItemStack[] stacks)
        {
            var result = new SerializedLoadoutItem[stacks.Length];
            for (int i = 0; i < stacks.Length; i++) result[i] = LoadoutProfileStore.Serialize(stacks[i]);
            return result;
        }

        private static ItemStack[] Deserialize(SerializedLoadoutItem[] saved, out string error)
        {
            error = null;
            var result = ItemStack.CreateArray(saved.Length);
            for (int i = 0; i < saved.Length; i++)
                if (!LoadoutProfileStore.TryDeserialize(saved[i], out result[i]))
                {
                    error = "A saved item is no longer valid. Save this profile again.";
                    return null;
                }
            return result;
        }

        private static void Add(List<ItemStack> destination, ItemStack[] source)
        {
            for (int i = 0; i < source.Length; i++) destination.Add(source[i]);
        }
    }

    [Preserve]
    public sealed class XUiC_LoadoutLockerWindowGroup : XUiController
    {
        private Vector3i lockerPosition;
        private Vector3i terminalPosition;
        private bool hasContext;
        private bool previousCursorMenu;
        private StorageNetworkSession session;
        private int confirmSave = -1;
        private float confirmUntil;
        private string actionMessage;
        private float actionUntil;
        private float nextRefresh;

        internal void SetContext(Vector3i locker, Vector3i terminal)
        {
            lockerPosition = locker;
            terminalPosition = terminal;
            hasContext = true;
        }

        public override void Init()
        {
            base.Init();
            for (int i = 0; i < LoadoutProfileStore.ProfileCount; i++)
            {
                int profileIndex = i;
                Bind("nearbyCraftLoadoutSave" + (i + 1), (sender, button) => SaveProfile(profileIndex));
                Bind("nearbyCraftLoadoutApply" + (i + 1), (sender, button) => ApplyProfile(profileIndex));
            }
        }

        public override void OnOpen()
        {
            World world = GameManager.Instance == null ? null : GameManager.Instance.World;
            EntityPlayerLocal player = xui == null || xui.playerUI == null ? null : xui.playerUI.entityPlayer;
            session = hasContext && world != null && player != null
                ? new StorageNetworkSession(world, player, terminalPosition, NearbyCraftMod.Config, lockerPosition)
                : null;
            if (session != null) session.Rescan();
            previousCursorMenu = xui.DragAndDropWindow.InMenu;
            xui.DragAndDropWindow.InMenu = true;
            confirmSave = -1;
            actionMessage = null;
            nextRefresh = Time.realtimeSinceStartup + 2f;
            base.OnOpen();
            xui.playerUI.windowManager.Open("backpack", false);
            xui.RecenterWindowGroup(windowGroup);
            SetAllChildrenDirty();
            Manager.BroadcastPlayByLocalPlayer(lockerPosition.ToVector3() + Vector3.one * 0.5f, "open_locker");
        }

        public override void OnClose()
        {
            if (xui != null && xui.DragAndDropWindow != null)
            {
                xui.DragAndDropWindow.PlaceItemBackInInventory();
                xui.DragAndDropWindow.InMenu = previousCursorMenu;
            }
            base.OnClose();
            if (xui != null && xui.playerUI != null) xui.playerUI.windowManager.Close("backpack");
            if (hasContext) Manager.BroadcastPlayByLocalPlayer(lockerPosition.ToVector3() + Vector3.one * 0.5f, "close_locker");
            session = null;
            hasContext = false;
        }

        public override void Update(float dt)
        {
            if (session != null && Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + 2f;
                World world = GameManager.Instance == null ? null : GameManager.Instance.World;
                if (!session.IsAvailable || world == null || !LoadoutLockerManager.IsLocker(world.GetBlock(lockerPosition).Block))
                {
                    xui.playerUI.windowManager.Close(LoadoutLockerManager.WindowGroupId);
                    return;
                }
                session.Rescan();
                SetAllChildrenDirty();
            }
            if (confirmSave >= 0 && Time.realtimeSinceStartup >= confirmUntil)
            {
                confirmSave = -1;
                SetAllChildrenDirty();
            }
            base.Update(dt);
        }

        private void Bind(string id, XUiEvent_OnPressEventHandler handler)
        {
            XUiController button = GetChildById(id);
            if (button != null) button.OnPress += handler;
            else Log.Error("[NearbyCraft] Loadout locker button was not found: {0}", id);
        }

        private bool Ready(out EntityPlayerLocal player, out XUiM_PlayerInventory inventory)
        {
            player = xui == null || xui.playerUI == null ? null : xui.playerUI.entityPlayer;
            inventory = xui == null ? null : xui.PlayerInventory;
            if (session == null || !session.IsAvailable || player == null || inventory == null) return false;
            if (!xui.DragAndDropWindow.CurrentStack.IsEmpty() || player.inventory.IsHoldingItemActionRunning())
            {
                Show("Finish the current action and put down the cursor stack first.");
                return false;
            }
            return true;
        }

        private void SaveProfile(int index)
        {
            EntityPlayerLocal player;
            XUiM_PlayerInventory inventory;
            if (!Ready(out player, out inventory)) return;
            if (LoadoutProfileStore.Get(index) != null && (confirmSave != index || Time.realtimeSinceStartup >= confirmUntil))
            {
                confirmSave = index;
                confirmUntil = Time.realtimeSinceStartup + 5f;
                Show("Press OVERWRITE again within 5 seconds to replace Loadout " + (index + 1) + ".");
                SetAllChildrenDirty();
                return;
            }

            try
            {
                PlayerLoadoutState state = PlayerLoadoutState.Capture(player, inventory);
                string error = null;
                if (state == null || !LoadoutProfileStore.Save(index, PlayerLoadoutBuilder.CaptureProfile(state), out error))
                {
                    Show(error ?? "The current loadout could not be saved.");
                    return;
                }
                confirmSave = -1;
                Show("Loadout " + (index + 1) + " saved. No items were moved.");
                SetAllChildrenDirty();
            }
            catch (Exception exception)
            {
                Log.Warning("[NearbyCraft] Loadout capture failed safely: {0}", exception.Message);
                Show("The current loadout could not be saved safely.");
            }
        }

        private void ApplyProfile(int index)
        {
            EntityPlayerLocal player;
            XUiM_PlayerInventory inventory;
            if (!Ready(out player, out inventory)) return;
            LoadoutProfileData profile = LoadoutProfileStore.Get(index);
            if (profile == null)
            {
                Show("Loadout " + (index + 1) + " is empty. Save it first.");
                return;
            }

            PlayerLoadoutState before = PlayerLoadoutState.Capture(player, inventory);
            PlayerLoadoutState target;
            List<ItemStack> currentManaged;
            List<ItemStack> targetManaged;
            string error = null;
            if (before == null || !PlayerLoadoutBuilder.TryBuildTarget(profile, before, player, inventory,
                out target, out currentManaged, out targetManaged, out error))
            {
                Show(error ?? "The loadout could not be prepared.");
                return;
            }

            session.Rescan();
            LoadoutSwapResult result;
            if (!session.TryExchangeLoadout(currentManaged, targetManaged,
                () => before.MatchesLive(player, inventory), out result))
            {
                string detail = DescribeProblem(result);
                Show(string.IsNullOrEmpty(detail) ? result.Error : result.Error + " " + detail);
                Manager.PlayInsidePlayerHead("ui_denied");
                return;
            }

            try
            {
                target.Apply(player, inventory);
                Show("Loadout " + (index + 1) + " equipped: " + result.Withdrawn + " withdrawn, "
                    + result.Deposited + " deposited.");
                Manager.PlayInsidePlayerHead("ui_skill_purchase");
            }
            catch (Exception exception)
            {
                // All compatibility checks and staging happen before the network
                // commit; native setters are expected not to fail on this thread.
                Log.Error("[NearbyCraft] Storage committed but the staged player loadout could not be applied: {0}", exception);
                Show("The network committed, but the player inventory refresh failed. Close the game and restore a backup before continuing.");
            }
            SetAllChildrenDirty();
        }

        private string DescribeProblem(LoadoutSwapResult result)
        {
            if (result == null || result.ProblemItem == null || result.ProblemItem.IsEmpty()) return null;
            string name = result.ProblemItem.itemValue.ItemClassOrMissing.GetLocalizedItemName();
            if (string.IsNullOrEmpty(name)) name = result.ProblemItem.itemValue.ItemClassOrMissing.GetItemName();
            return "Need " + Math.Max(1, result.ProblemCount) + " more space/item(s) for " + name + ".";
        }

        private void Show(string message)
        {
            actionMessage = message;
            actionUntil = Time.realtimeSinceStartup + 8f;
            SetAllChildrenDirty();
            EntityPlayerLocal player = xui == null || xui.playerUI == null ? null : xui.playerUI.entityPlayer;
            if (player != null) GameManager.ShowTooltip(player, message);
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            if (bindingName == "loadout_network_status")
            {
                if (!string.IsNullOrEmpty(actionMessage) && Time.realtimeSinceStartup < actionUntil) value = actionMessage;
                else if (session == null) value = "NO STORAGE CONSOLE LINK";
                else value = "T" + session.Tier + " CONSOLE  •  " + session.ConnectedStorageCount + "/"
                    + session.ChestLimit + " CHESTS  •  " + session.TotalItemCount + " NETWORK ITEMS";
                return true;
            }

            for (int i = 0; i < LoadoutProfileStore.ProfileCount; i++)
            {
                string suffix = (i + 1).ToString();
                if (bindingName == "loadout_" + suffix + "_save")
                {
                    value = confirmSave == i && Time.realtimeSinceStartup < confirmUntil ? "OVERWRITE?" : "SAVE";
                    return true;
                }
                if (bindingName == "loadout_" + suffix + "_status")
                {
                    value = Summary(LoadoutProfileStore.Get(i));
                    return true;
                }
                if (bindingName == "loadout_" + suffix + "_ready")
                {
                    value = (LoadoutProfileStore.Get(i) != null).ToString();
                    return true;
                }
            }
            return base.GetBindingValueInternal(ref value, bindingName);
        }

        private static string Summary(LoadoutProfileData profile)
        {
            if (profile == null) return "EMPTY PROFILE";
            int equipped = Count(profile.Equipment);
            int belt = Count(profile.Toolbelt);
            int supplies = 0;
            int locked = 0;
            if (profile.BackpackLocks != null)
                for (int i = 0; i < profile.BackpackLocks.Length; i++)
                    if (profile.BackpackLocks[i])
                    {
                        locked++;
                        if (profile.Backpack != null && i < profile.Backpack.Length && profile.Backpack[i] != null) supplies++;
                    }
            return equipped + " EQUIPPED  •  " + belt + " BELT  •  " + supplies + "/" + locked + " SUPPLY SLOTS";
        }

        private static int Count(SerializedLoadoutItem[] items)
        {
            if (items == null) return 0;
            int count = 0;
            for (int i = 0; i < items.Length; i++) if (items[i] != null) count++;
            return count;
        }
    }

    [HarmonyPatch(typeof(BlockCompositeTileEntity), nameof(BlockCompositeTileEntity.OnBlockActivated))]
    internal static class LoadoutLockerActivationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            if (!LoadoutLockerManager.IsLocker(_blockValue.Block)
                || (!string.Equals(_commandName, "TEFeatureStorage:Search", StringComparison.Ordinal)
                    && !string.Equals(_commandName, "Search", StringComparison.Ordinal))) return true;
            if (!NearbyCraftMod.CanUseLocalStorage)
            {
                GameManager.ShowTooltip(_player, "The loadout network is available in solo worlds only.");
                __result = false;
                return false;
            }

            TileEntity locker = _world.GetTileEntity(_blockPos);
            if (locker != null && !LoadoutLockerManager.CanAccess(locker))
            {
                Manager.BroadcastPlayByLocalPlayer(_blockPos.ToVector3() + Vector3.one * 0.5f, "Misc/locked");
                __result = false;
                return false;
            }
            World world = _world as World;
            int range = NearbyCraftMod.Config == null ? 15 : NearbyCraftMod.Config.TerminalRange;
            Vector3i terminal;
            if (!LoadoutLockerManager.TryFindTerminal(world, _blockPos, range, out terminal))
            {
                GameManager.ShowTooltip(_player, "No accessible Storage Console within " + range + " blocks.");
                __result = false;
                return false;
            }

            LocalPlayerUI playerUi = LocalPlayerUI.GetUIForPlayer(_player);
            XUiC_LoadoutLockerWindowGroup group = playerUi == null ? null
                : playerUi.xui.FindWindowGroupByName(LoadoutLockerManager.WindowGroupId) as XUiC_LoadoutLockerWindowGroup;
            if (group == null)
            {
                Log.Error("[NearbyCraft] Loadout locker XUi group was not found.");
                __result = false;
                return false;
            }
            _player.AimingGun = false;
            group.SetContext(_blockPos, terminal);
            playerUi.windowManager.Open(LoadoutLockerManager.WindowGroupId, true);
            __result = true;
            return false;
        }
    }
}
