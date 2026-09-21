using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    internal sealed partial class StorageNetworkSession
    {
        internal IEnumerable<Vector3i> StoragePositions
        {
            get { foreach (var source in sources) yield return source.Position; }
        }

        internal long CountProduct(int type)
        {
            long count = 0;
            foreach (var source in sources)
            {
                if (!IsSourceValid(source)) continue;
                for (int i = 0; i < source.Storage.items.Length; i++)
                {
                    var stack = source.Storage.items[i];
                    if (!IsSlotLocked(source, i) && stack != null && !stack.IsEmpty() && stack.itemValue.type == type)
                        count += stack.count;
                }
            }
            return count;
        }

        private bool ValidStation(TileEntityWorkstation station)
        {
            return IsAvailable && !AutomationBusy && station != null && !station.IsRemoving
                && !station.IsUserAccessing() && station.IsPlayerPlaced
                && WorkshopManager.Accessible(station) && WorkshopMachines.Supported(station)
                && world.GetTileEntity(station.ToWorldPos()) == station;
        }

        internal int CollectOutput(TileEntityWorkstation station, Action<int, int> collected = null)
        {
            if (!ValidStation(station) || station.Output == null) return 0;
            bool hasOutput = false;
            foreach (var stack in station.Output) if (stack != null && !stack.IsEmpty()) { hasOutput = true; break; }
            if (!hasOutput) return 0;
            var live = station.Output;
            var before = ItemStack.Clone(live);
            var after = ItemStack.Clone(live);
            var transaction = BeginTransaction();
            int moved = WorkshopPlanner.Collect(transaction.Plan, after);
            if (moved == 0 || !Commit(transaction, () => ValidStation(station)
                && ReferenceEquals(live, station.Output) && SameSlots(live, before), () =>
                {
                    for (int i = 0; i < live.Length; i++) live[i] = after[i];
                })) return 0;
            NotifyStation(station);
            if (collected != null)
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == null || before[i].IsEmpty()) continue;
                    int count = before[i].count - after[i].count;
                    if (count > 0) collected(before[i].itemValue.type, count);
                }
            return moved;
        }

        internal bool QueueBatch(TileEntityWorkstation station, RecipeQueueItem batch,
            IList<ItemStack> reservedOutputs, bool automaticFuel, out ItemStack missing, out string message)
        {
            missing = null;
            message = "Station is busy";
            if (!ValidStation(station) || station.Queue == null || station.Queue.Length == 0 || station.hasRecipeInQueue()) return false;
            var queue = station.Queue;
            int slot = queue.Length - 1;
            var original = queue[slot];
            if (original != null && (original.Recipe != null || original.RepairItem != null || original.Multiplier != 0)) return false;
            var transaction = BeginTransaction();
            var capacity = BeginTransaction();
            var inputLive = station.Input;
            var inputBefore = ItemStack.Clone(inputLive);
            var inputAfter = ItemStack.Clone(inputLive);
            bool materials = batch.Recipe.materialBasedRecipe;
            if (materials && !WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Material_Input))
            { message = "Recipe requires a smelting workstation"; return false; }
            if (!(materials ? WorkshopPlanner.ConsumeMaterials(inputAfter, station.InputSlotCount, batch.Recipe.ingredients, batch.Multiplier, out missing)
                : WorkshopPlanner.Consume(transaction.Plan, batch.Recipe.ingredients, batch.Multiplier, out missing)))
            {
                message = missing == null ? "Invalid batch" : "Needs " + missing.itemValue.ItemClassOrMissing.GetLocalizedItemName();
                return false;
            }
            if (!materials && !WorkshopPlanner.Consume(capacity.Plan, batch.Recipe.ingredients, batch.Multiplier, out missing)) return false;

            var fuelLive = station.Fuel;
            var fuelBefore = ItemStack.Clone(fuelLive);
            var fuelAfter = ItemStack.Clone(fuelLive);
            bool needsFuel = WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel);
            if (needsFuel)
            {
                if (station.IsBesideWater) { message = "Station is blocked by water"; return false; }
                if (automaticFuel)
                {
                    StageFuel(transaction.Plan, station, fuelAfter, batch.OneItemCraftTime * batch.Multiplier);
                    StageFuel(capacity.Plan, station, ItemStack.Clone(fuelLive), batch.OneItemCraftTime * batch.Multiplier);
                }
                if ((!automaticFuel && !station.IsBurning) || FuelSeconds(fuelAfter, station.BurnTimeLeft) <= 0)
                { message = automaticFuel ? "Needs wood in connected storage" : "Fuel and light the station (AUTO FUEL is off)"; return false; }
            }

            // Reserve space in a THROWAWAY plan. Prospective products must never
            // be committed before vanilla actually finishes crafting them.
            message = "Waiting for storage space";
            var product = new ItemStack(new ItemValue(batch.Recipe.itemValueType), checked(batch.Recipe.count * batch.Multiplier));
            if (!WorkshopPlanner.Reserve(capacity.Plan, reservedOutputs, product)) return false;
            var outputCapacity = new StorageTransferPlan();
            var outputLive = station.Output;
            var outputBefore = ItemStack.Clone(outputLive);
            outputCapacity.Add(outputLive, new bool[outputLive.Length]);
            if (outputCapacity.Deposit(product) != product.count) { message = "Waiting for workstation output space"; return false; }

            message = "Storage or workstation changed; retrying";
            ulong tick = GameTimer.Instance.ticks;
            if (!Commit(transaction, () => ValidStation(station) && ReferenceEquals(queue, station.Queue)
                && ReferenceEquals(original, queue[slot]) && !station.hasRecipeInQueue()
                && ReferenceEquals(inputLive, station.Input) && SameSlots(inputLive, inputBefore)
                && ReferenceEquals(outputLive, station.Output) && SameSlots(outputLive, outputBefore)
                && ReferenceEquals(fuelLive, station.Fuel) && SameSlots(fuelLive, fuelBefore), () =>
                {
                    CopySlots(inputAfter, inputLive);
                    CopySlots(fuelAfter, fuelLive);
                    queue[slot] = batch;
                    station.lastTickTime = tick;
                })) return false;
            if (needsFuel && automaticFuel && !station.IsBurning) station.IsBurning = true;
            NotifyStation(station);
            message = "Queued " + product.count;
            return true;
        }

        internal bool CanReserveProducts(IList<ItemStack> reserved, ItemStack product)
        {
            return WorkshopPlanner.Reserve(BeginTransaction().Plan, reserved, product);
        }

        private static bool SameSlots(ItemStack[] left, ItemStack[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!StorageTransferPlan.ExactEquals(left[i], right[i])) return false;
            return true;
        }

        private static void NotifyStation(TileEntityWorkstation station)
        {
            try { station.SetModified(); }
            catch (Exception e) { Log.Error("[NearbyCraft] Workshop transfer committed but notification failed: {0}", e); }
            StorageIndex.Invalidate();
            StorageTerminalManager.RequestItemsRefresh();
        }
    }
}
