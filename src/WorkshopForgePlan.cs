using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    // Dry-run the entire input layout before withdrawing anything. Existing stacks
    // and their in-flight timers are never moved; only newly supplied items balance.
    internal sealed class WorkshopForgePlan
    {
        internal ItemStack[] Inputs;
        internal readonly List<ItemStack> Supplies = new List<ItemStack>();
        internal double Seconds;

        internal static float SmeltSeconds(TileEntityWorkstation station, ItemValue raw, bool first)
        {
            var item = raw.ItemClass;
            float seconds = item.GetWeight() * (item.MeltTimePerUnit > 0 ? item.MeltTimePerUnit : 1f);
            string tag = first ? item.GetItemName() : "unit_" + item.MadeOfMaterial.ForgeCategory;
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Tools))
                foreach (var tool in station.Tools)
                {
                    if (tool == null || tool.IsEmpty()) continue;
                    float multiplier = 1f;
                    tool.itemValue.ModifyValue(null, null, PassiveEffects.CraftingSmeltTime,
                        ref seconds, ref multiplier, FastTags<TagGroup.Global>.Parse(tag));
                    seconds *= multiplier;
                }
            return float.IsNaN(seconds) || float.IsInfinity(seconds) ? 3600f : Math.Max(.05f, seconds);
        }

        internal static bool Create(TileEntityWorkstation station, Recipe recipe, int batches,
            out WorkshopForgePlan plan, out string reason)
        {
            plan = new WorkshopForgePlan { Inputs = ItemStack.Clone(station.Input) };
            reason = "Waiting for smelted materials";
            int slots = Math.Min(station.InputSlotCount, plan.Inputs.Length);
            foreach (var ingredient in recipe.ingredients)
            {
                long available = WorkshopMachines.MaterialUnits(station, ingredient, false);
                long covered = WorkshopMachines.MaterialUnits(station, ingredient, true);
                long required = (long)ingredient.count * batches;
                if (required <= covered) continue;
                string name = WorkshopMachines.RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
                var raw = name == null ? null : ItemClass.GetItem(name, false);
                if (raw == null || raw.IsEmpty() || !station.AcceptsMaterial(raw.ItemClass.MadeOfMaterial))
                { reason = "Supply this forge's special material manually"; return false; }
                int weight = raw.ItemClass.GetWeight();
                long capacity = Math.Max(0, ingredient.itemValue.ItemClass.MaxCount - covered);
                int count = WorkshopRules.FeedCount(required, available, covered - available, weight,
                    weight <= 0 ? 0 : (int)Math.Min(1000, capacity / weight));
                if (count <= 0 || covered + (long)count * weight < required)
                { reason = "Not enough forge material capacity"; return false; }
                plan.Supplies.Add(new ItemStack(raw, count));
            }
            // Reserve one slot for every missing input kind before letting any
            // ingredient use spare lanes. Iron must not take the clay's last slot.
            var remaining = plan.Supplies.Select(s => s.count).ToArray();
            for (int n = 0; n < plan.Supplies.Count; n++)
            {
                var supply = plan.Supplies[n];
                if (plan.Inputs.Take(slots).Any(s => s != null && !s.IsEmpty()
                    && StorageTransferPlan.Matches(s, supply) && s.count < s.itemValue.ItemClass.MaxCount)) continue;
                int slot = Array.FindIndex(plan.Inputs, 0, slots, s => s == null || s.IsEmpty());
                if (slot < 0) { reason = "Free a forge input slot for " + Localization.Get(supply.itemValue.ItemClass.GetItemName()); return false; }
                plan.Inputs[slot] = new ItemStack(supply.itemValue.Clone(), 1);
                remaining[n]--;
            }
            var loads = new double[slots];
            for (int i = 0; i < slots; i++) loads[i] = LaneSeconds(station, plan.Inputs[i], i);
            // Longest material workload first; free lanes go where they reduce
            // completion time most. Bounded feed limits keep this loop small.
            var supplies = plan.Supplies;
            foreach (int n in Enumerable.Range(0, remaining.Length).OrderByDescending(n =>
                remaining[n] * SmeltSeconds(station, supplies[n].itemValue, false)))
            {
                var supply = plan.Supplies[n];
                while (remaining[n] > 0)
                {
                    int best = -1; double finish = double.PositiveInfinity;
                    for (int i = 0; i < slots; i++)
                    {
                        var stack = plan.Inputs[i];
                        bool empty = stack == null || stack.IsEmpty();
                        if (!empty && (!StorageTransferPlan.Matches(stack, supply) || stack.count >= supply.itemValue.ItemClass.MaxCount)) continue;
                        double next = loads[i] + SmeltSeconds(station, supply.itemValue, empty);
                        if (next < finish) { best = i; finish = next; }
                    }
                    if (best < 0) { reason = "Free forge input space"; return false; }
                    if (plan.Inputs[best] == null || plan.Inputs[best].IsEmpty()) plan.Inputs[best] = new ItemStack(supply.itemValue.Clone(), 1);
                    else plan.Inputs[best].count++;
                    loads[best] = finish; remaining[n]--;
                }
            }
            // Estimate readiness for this batch, not the time to empty unrelated
            // or overstocked input stacks. Each native lane has its own timer.
            foreach (var ingredient in recipe.ingredients)
            {
                long deficit = (long)ingredient.count * batches - WorkshopMachines.MaterialUnits(station, ingredient, false);
                if (deficit <= 0) continue;
                string category = ingredient.itemValue.ItemClass.MadeOfMaterial.ForgeCategory;
                double low = 0, high = loads.Length == 0 ? 0 : loads.Max();
                for (int step = 0; step < 32; step++)
                {
                    double mid = (low + high) / 2; long units = 0;
                    for (int i = 0; i < slots; i++)
                    {
                        var stack = plan.Inputs[i];
                        if (stack == null || stack.IsEmpty() || !string.Equals(category, stack.itemValue.ItemClass.MadeOfMaterial.ForgeCategory, StringComparison.OrdinalIgnoreCase)) continue;
                        double first = FirstSeconds(station, stack, i), repeat = SmeltSeconds(station, stack.itemValue, false);
                        if (mid >= first) units += Math.Min(stack.count, 1 + (long)((mid - first) / repeat)) * stack.itemValue.ItemClass.GetWeight();
                    }
                    if (units >= deficit) high = mid; else low = mid;
                }
                plan.Seconds = Math.Max(plan.Seconds, high);
            }
            return true;
        }

        private static double FirstSeconds(TileEntityWorkstation station, ItemStack stack, int slot)
        {
            var before = station.Input[slot];
            float timer = station.GetTimerForSlot(slot);
            return before != null && !before.IsEmpty() && before.itemValue.type == stack.itemValue.type
                && timer >= 0 && !float.IsInfinity(timer) && !float.IsNaN(timer)
                ? timer : SmeltSeconds(station, stack.itemValue, true);
        }

        private static double LaneSeconds(TileEntityWorkstation station, ItemStack stack, int slot)
        {
            return stack == null || stack.IsEmpty() ? 0 : FirstSeconds(station, stack, slot)
                + Math.Max(0, stack.count - 1) * (double)SmeltSeconds(station, stack.itemValue, false);
        }
    }
}
