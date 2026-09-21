using System;
using System.Collections.Generic;
using System.Linq;
using Platform;
using UnityEngine;

namespace NearbyCraft
{
    internal static class WorkshopManager
    {
        internal const string BlockName = "nearbyCraftWorkshopController";
        internal const string WindowGroupId = "nearbycraft_workshop";
        private static World currentWorld;
        private static float nextTick;
        private static float nextRemovalAttempt;
        internal static readonly Dictionary<Vector3i, string> Status = new Dictionary<Vector3i, string>();
        internal static readonly Dictionary<string, string> TargetStatus = new Dictionary<string, string>();
        internal static readonly Dictionary<Vector3i, string> StationStatus = new Dictionary<Vector3i, string>();
        internal static readonly HashSet<Vector3i> Removed = new HashSet<Vector3i>();

        internal static bool IsController(Block block) { return block != null && block.GetBlockName() == BlockName; }
        internal static bool IsManager(Block block) { return IsController(block) || StorageTerminalManager.IsTerminal(block); }
        internal static string TargetKey(Vector3i position, string item) { return position + "/" + item; }

        internal static bool Accessible(TileEntity entity)
        {
            if (entity == null || entity.IsRemoving) return false;
            ILockable lockable;
            return !entity.TryGetSelfOrFeature<ILockable>(out lockable) || lockable == null
                || !lockable.IsLocked() || lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier);
        }

        internal static bool Supported(Recipe recipe)
        {
            if (recipe == null || recipe.IsScrap || recipe.isQuest || recipe.isChallenge) return false;
            var output = recipe.GetOutputItemClass();
            if (output == null || output.HasQuality || !output.CanStack() || !output.CanPlaceInContainer()) return false;
            return recipe.ingredients.All(i => i != null && !i.itemValue.HasQuality && i.count >= 0);
        }

        internal static void Update(ref ModEvents.SGameUpdateData data)
        {
            var game = GameManager.Instance;
            World world = game == null ? null : game.World;
            if (!ReferenceEquals(world, currentWorld))
            {
                currentWorld = world;
                nextTick = 0;
                nextRemovalAttempt = 0;
                Status.Clear();
                TargetStatus.Clear();
                StationStatus.Clear();
                Removed.Clear();
            }
            if (world != null && NearbyCraftMod.CanUseLocalStorage && WorkshopStore.Writable
                && Time.realtimeSinceStartup >= nextRemovalAttempt)
            {
                nextRemovalAttempt = Time.realtimeSinceStartup + 2f;
                foreach (var position in Removed.ToArray())
                {
                    if (world.GetChunkFromWorldPos(position) == null) continue;
                    if (IsManager(world.GetBlock(position).Block) || WorkshopStore.Remove(position))
                        Removed.Remove(position);
                }
            }
            if (world == null || game.IsPaused() || !NearbyCraftMod.CanUseLocalStorage
                || !NearbyCraftMod.Config.Enabled || !WorkshopStore.Writable || Time.realtimeSinceStartup < nextTick) return;
            nextTick = Time.realtimeSinceStartup + 2f;
            var player = world.GetPrimaryPlayer();
            var ui = player == null ? null : LocalPlayerUI.GetUIForPlayer(player);
            if (player == null || player.IsDead() || ui == null || ui.xui == null) return;
            if (!ui.xui.DragAndDropWindow.CurrentStack.IsEmpty() || StorageTerminalManager.IsOpen
                || ui.windowManager.IsWindowOpen(LoadoutLockerManager.WindowGroupId)) return;

            var consoles = new HashSet<Vector3i>();
            var chests = new HashSet<Vector3i>();
            var claimedStations = new HashSet<Vector3i>();
            foreach (var controller in WorkshopStore.Controllers.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).ToArray())
            {
                if (!WorkshopStore.Writable) break;
                try { Tick(world, player, ui.xui, controller, consoles, claimedStations, chests); }
                catch (Exception e)
                {
                    Status[controller.Position] = "Automation stopped after an error. Re-enable after checking the log.";
                    controller.Enabled = false;
                    string error;
                    WorkshopStore.SaveProgress(out error);
                    Log.Error("[NearbyCraft] Workshop at {0} stopped: {1}", controller.Position, e);
                }
            }
        }

        private static void Tick(World world, EntityPlayerLocal player, XUi xui, WorkshopControllerData controller,
            HashSet<Vector3i> consoles, HashSet<Vector3i> claimedStations, HashSet<Vector3i> claimedChests)
        {
            Vector3i pos = controller.Position;
            Status[pos] = "PAUSED";
            if (!controller.Enabled) return;
            if (world.GetChunkFromWorldPos(pos) == null) { Status[pos] = "Area unloaded"; return; }
            if (!IsManager(world.GetBlock(pos).Block) || !Accessible(world.GetTileEntity(pos)))
            { Status[pos] = "Controller missing or locked"; return; }
            if (!controller.Linked || !StorageTerminalManager.IsTerminal(world.GetBlock(controller.Console).Block)
                || !Accessible(world.GetTileEntity(controller.Console))
                || (controller.Console.ToVector3() - pos.ToVector3()).sqrMagnitude > NearbyCraftMod.Config.TerminalRange * NearbyCraftMod.Config.TerminalRange)
            { Status[pos] = "LINK A STORAGE CONSOLE WITHIN RANGE"; return; }
            if (!consoles.Add(controller.Console)) { Status[pos] = "Another controller already manages this console"; return; }

            var network = new StorageNetworkSession(world, player, controller.Console, NearbyCraftMod.Config, pos, true);
            network.Rescan(false);
            if (network.AutomationBusy || world.GetTileEntity(controller.Console).IsUserAccessing())
            { Status[pos] = "Waiting: storage is in use"; return; }
            if (network.ConnectedStorageCount == 0) { Status[pos] = "No accessible chests connected"; return; }
            if (network.StoragePositions.Any(p => claimedChests.Contains(p)))
            { Status[pos] = "Overlapping chest networks: use one active controller per network"; return; }
            foreach (var chest in network.StoragePositions) claimedChests.Add(chest);
            var devices = FindDevices(world, pos)
                .Where(s => !controller.ExcludedStations.Contains(s.ToWorldPos().ToString())).ToList();
            var managed = devices.Where(s => claimedStations.Add(s.ToWorldPos())).ToList();
            var allStations = managed.OfType<TileEntityWorkstation>().ToList();
            var stations = allStations;
            Status[pos] = managed.Count + " machines / " + network.ConnectedStorageCount + " chests / T" + network.Tier + " console";
            if (managed.Count == 0) { Status[pos] = "No enabled machines within range"; return; }
            // Busy machines remain in demand/output accounting, but their native
            // inventories are left alone; other machines can continue working.
            bool deliveryChanged = false;
            foreach (var station in stations)
            {
                network.CollectOutput(station, (type, count) =>
                    deliveryChanged |= WorkshopStore.CreditDelivery(controller, ItemClass.GetForId(type).GetItemName(), count));
                string fuelMessage;
                network.MaintainFuel(station, controller.AutoFuel, out fuelMessage);
                StationStatus[station.ToWorldPos()] = fuelMessage;
            }
            if (deliveryChanged)
            {
                string error;
                if (!WorkshopStore.SaveProgress(out error)) { Status[pos] = error; return; }
            }
            foreach (var collector in managed.OfType<TileEntityCollector>())
            {
                string message;
                network.ServiceCollector(collector, controller.AutoFuel, WorkshopScheduler.ReservedOutputs(allStations, controller), out message);
                StationStatus[collector.ToWorldPos()] = message;
            }

            new WorkshopScheduler(network, player, xui, controller, stations).Run();
        }

        internal static List<TileEntityWorkstation> FindStations(World world, Vector3i position)
        {
            return FindDevices(world, position).OfType<TileEntityWorkstation>().ToList();
        }

        internal static List<TileEntity> FindDevices(World world, Vector3i position)
        {
            var result = new List<TileEntity>();
            int range = NearbyCraftMod.Config.TerminalRange;
            int minX = Utils.Fastfloor((position.x - range) / 16f), maxX = Utils.Fastfloor((position.x + range) / 16f);
            int minZ = Utils.Fastfloor((position.z - range) / 16f), maxZ = Utils.Fastfloor((position.z + range) / 16f);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                var chunk = world.GetChunkSync(x, z) as Chunk;
                if (chunk == null || chunk.tileEntities == null) continue;
                foreach (var entity in chunk.tileEntities.list)
                {
                    if (!Accessible(entity) || (entity.ToWorldPos().ToVector3() - position.ToVector3()).sqrMagnitude > range * range) continue;
                    var station = entity as TileEntityWorkstation;
                    if ((WorkshopMachines.Supported(station) && station.IsPlayerPlaced)
                        || WorkshopCollectors.Supported(entity as TileEntityCollector)) result.Add(entity);
                }
            }
            return result.OrderBy(s => (s.ToWorldPos().ToVector3() - position.ToVector3()).sqrMagnitude)
                .ThenBy(s => s.ToWorldPos().x).ThenBy(s => s.ToWorldPos().y).ThenBy(s => s.ToWorldPos().z).Take(WorkshopRules.MaximumStations).ToList();
        }

        internal static Recipe PrepareRecipe(Recipe recipe, XUi xui, TileEntityWorkstation station = null)
        {
            if (station == null) return PrepareRecipeCore(recipe, xui);
            // Vanilla reads tools from the open UI. Scope its cache to the actual
            // station on the game thread; restore it even if a modded effect throws.
            var tools = EffectManager.slotsCached;
            int frame = EffectManager.slotsQueriedFrame, entity = EffectManager.slotsQueriedForEntity;
            try
            {
                EffectManager.slotsCached = WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Tools)
                    ? station.Tools : new ItemStack[0];
                EffectManager.slotsQueriedFrame = Time.frameCount;
                EffectManager.slotsQueriedForEntity = xui.playerUI.entityPlayer.entityId;
                return PrepareRecipeCore(recipe, xui);
            }
            finally
            {
                EffectManager.slotsCached = tools;
                EffectManager.slotsQueriedFrame = frame;
                EffectManager.slotsQueriedForEntity = entity;
            }
        }

        private static Recipe PrepareRecipeCore(Recipe recipe, XUi xui)
        {
            int tier = recipe.GetCraftingTier(xui.playerUI.entityPlayer);
            var staged = new Recipe { itemValueType = recipe.itemValueType, count = XUiM_Recipes.GetRecipeCraftOutputCount(xui, recipe),
                craftingArea = recipe.craftingArea, craftExpGain = recipe.craftExpGain,
                craftingTime = Math.Max(0.05f, XUiM_Recipes.GetRecipeCraftTime(xui, recipe)), craftingToolType = recipe.craftingToolType,
                craftingTier = tier, tags = recipe.tags, UseIngredientModifier = false, materialBasedRecipe = recipe.materialBasedRecipe };
            float modifier = XUiM_Recipes.GetCraftingInputModifier(recipe);
            if (modifier <= 0f) return staged;
            foreach (var ingredient in recipe.ingredients)
            {
                int count = ingredient.count;
                if (recipe.UseIngredientModifier)
                {
                    count = (int)EffectManager.GetValue(PassiveEffects.CraftingIngredientCount, null, count, xui.playerUI.entityPlayer,
                        recipe, FastTags<TagGroup.Global>.Parse(ingredient.itemValue.ItemClass.GetItemName()),
                        calcEquipment: true, calcHoldingItem: true, calcProgression: true, calcBuffs: true, calcChallenges: true, craftingTier: tier);
                    if (count > 0) count = Math.Max(1, (int)(count * modifier));
                }
                if (count > 0)
                {
                    var duplicate = staged.ingredients.Find(i => i.itemValue.type == ingredient.itemValue.type);
                    if (duplicate == null) staged.ingredients.Add(new ItemStack(ingredient.itemValue.Clone(), count));
                    else duplicate.count = checked(duplicate.count + count);
                }
            }
            return staged;
        }

        internal static long CountOutput(TileEntityWorkstation station, int type)
        {
            return station.Output.Where(s => s != null && !s.IsEmpty() && s.itemValue.type == type).Sum(s => (long)s.count);
        }

        internal static long CountQueued(TileEntityWorkstation station, int type)
        {
            return station.Queue.Where(q => q != null && q.Recipe != null && q.Multiplier > 0 && q.Recipe.itemValueType == type)
                .Sum(q => (long)q.Multiplier * q.Recipe.count);
        }

        internal static List<ItemStack> OutstandingOutputs(List<TileEntityWorkstation> stations)
        {
            var result = new List<ItemStack>();
            foreach (var station in stations)
            {
                result.AddRange(station.Output.Where(s => s != null && !s.IsEmpty()).Select(s => s.Clone()));
                foreach (var q in station.Queue)
                {
                    if (q == null || q.Recipe == null || q.Multiplier <= 0) continue;
                    var value = new ItemValue(q.Recipe.itemValueType, q.Quality, q.Quality);
                    result.Add(new ItemStack(value, checked(q.Recipe.count * q.Multiplier)));
                }
            }
            return result;
        }
    }
}
