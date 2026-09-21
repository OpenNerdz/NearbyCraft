using System;

namespace NearbyCraft
{
    // Shared verbatim with DeepBore's direct-box path and pure conservation tests.
    internal static class StorageOutputPlanner
    {
        // Allocation-free early exit: do not discover/clone a network for an empty or reserved hopper.
        internal static bool HasExportable(ItemStack[] source, PackedBoolArray locked, int[] allowedTypes)
        {
            if (source == null || allowedTypes == null || allowedTypes.Length == 0) return false;
            for (int i = 0; i < source.Length; i++)
            {
                var stack = source[i];
                if (locked != null && i < locked.Length && locked[i]) continue;
                if (stack != null && !stack.IsEmpty() && stack.count > 0
                    && Array.IndexOf(allowedTypes, stack.itemValue.type) >= 0
                    && stack.itemValue.ItemClassOrMissing.CanPlaceInContainer()) return true;
            }
            return false;
        }

        internal static int Collect(StorageTransferPlan destination, ItemStack[] sourceAfter,
            bool[] sourceLocked, int[] allowedTypes)
        {
            if (destination == null || sourceAfter == null || sourceLocked == null
                || sourceLocked.Length != sourceAfter.Length || allowedTypes == null) return 0;
            int moved = 0;
            for (int i = 0; i < sourceAfter.Length; i++)
            {
                var stack = sourceAfter[i];
                if (sourceLocked[i] || stack == null || stack.IsEmpty()
                    || Array.IndexOf(allowedTypes, stack.itemValue.type) < 0) continue;
                int amount = destination.Deposit(stack);
                stack.count -= amount;
                moved = checked(moved + amount);
                if (stack.count == 0) sourceAfter[i] = ItemStack.Empty.Clone();
            }
            return moved;
        }
    }
}
