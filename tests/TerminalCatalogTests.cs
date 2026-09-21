using NearbyCraft;

internal static class TerminalCatalogTests
{
    internal static void Run(Action<bool, string> check)
    {
        ItemStack Stack(int type, int count, ushort seed = 0, int metadata = 0) =>
            new ItemStack(new ItemValue { type = type, Seed = seed, Metadata = metadata }, count);
        string Name(ItemStack stack) => stack.itemValue.type == 1 ? "Wood" : stack.itemValue.type == 2 ? "Ammo" : "Bandages";
        var catalog = new TerminalCatalog(Name, s => "resource" + s.itemValue.type);
        var result = new List<TerminalCatalog.Entry>();
        var original = new[] { Stack(2, 100, 12), Stack(2, 75, 34), Stack(1, 200), Stack(3, 20) };
        foreach (var stack in original) catalog.Add(stack);
        catalog.FilterAndSort("", StorageTerminalSort.Name, result);
        check(result.Count == 3 && result[0].Count == 175, "Different-seed ammunition combines into one total entry");
        check(result.Select(e => e.DisplayName).SequenceEqual(new[] { "Ammo", "Bandages", "Wood" }), "Name quick sort is alphabetical");
        check(original[0].count == 100 && original[1].count == 75 && original[0].itemValue.Seed == 12, "Grouping does not mutate chest inventory");
        var transfer = result[0].TransferStack();
        check(transfer.count == 100 && result[0].Count == 175, "A displayed aggregate withdraws at most one native stack");
        transfer.count = 1;
        transfer.itemValue.Metadata = 999;
        check(result[0].TransferStack().count == 100 && result[0].Template.itemValue.Metadata == 0, "Cursor clones cannot mutate the catalog");
        catalog.FilterAndSort("", StorageTerminalSort.Count, result);
        check(result.Select(e => e.Count).SequenceEqual(new long[] { 200, 175, 20 }), "Quantity sort uses the TOTAL, not capped transfer size");
        catalog.FilterAndSort("", StorageTerminalSort.Type, result);
        check(result.Select(e => e.Template.itemValue.type).SequenceEqual(new[] { 1, 2, 3 }), "Type quick sort uses native item IDs");
        catalog.FilterAndSort("  aMmO  ", StorageTerminalSort.Name, result);
        check(result.Count == 1 && result[0].Count == 175, "Search finds the whole group, case insensitive and trimmed");
        catalog.FilterAndSort("RESOURCE1", StorageTerminalSort.Name, result);
        check(result.Count == 1 && result[0].DisplayName == "Wood", "Internal-name search still works");
        catalog.FilterAndSort("missing", StorageTerminalSort.Name, result);
        check(result.Count == 0, "Empty search results remove stale entries");

        catalog.Clear();
        catalog.Add(Stack(2, 30));
        catalog.Add(Stack(2, 40, metadata: 1));
        var flagged = Stack(2, 50); flagged.itemValue.Flags = 1; catalog.Add(flagged);
        var painted = Stack(2, 60); painted.itemValue.TextureFullArray = 1; catalog.Add(painted);
        catalog.FilterAndSort("", StorageTerminalSort.Name, result);
        check(result.Count == 4 && result.Sum(e => e.Count) == 180, "Metadata, flags and paint variants remain separate and retain totals");
        var stable = result.ToArray();
        catalog.FilterAndSort("", StorageTerminalSort.Name, result);
        check(result.SequenceEqual(stable), "Sorting ties is deterministic");

        catalog.Clear();
        var equipment = Stack(3, 1); equipment.itemValue.HasQuality = true;
        catalog.Add(equipment); catalog.Add(equipment);
        var tool = Stack(1, 1); tool.itemValue.ItemClassOrMissing.MaxCount = 1;
        catalog.Add(tool); catalog.Add(tool);
        catalog.FilterAndSort("", StorageTerminalSort.Name, result);
        check(result.Count == 4 && result.All(e => e.TransferStack().count == 1), "Quality equipment and non-stackables keep their own cells");

        catalog.Clear();
        catalog.Add(Stack(2, int.MaxValue)); catalog.Add(Stack(2, int.MaxValue));
        catalog.FilterAndSort("", StorageTerminalSort.Count, result);
        check(result.Count == 1 && result[0].Count == 2L * int.MaxValue, "Aggregate totals do not overflow 32-bit counts");
        check(result[0].TransferStack().count == 100, "Even huge virtual totals cannot create oversized cursor stacks");
        check(TerminalCatalog.TransferCount(0, 100) == 0 && TerminalCatalog.TransferCount(-1, 100) == 0
            && TerminalCatalog.TransferCount(20, 0) == 1 && TerminalCatalog.TransferCount(long.MaxValue, 100) == 100,
            "Transfer quantity clamps safely for empty and malformed values");
        check(TerminalCatalog.CountLabel(12600) == "12,600" && TerminalCatalog.CountLabel(100000) == "100k"
            && TerminalCatalog.CountLabel(1299999) == "1.2M" && TerminalCatalog.CountLabel(2147483648) == "2.1B",
            "Readable count labels retain precision in the tooltip and never round up");
        catalog.Clear(); catalog.Add(null); catalog.Add(ItemStack.Empty);
        catalog.FilterAndSort("", StorageTerminalSort.Name, result);
        check(result.Count == 0, "Clearing catalog and ignoring empty slots removes stale totals");

        // Exercise presentation -> real withdrawal -> presentation, with actual
        // production transfer code. Sources include separately created ammo seeds.
        var random = new Random(150121);
        for (int run = 0; run < 1000; run++)
        {
            var slots = Enumerable.Range(0, random.Next(1, 40))
                .Select(_ => Stack(random.Next(1, 4), random.Next(1, 101), (ushort)random.Next(1, 65000))).ToArray();
            catalog.Clear();
            foreach (var stack in slots) catalog.Add(stack);
            catalog.FilterAndSort("", StorageTerminalSort.Count, result);
            check(result.Count == slots.Select(s => s.itemValue.type).Distinct().Count(), "Random grouping yields one cell per ordinary supply");
            check(result.Sum(e => e.Count) == slots.Sum(s => (long)s.count), "Random aggregation preserves total inventory");
            var entry = result[random.Next(result.Count)];
            var request = entry.TransferStack();
            check(request.count > 0 && request.count <= 100 && request.count <= entry.Count, "Random cursor request stays bounded");
            if ((run & 1) == 0) request.count = Math.Max(1, request.count / 2);
            var plan = new StorageTransferPlan();
            plan.Add(slots, new bool[slots.Length]);
            check(plan.Withdraw(request, request.count) == request.count && plan.TryCommit(_ => true), "Grouped full/half-stack withdrawal uses real items");
            long left = slots.Where(s => s.itemValue.type == request.itemValue.type).Sum(s => (long)s.count);
            check(left + request.count == entry.Count, "Grouped withdrawal conserves the exact item total");
            catalog.Clear();
            foreach (var stack in slots) catalog.Add(stack);
            catalog.FilterAndSort("", StorageTerminalSort.Name, result);
            check(result.Where(e => e.Template.itemValue.type == request.itemValue.type).Sum(e => e.Count) == left,
                "Refresh displays the exact remaining group after a transfer");
        }
    }
}
