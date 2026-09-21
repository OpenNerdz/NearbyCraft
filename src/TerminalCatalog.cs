using System;
using System.Collections.Generic;
using System.Globalization;

namespace NearbyCraft
{
    internal enum StorageTerminalSort : byte { Name, Count, Type }

    // Read-only presentation of many physical slots. The total is never used as
    // a cursor/backpack stack: transfers always receive a native-sized clone.
    internal sealed class TerminalCatalog
    {
        internal sealed class Entry
        {
            internal ItemStack Template;
            internal long Count;
            internal string DisplayName, InternalName;
            internal int Order;

            internal ItemStack TransferStack()
            {
                var result = Template.Clone();
                result.count = TransferCount(Count, Template.itemValue.ItemClassOrMissing.MaxCount);
                return result;
            }
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<int, List<Entry>> byType = new Dictionary<int, List<Entry>>();
        private readonly Func<ItemStack, string> displayName, internalName;

        internal TerminalCatalog(Func<ItemStack, string> displayName, Func<ItemStack, string> internalName)
        {
            this.displayName = displayName;
            this.internalName = internalName;
        }

        internal void Clear() { entries.Clear(); byType.Clear(); }

        internal void Add(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty()) return;
            List<Entry> candidates;
            if (!byType.TryGetValue(stack.itemValue.type, out candidates))
                byType.Add(stack.itemValue.type, candidates = new List<Entry>());
            // Equipment keeps its individual quality/durability display and slot.
            if (stack.itemValue.ItemClassOrMissing.CanStack() && !stack.itemValue.HasQuality)
            {
                foreach (var entry in candidates)
                {
                    if (!StorageTransferPlan.Matches(entry.Template, stack)) continue;
                    entry.Count += stack.count;
                    return;
                }
            }
            var template = stack.Clone();
            template.count = 1;
            var added = new Entry { Template = template, Count = stack.count,
                DisplayName = displayName(stack) ?? "", InternalName = internalName(stack) ?? "", Order = entries.Count };
            entries.Add(added);
            candidates.Add(added);
        }

        internal void FilterAndSort(string search, StorageTerminalSort sort, List<Entry> result)
        {
            result.Clear();
            search = (search ?? "").Trim();
            foreach (var entry in entries)
                if (search.Length == 0 || entry.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || entry.InternalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(entry);
            result.Sort((left, right) => Compare(left, right, sort));
        }

        private static int Compare(Entry left, Entry right, StorageTerminalSort sort)
        {
            int order = sort == StorageTerminalSort.Count ? right.Count.CompareTo(left.Count)
                : sort == StorageTerminalSort.Type ? left.Template.itemValue.type.CompareTo(right.Template.itemValue.type) : 0;
            if (order != 0) return order;
            order = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (order != 0) return order;
            order = right.Count.CompareTo(left.Count);
            if (order != 0) return order;
            order = left.Template.itemValue.type.CompareTo(right.Template.itemValue.type);
            return order != 0 ? order : left.Order.CompareTo(right.Order);
        }

        internal static int TransferCount(long total, int maxCount)
        {
            return (int)Math.Min(Math.Max(0L, total), Math.Max(1, maxCount));
        }

        internal static string CountLabel(long count)
        {
            if (count < 100000) return count.ToString("N0", CultureInfo.InvariantCulture);
            double divisor = count < 1000000 ? 1000d : count < 1000000000 ? 1000000d : 1000000000d;
            string suffix = count < 1000000 ? "k" : count < 1000000000 ? "M" : "B";
            // Truncate the abbreviation; never advertise rounded-up stock.
            return (Math.Floor(count / divisor * 10d) / 10d).ToString("0.#", CultureInfo.InvariantCulture) + suffix;
        }
    }
}
