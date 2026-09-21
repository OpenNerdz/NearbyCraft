using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    internal static class TerminalRules
    {
        internal const string BlockName = "nearbyCraftStorageTerminal";
        // NGUI touch IDs, not Unity Input.GetMouseButton indices.
        internal static bool IsRightClick(int mouseButton) { return mouseButton == -2; }

        internal static bool CanShiftToInventory(bool canUseBackpack, bool canUseToolbelt)
        {
            return canUseBackpack || canUseToolbelt;
        }

        internal static bool CanBulkDepositSlot(bool matchingOnly, bool backpackSlotLocked)
        {
            return !matchingOnly || !backpackSlotLocked;
        }

        internal static int GetTier(string name)
        {
            if (name == BlockName) return 1;
            for (int tier = 2; tier <= 4; tier++)
                if (name == BlockName + "Tier" + tier) return tier;
            return 0;
        }

        internal static int ChestLimit(int tier)
        {
            return tier >= 1 && tier <= 4 ? 8 << (tier - 1) : 0;
        }

        // Call once per eligible backpack slot in stable slot order. Deposit All
        // includes locked backpack slots; Matching Only excludes them. Neither
        // mode includes the toolbelt in its reserve accounting.
        internal static int Depositable(string name, int count, IDictionary<string, int> reserves, IDictionary<string, int> retained)
        {
            if (count <= 0) return 0;
            int reserve = 0;
            if (reserves != null) reserves.TryGetValue(name, out reserve);
            int alreadyKept;
            retained.TryGetValue(name, out alreadyKept);
            int keep = Math.Min(count, Math.Max(0, reserve - alreadyKept));
            retained[name] = alreadyKept + keep;
            return count - keep;
        }
    }
}
