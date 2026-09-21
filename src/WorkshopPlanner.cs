using System.Collections.Generic;

namespace NearbyCraft
{
    // Pure inventory planning, shared by production and the conservation tests.
    // Neither method commits: callers validate every world reference first.
    internal static class WorkshopPlanner
    {
        internal static int Collect(StorageTransferPlan network, ItemStack[] outputAfter)
        {
            int moved = 0;
            for (int i = 0; i < outputAfter.Length; i++)
            {
                if (outputAfter[i] == null || outputAfter[i].IsEmpty()) continue;
                int deposited = network.Deposit(outputAfter[i]);
                outputAfter[i].count -= deposited;
                moved += deposited;
                if (outputAfter[i].count == 0) outputAfter[i] = ItemStack.Empty.Clone();
            }
            return moved;
        }

        internal static bool Consume(StorageTransferPlan plan, IList<ItemStack> ingredients, int batches, out ItemStack missing)
        {
            missing = null;
            if (batches <= 0 || batches > WorkshopRules.BatchLimit) return false;
            foreach (var ingredient in ingredients)
            {
                int count = checked(ingredient.count * batches);
                if (count <= 0) continue;
                if (plan.Withdraw(ingredient, count) == count) continue;
                missing = ingredient;
                return false;
            }
            return true;
        }

        internal static bool Reserve(StorageTransferPlan capacity, IList<ItemStack> outstanding, ItemStack product)
        {
            foreach (var output in outstanding)
                if (capacity.Deposit(output) != output.count) return false;
            return product != null && product.count > 0 && capacity.Deposit(product) == product.count;
        }

        internal static bool ConsumeMaterials(ItemStack[] input, int firstMaterialSlot,
            IList<ItemStack> ingredients, int batches, out ItemStack missing)
        {
            missing = null;
            if (batches <= 0 || batches > WorkshopRules.BatchLimit) return false;
            foreach (var ingredient in ingredients)
            {
                int remaining = checked(ingredient.count * batches);
                for (int i = firstMaterialSlot; i < input.Length && remaining > 0; i++)
                {
                    if (input[i] == null || input[i].itemValue.type != ingredient.itemValue.type) continue;
                    int taken = System.Math.Min(remaining, System.Math.Max(0, input[i].count));
                    input[i].count -= taken; // Keep unit type even at zero: vanilla forge slots require it.
                    remaining -= taken;
                }
                if (remaining <= 0) continue;
                missing = new ItemStack(ingredient.itemValue.Clone(), remaining);
                return false;
            }
            return true;
        }

        internal static int Supply(StorageTransferPlan network, ItemStack[] destination,
            int slotCount, ItemStack template, int requested)
        {
            int moved = 0;
            int maximum = System.Math.Max(1, template.itemValue.ItemClassOrMissing.MaxCount);
            for (int pass = 0; pass < 2 && moved < requested; pass++)
            for (int i = 0; i < System.Math.Min(slotCount, destination.Length) && moved < requested; i++)
            {
                var stack = destination[i];
                bool empty = stack == null || stack.IsEmpty();
                if (pass == 0 ? empty || !StorageTransferPlan.Matches(stack, template) : !empty) continue;
                int take = System.Math.Min(requested - moved, System.Math.Max(0, maximum - (empty ? 0 : stack.count)));
                take = network.Withdraw(template, take);
                if (take <= 0) continue;
                if (empty) destination[i] = new ItemStack(template.itemValue.Clone(), take);
                else stack.count += take;
                moved += take;
            }
            return moved;
        }
    }
}
