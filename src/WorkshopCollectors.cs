using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    internal static class WorkshopCollectors
    {
        internal static bool Supported(TileEntityCollector collector)
        {
            if (collector == null) return false;
            string name = collector.block.GetBlockName();
            return name == "cntDewCollector" || name == "cntApiary" || name == "cntChickenCoop";
        }

        internal static string Describe(TileEntityCollector collector)
        {
            long output = collector.Items.Where(s => s != null && !s.IsEmpty()).Sum(s => (long)s.count);
            long input = collector.FuelSlots.Where(s => s != null && !s.IsEmpty()).Sum(s => (long)s.count);
            string state = collector.IsUserAccessing() ? "IN USE" : collector.isUnderwater ? "UNDERWATER"
                : collector.isBlocked ? "SKY BLOCKED" : "NATIVE PRODUCTION";
            return state + " / " + input + " inputs / " + output + " output waiting";
        }
    }

    internal sealed partial class StorageNetworkSession
    {
        private bool ValidCollector(TileEntityCollector collector)
        {
            return IsAvailable && !AutomationBusy && WorkshopCollectors.Supported(collector)
                && !collector.IsRemoving && !collector.IsUserAccessing() && WorkshopManager.Accessible(collector)
                && world.GetTileEntity(collector.ToWorldPos()) == collector;
        }

        internal void ServiceCollector(TileEntityCollector collector, bool supply, IList<ItemStack> reserved, out string message)
        {
            message = "";
            if (!ValidCollector(collector)) return;
            var live = collector.Items;
            var before = ItemStack.Clone(live);
            var after = ItemStack.Clone(live);
            var collection = BeginTransaction();
            int moved = 0;
            for (int i = 0; i < after.Length; i++)
            {
                if (after[i] == null || after[i].IsEmpty() || collector.IsCurrentStack(i)) continue;
                int count = collection.Plan.Deposit(after[i]);
                after[i].count -= count;
                moved += count;
                if (after[i].count == 0) after[i] = ItemStack.Empty.Clone();
            }
            if (moved > 0 && Commit(collection, () => ValidCollector(collector)
                && ReferenceEquals(live, collector.Items) && SameSlots(live, before), () => CopySlots(after, live)))
                NotifyCollector(collector);
            if (!supply || !collector.collector.UsesFuel()) return;
            if (collector.isUnderwater || collector.isBlocked) { message = "Clear the sky/water obstruction"; return; }

            var fuels = collector.FuelSlots;
            var fuelBefore = ItemStack.Clone(fuels);
            var fuelAfter = ItemStack.Clone(fuels);
            var transaction = BeginTransaction();
            var capacity = BeginTransaction();
            foreach (var output in reserved)
                if (capacity.Plan.Deposit(output) != output.count) { message = "Waiting for storage space"; return; }
            var needs = new Dictionary<string, int>();
            foreach (var output in collector.collector.OrderedSlotOutputs.Keys)
            {
                int count = collector.getCurrentConvertCount(output);
                if (count <= 0) continue; // Bees/chickens and tool upgrades stay under native control.
                string name = collector.HasModConvert ? output.OutputItemModded : output.OutputItem;
                var value = ItemClass.GetItem(name, false);
                if (value == null || value.IsEmpty()) continue;
                var product = new ItemStack(value, count);
                if (capacity.Plan.Deposit(product) != count) { message = "Waiting for storage space"; return; }
                int cost = collector.collector.GetSandboxModifiedFuelNeeded(collector.fuelCost(output, count));
                int current;
                needs.TryGetValue(output.Fuel, out current);
                needs[output.Fuel] = (int)Math.Min(120L, current + (long)Math.Max(0, cost));
            }
            if (needs.Count == 0) { message = "Install the required bees/chickens in this device"; return; }
            int supplied = 0;
            foreach (var need in needs)
            {
                var fuelType = collector.collector.GetFuelType(need.Key);
                int present = fuelAfter.Where(s => s != null && !s.IsEmpty()
                    && fuelType.Items.Contains(s.itemValue.ItemClass.GetItemName())).Sum(s => s.count);
                int missing = Math.Max(0, need.Value - present);
                foreach (string name in fuelType.Items)
                {
                    if (missing == 0) break;
                    var value = ItemClass.GetItem(name, false);
                    if (value == null || value.IsEmpty()) continue;
                    int count = WorkshopPlanner.Supply(transaction.Plan, fuelAfter, fuelAfter.Length, new ItemStack(value, 1), missing);
                    supplied += count;
                    missing -= count;
                }
                if (missing > 0) message = "Needs " + string.Join(" / ", fuelType.Items.Select(n => Localization.Get(n)));
            }
            if (supplied > 0 && Commit(transaction, () => ValidCollector(collector)
                && ReferenceEquals(fuels, collector.FuelSlots) && SameSlots(fuels, fuelBefore), () => CopySlots(fuelAfter, fuels)))
            {
                collector.visibleChanged = true;
                NotifyCollector(collector);
            }
        }

        private static void NotifyCollector(TileEntityCollector collector)
        {
            try { collector.SetModified(); collector.NotifyListeners(); }
            catch (Exception e) { Log.Error("[NearbyCraft] Collector transfer committed but notification failed: {0}", e); }
            StorageIndex.Invalidate();
        }
    }
}
