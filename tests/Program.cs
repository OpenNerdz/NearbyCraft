using NearbyCraft;

int assertions = 0;
void Check(bool result, string message)
{
    assertions++;
    if (!result) throw new Exception(message);
}
WorkshopTests.Run(Check);
TerminalCatalogTests.Run(Check);
StorageOutputTests.Run(Check);
ItemStack Stack(int type, int count, int metadata = 0) => new ItemStack(new ItemValue { type = type, Metadata = metadata }, count);
StorageTransferPlan Plan(ItemStack[] slots, params bool[] locked)
{
    var plan = new StorageTransferPlan();
    plan.Add(slots, locked.Length == 0 ? new bool[slots.Length] : locked);
    return plan;
}

var slots = new[] { Stack(1, 95), ItemStack.Empty, Stack(2, 100) };
var plan = Plan(slots);
Check(plan.Deposit(Stack(1, 120)) == 105, "Partial capacity is exact");
Check(slots[0].count == 95 && slots[1].IsEmpty(), "Planning does not mutate live slots");
Check(plan.TryCommit(_ => true), "Deposit commits");
Check(slots[0].count == 100 && slots[1].count == 100, "Matching stack is filled first");
Check(!plan.TryCommit(_ => true), "Cannot commit twice");

slots = new[] { Stack(1, 10) };
plan = Plan(slots);
Check(plan.Withdraw(Stack(1, 1), 4) == 4 && plan.CanCommit(_ => true),
    "A transaction can be prevalidated before native inventory staging");
Check(slots[0].count == 10, "Prevalidation never commits planned changes");
Check(plan.TryCommit(_ => true) && slots[0].count == 6, "A prevalidated transaction still commits once");

slots = new[] { Stack(1, 90), ItemStack.Empty };
plan = Plan(slots, true, true);
Check(plan.Deposit(Stack(1, 5)) == 0 && plan.Withdraw(Stack(1, 1), 20) == 0, "Locked slots untouched");
Check(!plan.Contains(Stack(1, 1)), "Locked supplies do not seed smart deposit");

slots = new[] { Stack(1, 20), Stack(1, 30) };
plan = Plan(slots);
Check(plan.Withdraw(Stack(1, 1), 99) == 50, "Withdraw returns real amount only");
Check(plan.TryCommit(_ => true) && slots.All(s => s.IsEmpty()), "Withdrawal empties exact sources");

slots = new[] { Stack(1, 10), ItemStack.Empty };
plan = Plan(slots);
plan.Withdraw(Stack(1, 1), 10);
plan.Deposit(Stack(2, 50));
slots[0].count = 9;
Check(!plan.TryCommit(_ => true), "Changed source rejects whole operation");
Check(slots[0].count == 9 && slots[1].IsEmpty(), "Rejection leaves all live state unchanged");

var first = new[] { Stack(1, 10) };
var second = new[] { Stack(2, 10) };
plan = Plan(first);
plan.Add(second, new bool[1]);
plan.Withdraw(Stack(1, 1), 10);
plan.Withdraw(Stack(2, 1), 10);
Check(!plan.TryCommit(index => index == 0), "Invalid second chest aborts before writing first");
Check(first[0].count == 10 && second[0].count == 10, "Multi-chest all-or-nothing validation");

slots = new[] { Stack(1, 100) };
plan = Plan(slots);
Check(plan.Withdraw(Stack(1, 1), 100) == 100 && plan.Deposit(Stack(2, 100)) == 100, "Full chest swap can reuse vacated slot");
Check(plan.TryCommit(_ => true) && slots[0].itemValue.type == 2, "Swap commits");
plan = Plan(slots);
plan.Withdraw(Stack(2, 1), 100);
Check(plan.Deposit(Stack(1, 101)) != 101, "Oversized swap is detectable before commit");
Check(slots[0].itemValue.type == 2 && slots[0].count == 100, "Abandoned swap requires no rollback");

slots = new[] { Stack(1, 40, 3), ItemStack.Empty };
plan = Plan(slots);
Check(!plan.Contains(Stack(1, 10, 4)), "Metadata variants stay distinct");
Check(plan.Withdraw(Stack(1, 1, 4), 50) == 0, "Metadata must match on withdrawal");
Check(plan.Deposit(Stack(1, 10, 4)) == 10 && plan.TryCommit(_ => true), "Variant uses its own slot");
Check(slots[0].count == 40 && slots[1].itemValue.Metadata == 4, "Metadata preserved");

slots = new[] { ItemStack.Empty };
plan = Plan(slots);
var forbidden = Stack(3, 4);
forbidden.itemValue.ItemClassOrMissing.CanStore = false;
Check(plan.Deposit(forbidden) == 0, "Container restrictions enforced");
plan.Deposit(Stack(1, 1));
Check(!plan.Contains(Stack(1, 1)), "Fresh deposits do not seed matching-only policy");
Check(plan.Deposit(ItemStack.Empty) == 0 && plan.Withdraw(ItemStack.Empty, 1) == 0, "Empty requests ignored");

var random = new Random(251570);
var storedSupply = Stack(1, 70);
storedSupply.itemValue.Seed = 123;
var carriedSupply = Stack(1, 20);
carriedSupply.itemValue.Seed = 456;
slots = new[] { storedSupply };
plan = Plan(slots);
Check(plan.Contains(carriedSupply), "Ordinary supplies match despite different native seeds");
Check(plan.Deposit(carriedSupply) == 20 && plan.TryCommit(_ => true) && slots[0].count == 90,
    "Different-seed supplies fill an existing stack without empty slots");
plan = Plan(slots);
plan.Withdraw(carriedSupply, 5);
slots[0].itemValue.Seed = 789;
Check(!plan.TryCommit(_ => true) && slots[0].count == 90, "Snapshot validation still detects seed changes");
carriedSupply.itemValue.Metadata = 5;
Check(!StorageTransferPlan.Matches(storedSupply, carriedSupply), "Ignoring supply seed does not ignore metadata");
carriedSupply.itemValue.Metadata = 0;
carriedSupply.itemValue.HasQuality = storedSupply.itemValue.HasQuality = true;
Check(!StorageTransferPlan.Matches(storedSupply, carriedSupply), "Quality-bearing seeds remain distinct");
carriedSupply.itemValue.HasQuality = storedSupply.itemValue.HasQuality = false;
carriedSupply.itemValue.TextureFullArray = 4;
Check(!StorageTransferPlan.Matches(storedSupply, carriedSupply), "Block textures remain distinct");
carriedSupply.itemValue.TextureFullArray = 0;
carriedSupply.itemValue.Flags = 2;
Check(!StorageTransferPlan.Matches(storedSupply, carriedSupply), "Item flags remain distinct");
Check(TerminalRules.IsRightClick(-2) && !TerminalRules.IsRightClick(-1)
    && !TerminalRules.IsRightClick(1), "Right-click uses NGUI -2, not Unity button 1");
Check(TerminalRules.CanShiftToInventory(true, false), "Shift-click accepts backpack-only items");
Check(TerminalRules.CanShiftToInventory(false, true), "Shift-click accepts toolbelt-only items");
Check(!TerminalRules.CanShiftToInventory(false, false), "Shift-click rejects items that fit neither inventory");
Check(TerminalRules.CanBulkDepositSlot(false, true), "Deposit All includes locked backpack slots");
Check(TerminalRules.CanBulkDepositSlot(false, false), "Deposit All includes unlocked backpack slots");
Check(!TerminalRules.CanBulkDepositSlot(true, true), "Matching Only preserves locked backpack slots");
Check(TerminalRules.CanBulkDepositSlot(true, false), "Matching Only includes unlocked backpack slots");
foreach (bool matchingOnly in new[] { false, true })
{
    var backpack = new[] { Stack(1, 20), Stack(1, 30) };
    var backpackLocks = new[] { true, false };
    var chest = new[] { Stack(1, 10), ItemStack.Empty };
    var reserveCounts = new Dictionary<string, int> { ["supply"] = 5 };
    var keptCounts = new Dictionary<string, int>();
    var transfer = Plan(chest);
    int moved = 0;
    for (int i = 0; i < backpack.Length; i++)
    {
        if (!TerminalRules.CanBulkDepositSlot(matchingOnly, backpackLocks[i])) continue;
        int amount = TerminalRules.Depositable("supply", backpack[i].count, reserveCounts, keptCounts);
        var request = backpack[i].Clone();
        request.count = amount;
        if (matchingOnly && !transfer.Contains(request)) continue;
        int deposited = transfer.Deposit(request);
        backpack[i].count -= deposited;
        moved += deposited;
    }
    Check(transfer.TryCommit(_ => true), "Bulk mode transfer commits");
    Check(moved == (matchingOnly ? 25 : 45), "Each mode moves the correct total with a backpack lock and reserve");
    Check(backpack[0].count == (matchingOnly ? 20 : 5), "Only Deposit All draws from the locked backpack slot");
    Check(backpack.Sum(s => s.count) + chest.Sum(s => s.count) == 60, "Bulk lock policy preserves item totals");
    Check(backpackLocks[0], "Depositing does not unlock the backpack slot itself");
}
Check(TerminalRules.GetTier(TerminalRules.BlockName) == 1, "Old consoles remain Tier 1");
Check(TerminalRules.GetTier("nearbyCraftStorageTerminalTier5") == 0 && TerminalRules.GetTier(null) == 0, "Only defined consoles recognized");
for (int tier = 1; tier <= 4; tier++)
    Check(TerminalRules.ChestLimit(tier) == new[] { 8, 16, 32, 64 }[tier - 1], "Tier chest limit");
Check(TerminalRules.ChestLimit(0) == 0 && TerminalRules.ChestLimit(5) == 0, "Invalid tiers cannot allocate storage");
var reserves = new Dictionary<string, int> { ["ammo"] = 150, ["food"] = 10 };
var retained = new Dictionary<string, int>();
Check(TerminalRules.Depositable("ammo", 100, reserves, retained) == 0, "Reserve consumes first stack");
Check(TerminalRules.Depositable("ammo", 100, reserves, retained) == 50, "Reserve spans stacks without doubling");
Check(TerminalRules.Depositable("ammo", 50, reserves, retained) == 50, "Remaining surplus deposits");
Check(TerminalRules.Depositable("food", 20, reserves, retained) == 10, "Reserves independent by type");
Check(TerminalRules.Depositable("wood", 90, reserves, retained) == 90, "Unreserved supplies deposit normally");

var networkSlots = new[] { Stack(2, 1), ItemStack.Empty };
var networkPlan = Plan(networkSlots);
LoadoutSwapResult swap;
Check(LoadoutSwapPlanner.TryPlan(new[] { Stack(1, 1), Stack(2, 1) },
    new[] { Stack(2, 1), Stack(1, 1) }, networkPlan, out swap), "Player-slot permutations need no network items");
Check(swap.Withdrawn == 0 && swap.Deposited == 0, "Permutation reuses carried items instead of network round trips");
Check(networkPlan.TryCommit(_ => true) && networkSlots[0].itemValue.type == 2, "Permutation leaves network unchanged");

networkSlots = new[] { Stack(2, 1) };
networkPlan = Plan(networkSlots);
Check(LoadoutSwapPlanner.TryPlan(new[] { Stack(1, 1) }, new[] { Stack(2, 1) }, networkPlan, out swap),
    "Loadout can exchange through a full network by withdrawing before depositing");
Check(swap.Withdrawn == 1 && swap.Deposited == 1 && networkPlan.TryCommit(_ => true), "Exact swap commits both sides");
Check(networkSlots[0].itemValue.type == 1 && networkSlots[0].count == 1, "Outgoing item occupies space freed by requested item");

networkSlots = new[] { Stack(2, 1) };
networkPlan = Plan(networkSlots);
Check(!LoadoutSwapPlanner.TryPlan(new[] { Stack(1, 1) }, new[] { Stack(3, 1) }, networkPlan, out swap),
    "Missing requested loadout item rejects whole plan");
Check(swap.ProblemItem.itemValue.type == 3 && swap.ProblemCount == 1, "Missing item is reported precisely");
Check(networkSlots[0].itemValue.type == 2, "Rejected loadout never mutates live network");

networkSlots = new[] { Stack(2, 100) };
networkPlan = Plan(networkSlots);
Check(!LoadoutSwapPlanner.TryPlan(new[] { Stack(1, 1) }, new[] { ItemStack.Empty }, networkPlan, out swap),
    "No unlocked network space rejects outgoing loadout items");
Check(networkSlots[0].itemValue.type == 2 && networkSlots[0].count == 100, "Full-network rejection preserves live counts");

var variantA = Stack(1, 1, 7);
var variantB = Stack(1, 1, 8);
networkSlots = new[] { variantB.Clone() };
networkPlan = Plan(networkSlots);
Check(LoadoutSwapPlanner.TryPlan(new[] { variantA.Clone() }, new[] { variantB.Clone() }, networkPlan, out swap)
    && swap.Withdrawn == 1 && swap.Deposited == 1, "Loadouts retain metadata-distinct equipment variants");
Check(networkPlan.TryCommit(_ => true) && networkSlots[0].itemValue.Metadata == 7, "Metadata variant exchange commits exactly");

var configuration = new NearbyCraftConfig { Range = 100, TerminalRange = -1, CacheMilliseconds = 0, TerminalSort = "count", PersonalReserves = null };
configuration.Validate();
Check(configuration.Range == 30 && configuration.TerminalRange == 1 && configuration.CacheMilliseconds == 100 && configuration.TerminalSort == "Count" && configuration.PersonalReserves.Count == 0, "Config migration and clamps");
for (int test = 0; test < 3000; test++)
{
    slots = Enumerable.Range(0, 12).Select(_ => random.Next(4) == 0 ? ItemStack.Empty : Stack(random.Next(1, 4), random.Next(1, 101), random.Next(2))).ToArray();
    var locks = slots.Select(_ => random.Next(4) == 0).ToArray();
    var before = ItemStack.Clone(slots);
    plan = Plan(slots, locks);
    var request = Stack(random.Next(1, 4), random.Next(1, 301), random.Next(2));
    bool deposit = random.Next(2) == 0;
    int moved = deposit ? plan.Deposit(request) : plan.Withdraw(request, request.count);
    Check(moved >= 0 && moved <= request.count, "Randomized transfer bounds");
    Check(plan.TryCommit(_ => true), "Randomized valid commit");
    int Total(ItemStack[] items, int type, int metadata) => items.Where(s => s.itemValue.type == type && s.itemValue.Metadata == metadata).Sum(s => s.count);
    for (int type = 1; type <= 3; type++)
        for (int metadata = 0; metadata <= 1; metadata++)
        {
            int expected = type == request.itemValue.type && metadata == request.itemValue.Metadata ? (deposit ? moved : -moved) : 0;
            Check(Total(slots, type, metadata) - Total(before, type, metadata) == expected, "Per-variant item conservation");
        }
    for (int i = 0; i < slots.Length; i++)
    {
        Check(slots[i].count >= 0 && slots[i].count <= 100, "Stack capacity maintained");
        if (locks[i]) Check(slots[i].count == before[i].count && slots[i].itemValue.Equals(before[i].itemValue), "Randomized lock preservation");
    }
}
Console.WriteLine($"PASS: {assertions} assertions, including 3000 existing + 2000 workshop + 1000 grouped-display + 10000 ore-export randomized scenarios.");
