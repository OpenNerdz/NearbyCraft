using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    internal sealed class LoadoutSwapResult
    {
        internal int Withdrawn;
        internal int Deposited;
        internal ItemStack ProblemItem;
        internal int ProblemCount;
        internal string Error;
    }

    // Reduces the player's current managed slots and the requested layout to
    // two multisets. Items already carried are reused even when they move to a
    // different slot, so the storage network only receives true surplus and
    // supplies true deficits.
    internal static class LoadoutSwapPlanner
    {
        internal static bool TryPlan(IList<ItemStack> current, IList<ItemStack> target,
            StorageTransferPlan network, out LoadoutSwapResult result)
        {
            result = new LoadoutSwapResult();
            if (current == null || target == null || network == null || current.Count != target.Count)
            {
                result.Error = "The saved loadout no longer matches this inventory layout.";
                return false;
            }

            var surplus = new List<ItemStack>(current.Count);
            for (int i = 0; i < current.Count; i++)
            {
                ItemStack stack = current[i];
                surplus.Add(IsEmpty(stack) ? ItemStack.Empty.Clone() : stack.Clone());
            }

            for (int targetIndex = 0; targetIndex < target.Count; targetIndex++)
            {
                ItemStack wanted = target[targetIndex];
                if (IsEmpty(wanted)) continue;
                if (wanted.count <= 0 || wanted.count > Math.Max(1, wanted.itemValue.ItemClassOrMissing.MaxCount))
                {
                    result.Error = "A saved stack has an invalid size. Save this profile again.";
                    result.ProblemItem = wanted.Clone();
                    return false;
                }

                int remaining = wanted.count;
                for (int sourceIndex = 0; sourceIndex < surplus.Count && remaining > 0; sourceIndex++)
                {
                    ItemStack carried = surplus[sourceIndex];
                    if (!StorageTransferPlan.Matches(carried, wanted)) continue;
                    int used = Math.Min(carried.count, remaining);
                    carried.count -= used;
                    remaining -= used;
                    if (carried.count == 0) surplus[sourceIndex] = ItemStack.Empty.Clone();
                }

                if (remaining <= 0) continue;
                int withdrawn = network.Withdraw(wanted, remaining);
                result.Withdrawn += withdrawn;
                if (withdrawn != remaining)
                {
                    result.ProblemItem = wanted.Clone();
                    result.ProblemCount = remaining - withdrawn;
                    result.Error = "The connected storage network is missing required items.";
                    return false;
                }
            }

            for (int i = 0; i < surplus.Count; i++)
            {
                ItemStack outgoing = surplus[i];
                if (IsEmpty(outgoing)) continue;
                int deposited = network.Deposit(outgoing);
                result.Deposited += deposited;
                if (deposited != outgoing.count)
                {
                    result.ProblemItem = outgoing.Clone();
                    result.ProblemCount = outgoing.count - deposited;
                    result.Error = outgoing.itemValue.ItemClassOrMissing.CanPlaceInContainer()
                        ? "The connected storage network does not have enough unlocked space."
                        : "An outgoing item cannot be placed in storage.";
                    return false;
                }
            }
            return true;
        }

        private static bool IsEmpty(ItemStack stack)
        {
            return stack == null || stack.IsEmpty() || stack.count <= 0;
        }
    }
}
