using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    // No world/UI side effects until every source snapshot has been validated.
    // Callers must run planning and commit synchronously on the game thread.
    internal sealed class StorageTransferPlan
    {
        private sealed class Inventory
        {
            internal ItemStack[] Live;
            internal ItemStack[] Before;
            internal ItemStack[] After;
            internal bool[] Locked;
        }

        private readonly List<Inventory> inventories = new List<Inventory>();
        private bool committed;

        internal void Add(ItemStack[] slots, bool[] locked)
        {
            inventories.Add(new Inventory { Live = slots, Before = ItemStack.Clone(slots),
                After = ItemStack.Clone(slots), Locked = locked });
        }

        internal bool Contains(ItemStack stack)
        {
            foreach (Inventory inventory in inventories)
                for (int i = 0; i < inventory.Before.Length; i++)
                    if (!inventory.Locked[i] && Matches(inventory.Before[i], stack)) return true;
            return false;
        }

        internal int Withdraw(ItemStack template, int requested)
        {
            if (Empty(template) || requested <= 0 || committed) return 0;
            int remaining = requested;
            foreach (Inventory inventory in inventories)
                for (int i = 0; i < inventory.After.Length && remaining > 0; i++)
                {
                    ItemStack stack = inventory.After[i];
                    if (inventory.Locked[i] || !Matches(stack, template)) continue;
                    int amount = Math.Min(stack.count, remaining);
                    stack.count -= amount;
                    remaining -= amount;
                    if (stack.count == 0) inventory.After[i] = ItemStack.Empty.Clone();
                }
            return requested - remaining;
        }

        internal int Deposit(ItemStack stack)
        {
            if (Empty(stack) || committed || !stack.itemValue.ItemClassOrMissing.CanPlaceInContainer()) return 0;
            int remaining = stack.count;
            int maximum = Math.Max(1, stack.itemValue.ItemClassOrMissing.MaxCount);
            // Fill matching stacks across the network before using empty slots.
            for (int pass = 0; pass < 2 && remaining > 0; pass++)
                foreach (Inventory inventory in inventories)
                    for (int i = 0; i < inventory.After.Length && remaining > 0; i++)
                    {
                        if (inventory.Locked[i]) continue;
                        ItemStack target = inventory.After[i];
                        bool empty = Empty(target);
                        if (pass == 0 ? empty || !Matches(target, stack) : !empty) continue;
                        int amount = Math.Min(remaining, Math.Max(0, maximum - (empty ? 0 : target.count)));
                        if (amount == 0) continue;
                        if (empty) inventory.After[i] = new ItemStack(stack.itemValue.Clone(), amount);
                        else target.count += amount;
                        remaining -= amount;
                    }
            return stack.count - remaining;
        }

        internal bool Changed(int index)
        {
            Inventory inventory = inventories[index];
            for (int i = 0; i < inventory.Before.Length; i++)
                if (!Equal(inventory.Before[i], inventory.After[i])) return true;
            return false;
        }

        internal int InventoryCount { get { return inventories.Count; } }

        // Read-only copies for callers that must stage a native inventory setter
        // before committing the storage arrays. Exposing clones keeps the plan's
        // validation snapshots private and prevents callers from changing them.
        internal ItemStack[] GetBefore(int index)
        {
            return ItemStack.Clone(inventories[index].Before);
        }

        internal ItemStack[] GetAfter(int index)
        {
            return ItemStack.Clone(inventories[index].After);
        }

        internal bool TryCommit(Func<int, bool> sourceStillValid)
        {
            if (!CanCommit(sourceStillValid)) return false;
            // All checks precede all writes. Only changed slots are replaced.
            for (int n = 0; n < inventories.Count; n++)
            {
                Inventory inventory = inventories[n];
                for (int i = 0; i < inventory.Live.Length; i++)
                    if (!Equal(inventory.Before[i], inventory.After[i]))
                        inventory.Live[i] = inventory.After[i];
            }
            committed = true;
            return true;
        }

        internal bool CanCommit(Func<int, bool> sourceStillValid)
        {
            if (committed || sourceStillValid == null) return false;
            for (int n = 0; n < inventories.Count; n++)
            {
                Inventory inventory = inventories[n];
                if (!sourceStillValid(n) || inventory.Live.Length != inventory.Before.Length) return false;
                for (int i = 0; i < inventory.Live.Length; i++)
                    if (!Equal(inventory.Live[i], inventory.Before[i])) return false;
            }
            return true;
        }

        internal static bool Matches(ItemStack left, ItemStack right)
        {
            if (Empty(left) || Empty(right) || left.itemValue.type != right.itemValue.type) return false;
            if (SameValue(left.itemValue, right.itemValue)) return true;
            // Ordinary stackable supplies receive time-derived seeds in V3.2.
            // A seed is not a different kind of wood/ammunition. Keep meaningful
            // metadata distinct, and still obey native block-texture stacking.
            if (!left.itemValue.ItemClassOrMissing.CanStack()
                || left.itemValue.HasQuality || right.itemValue.HasQuality) return false;
            ItemStack leftOne = left.Clone();
            ItemStack rightOne = right.Clone();
            leftOne.count = rightOne.count = 1;
            if (!leftOne.CanStackWith(rightOne, true)) return false;
            leftOne.itemValue.Seed = rightOne.itemValue.Seed;
            return SameValue(leftOne.itemValue, rightOne.itemValue);
        }

        private static bool SameValue(ItemValue left, ItemValue right)
        {
            return left.Equals(right) && left.Flags == right.Flags && left.TextureFullArray == right.TextureFullArray;
        }

        internal static bool ExactEquals(ItemStack left, ItemStack right)
        {
            return Empty(left) || Empty(right) ? Empty(left) && Empty(right)
                : left.count == right.count && SameValue(left.itemValue, right.itemValue);
        }

        private static bool Equal(ItemStack left, ItemStack right)
        {
            return ExactEquals(left, right);
        }

        private static bool Empty(ItemStack stack)
        {
            return stack == null || stack.IsEmpty() || stack.count <= 0;
        }
    }
}
