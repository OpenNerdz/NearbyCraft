using NearbyCraft;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
void Define(int id, string name, string category = null, int weight = 1, int fuel = 0, int max = 30000)
    => ItemClass.Registry[id] = new ItemClass { Id = id, Name = name, Weight = weight, Fuel = fuel, MaxCount = max, MadeOfMaterial = new MaterialBlock { ForgeCategory = category } };
Define(1, "resourceWood", fuel: 10); Define(2, "resourceRockSmall", "stone"); Define(3, "unit_stone", "stone");
Define(4, "cement"); Define(5, "sand"); Define(6, "concrete"); Define(7, "unit_iron", "iron");
Define(8, "resourceScrapIron", "iron", 5); Define(9, "unit_clay", "clay"); Define(10, "resourceClayLump", "clay");
Define(11, "ironProduct"); Define(12, "tool");
ItemStack S(int type, int count) => new(new ItemValue(type), count);
Recipe R(int type, string area, bool material, params ItemStack[] inputs) => new() { itemValueType = type, count = 1, craftingArea = area, materialBasedRecipe = material, ingredients = inputs.ToList() };
XUiM_Recipes.All.Add(R(4, "forge", true, S(3, 1)));
XUiM_Recipes.All.Add(R(5, "mixer", false, S(2, 1)));
XUiM_Recipes.All.Add(R(6, "mixer", false, S(4, 1), S(5, 1), S(2, 1)));
XUiM_Recipes.All.Add(R(11, "forge", true, S(7, 3), S(9, 1)));
var temporary = Directory.CreateTempSubdirectory("nearbycraft-scheduler-");
try
{
    var world = GameManager.Instance.World;
    var controllerPos = new Vector3i(0, 0, 0);
    var stations = new List<TileEntityWorkstation>();
    ItemStack[] chest = Array.Empty<ItemStack>();
    StorageNetworkSession network = null;
    int testWorld = 0;
    void Reset(int forges = 3, int mixers = 0)
    {
        world.Devices.Clear(); stations.Clear(); GamePrefs.SaveName = "Case" + testWorld++;
        WorkshopStore.Initialize(temporary.FullName);
        WorkshopStore.Edit(controllerPos, c => { c.Enabled = true; c.Linked = true; c.ConsoleX = c.ConsoleY = c.ConsoleZ = 0; }, out _);
        chest = ItemStack.CreateArray(24); chest[0] = S(2, 1000); chest[1] = S(1, 1000);
        chest[2] = S(8, 1000); chest[3] = S(10, 1000);
        network = new StorageNetworkSession(world, chest);
        for (int i = 0; i < forges + mixers; i++)
        {
            bool forge = i < forges;
            var station = new TileEntityWorkstation { Position = new Vector3i(i + 1, 0, 0) };
            station.block.Name = forge ? "forge" : "mixer";
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Output] = true;
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Fuel] = forge;
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Material_Input] = forge;
            if (forge) station.Input = new[] { S(0, 0), S(0, 0), S(0, 0), S(3, 0), S(7, 0), S(9, 0) };
            world.Devices[station.Position] = station; stations.Add(station);
        }
    }
    WorkshopControllerData C() => WorkshopStore.Get(controllerPos);
    void Order(string item, int count, bool once = true) => WorkshopStore.Edit(controllerPos,
        c => c.Targets.Add(new WorkshopTarget { Item = item, Target = count, Remaining = once ? count : 0, Once = once, TrackDelivery = once }), out _);
    void Tick() => new WorkshopScheduler(network, world.GetPrimaryPlayer(), new XUi(), C(), stations).Run();
    long Count(int type) => chest.Where(s => s.itemValue.type == type).Sum(s => (long)s.count);
    void Advance()
    {
        foreach (var station in stations)
        {
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Material_Input))
                for (int i = 0; i < station.InputSlotCount; i++)
                {
                    var raw = station.Input[i]; if (raw.IsEmpty()) continue;
                    var unit = station.Input.Skip(station.InputSlotCount).First(s => s.itemValue.ItemClass.MadeOfMaterial.ForgeCategory == raw.itemValue.ItemClass.MadeOfMaterial.ForgeCategory);
                    unit.count += raw.count * raw.itemValue.ItemClass.GetWeight(); station.Input[i] = S(0, 0);
                }
            for (int i = 0; i < station.Queue.Length; i++)
            {
                var q = station.Queue[i]; if (q?.Recipe == null) continue;
                var plan = new StorageTransferPlan(); plan.Add(station.Output, new bool[station.Output.Length]);
                Check(plan.Deposit(S(q.Recipe.itemValueType, q.Multiplier * q.Recipe.count)) == q.Multiplier * q.Recipe.count && plan.TryCommit(_ => true), "Simulated native output fits");
                station.Queue[i] = null;
            }
            network.CollectOutput(station, (type, count) => WorkshopStore.CreditDelivery(C(), ItemClass.GetForId(type).Name, count));
        }
    }

    Reset(); Order("cement", 1); stations[2].Input[3].count = 9;
    Tick();
    Check(stations[2].hasRecipeInQueue() && !stations[0].hasRecipeInQueue() && C().Smelting.Count == 0, "Uses farther preloaded forge before empty nearest forges");
    Check(Count(2) == 1000 && C().Targets[0].Remaining == 0, "Preloaded material avoids unnecessary raw feed and duplicate assignments");

    Reset(); Order("cement", 24); Tick();
    Check(C().Smelting.Count == 3 && C().Smelting.Sum(j => j.Count) == 24, "Splits one order across three forges without duplicate reservations");
    Check(C().Smelting.Max(j => j.Count) - C().Smelting.Min(j => j.Count) <= 1, "Empty forges receive balanced batches");
    long rawAfter = Count(2); Tick(); Tick();
    Check(Count(2) == rawAfter && C().Smelting.Sum(j => j.Count) == 24, "Repeated ticks do not repeat pending smelting demand");
    WorkshopStore.Initialize(temporary.FullName); Tick();
    Check(Count(2) == rawAfter && C().Smelting.Count == 3, "Settings reload preserves forge ownership and demand reservations");
    for (int i = 0; i < 5; i++) { Advance(); Tick(); }
    Check(Count(4) == 24 && C().Targets[0].Remaining == 0 && C().Smelting.Count == 0, "Parallel smelting finishes exact requested total");

    Reset(); Order("cement", 1); Tick();
    Check(C().Smelting.Count == 1 && Count(2) == 999, "Tiny order does not spread duplicate single batches");

    Reset(); Order("ironProduct", 10); chest[3] = S(0, 0); Tick();
    Check(C().Smelting.Count == 0 && Count(8) == 1000 && stations.All(s => s.Input.Take(3).All(i => i.IsEmpty())), "Missing clay prevents speculative iron smelting on every forge");
    chest[3] = S(10, 10); Tick();
    Check(C().Smelting.Count > 1 && C().Smelting.Sum(j => j.Count) == 10, "Supplying missing ingredient automatically resumes balanced assignments");

    Reset(2); Order("cement", 100); Order("ironProduct", 100); Tick();
    Check(C().Smelting.Select(j => j.Owner).Distinct().Count() == 2, "First pass gives competing orders a forge before filling spare capacity");

    Reset(2); Order("cement", 1); stations[1].Input[0] = S(2, 10); Tick();
    Check(C().Smelting.Single().Position == stations[1].Position && Count(2) == 1000, "Uses already-smelting matching input in farther forge");
    C().Targets.Clear(); Tick();
    Check(C().Smelting.Count == 0 && stations[1].Input[0].count == 10, "Removing order releases assignment without deleting real material");

    Reset(3, 1); Order("concrete", 20); Tick();
    Check(stations.Any(s => s.block.Name == "mixer" && s.hasRecipeInQueue()) && C().Smelting.Count > 1, "Even one busy mixer can request other intermediates from multiple forges concurrently");
    for (int i = 0; i < 30; i++) { Advance(); Tick(); }
    Check(Count(6) == 20 && C().Targets[0].Remaining == 0, "Dependency chain completes through parallel native queues");

    Reset(); Order("cement", 12, false); Tick();
    chest[4] = S(4, 12); Tick();
    Check(C().Smelting.Count == 0 && stations.All(s => !s.hasRecipeInQueue()), "Externally satisfied stock demand releases unnecessary unqueued assignments");

    Reset(); Order("cement", 20); Tick();
    var removed = stations[0]; stations.RemoveAt(0); world.Devices.Remove(removed.Position); Tick();
    Check(C().Smelting.All(j => j.Position != removed.Position), "Removed forge is released and remaining work can be reassigned");
    for (int i = 0; i < 10; i++) { Advance(); Tick(); }
    Check(Count(4) == 20, "Surviving forges finish the order after one forge is removed");

    Reset(1); Order("cement", 8); Tick();
    Check(stations[0].Input.Take(3).Select(s => s.count).SequenceEqual(new[] { 3, 3, 2 }), "One material fills all independent native lanes evenly");
    Check(C().Completed.Count == 0 && C().Targets[0].Queued == 0, "Smelting alone never marks an order complete");
    Advance(); Tick();
    Check(C().Completed.Count == 0 && C().Targets[0].Queued == 8 && C().Targets[0].Returned == 0, "Queue submission is not delivery");
    // Simulate native completion without collecting output yet.
    stations[0].Output[0] = S(4, 8); stations[0].Queue = new RecipeQueueItem[4]; Tick();
    Check(C().Completed.Count == 0, "Output sitting inside a machine is not completed history");
    Advance(); Tick(); Tick();
    Check(C().Completed.Count == 1 && C().Completed[0].Produced == 8 && C().Targets[0].CompletedUtcTicks > 0,
        "Collected output creates one bounded completion receipt");
    WorkshopStore.Initialize(temporary.FullName); Tick();
    Check(C().Completed.Count == 1 && C().Completed[0].VerifiedDelivery, "Completion survives reload without duplicate history");

    Reset(1); Order("cement", 5); stations[0].Input[3].count = 5; Tick();
    stations[0].Queue = new RecipeQueueItem[4]; Tick();
    Check(C().Completed.Count == 0 && C().Targets[0].Returned == 0, "Manually cancelled native queue cannot fabricate a delivery receipt");

    Reset(0, 2); Order("sand", 15); stations[1].CraftSpeed = .5f; Tick();
    Check(stations[1].Queue.Last().Multiplier == 10 && stations[0].Queue.Last().Multiplier == 5,
        "Twice-as-fast machine receives twice the work instead of an equal split");
    Check(stations[1].Queue.Last().OneItemCraftTime == 5 && stations[0].Queue.Last().OneItemCraftTime == 10,
        "Estimated machine-specific recipe time is also used by the actual queued batch");
    Reset(0, 2); Order("sand", 1); stations[1].CraftSpeed = .25f; Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "Small requests choose the farther faster compatible machine");
    Reset(0, 2); Order("sand", 5); stations[0].Busy = true; Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "A machine being accessed does not block independent free machines");
    Reset(0, 2); Order("sand", 5); stations[0].Output = Enumerable.Range(0, 6).Select(_ => S(2, 30000)).ToArray(); Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "Output-blocked machine does not absorb the available allocation");
    Reset(2); Order("cement", 1); C().AutoFuel = false;
    stations[1].Fuel[0] = S(1, 5); stations[1].IsBurning = true; Tick();
    Check(C().Smelting.Single().Position == stations[1].Position, "Manual-fuel mode assigns only to a fueled and lit forge");
    Reset(0, 1); Order("sand", 2);
    var fasterRecipe = R(5, "mixer", false, S(2, 1)); fasterRecipe.craftingTime = 1; XUiM_Recipes.All.Add(fasterRecipe); Tick();
    Check(stations[0].Queue.Last().OneItemCraftTime == 1, "Available faster recipe wins over catalog order on one machine");
    XUiM_Recipes.All.Remove(fasterRecipe);
    Reset(0, 1); Order("sand", 1);
    var bulkRecipe = R(5, "mixer", false, S(2, 10)); bulkRecipe.count = 10; bulkRecipe.craftingTime = 30; XUiM_Recipes.All.Add(bulkRecipe); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 1, "Tiny request avoids a slower bulk recipe even when bulk has better throughput");
    Reset(0, 1); Order("sand", 20); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 10 && stations[0].Queue.Last().Multiplier == 2, "Larger request can use the higher-throughput bulk recipe");
    Reset(0, 1); Order("sand", 1, false); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 1, "Stock target below a bulk yield still finds the fitting alternative recipe");
    XUiM_Recipes.All.Remove(bulkRecipe);
    Reset(2); Order("cement", 1);
    stations[1].isModuleUsed[(int)TileEntityWorkstation.Module.Tools] = true;
    stations[1].Tools[0] = S(12, 1); ItemClass.GetForId(12).SmeltMultiplier = .2f;
    Tick(); Check(C().Smelting.Single().Position == stations[1].Position, "Faster native smelting tool wins over nearest forge");
    ItemClass.GetForId(12).SmeltMultiplier = 1;

    var layoutRng = new Random(1800);
    for (int example = 0; example < 400; example++)
    {
        Reset(1); var forge = stations[0]; int lanes = layoutRng.Next(1, 4);
        forge.InputSlotCount = lanes;
        forge.Input = ItemStack.CreateArray(lanes).Concat(new[] { S(3, 0), S(7, 0), S(9, 0) }).ToArray();
        int materials = layoutRng.Next(1, lanes + 1);
        var inputs = new[] { S(3, layoutRng.Next(1, 100)), S(7, layoutRng.Next(1, 100)), S(9, layoutRng.Next(1, 100)) }.Take(materials).ToArray();
        var recipe = R(4, "forge", true, inputs);
        Check(WorkshopForgePlan.Create(forge, recipe, 1, out var plan, out _), "Random lane plan fits all required materials");
        Check(forge.Input.Take(lanes).All(s => s.IsEmpty()), "Dry-run never changes native input stacks");
        foreach (var supply in plan.Supplies)
            Check(plan.Inputs.Take(lanes).Where(s => s.itemValue.type == supply.itemValue.type).Sum(s => s.count) == supply.count,
                "Random lane plan conserves every withdrawn raw item");
        Check(plan.Seconds > 0 && !double.IsNaN(plan.Seconds) && !double.IsInfinity(plan.Seconds), "Random lane plan has finite positive completion estimate");
        if (materials == 1)
            Check(plan.Inputs.Take(lanes).Max(s => s.count) - plan.Inputs.Take(lanes).Min(s => s.count) <= 1,
                "Single-material plan balances two-slot and three-slot forges");
    }
    Reset(1); var twoLane = stations[0]; twoLane.InputSlotCount = 2;
    twoLane.Input = new[] { S(0, 0), S(0, 0), S(3, 0), S(7, 0), S(9, 0) };
    Check(WorkshopForgePlan.Create(twoLane, R(11, "forge", true, S(7, 100), S(9, 2)), 1, out var twoPlan, out _)
        && twoPlan.Inputs[0].itemValue.type != twoPlan.Inputs[1].itemValue.type, "Iron cannot steal clay's second input slot");
    twoLane.Input[0] = S(8, 7); twoLane.Timers[0] = .25f;
    Check(WorkshopForgePlan.Create(twoLane, R(11, "forge", true, S(7, 40)), 1, out var existingPlan, out _)
        && existingPlan.Inputs[0].count == 7 && existingPlan.Inputs[1].count == 1 && twoLane.Timers[0] == .25f,
        "New supply uses spare lane without moving existing raw input or resetting progress");

    var rng = new Random(1700);
    for (int example = 0; example < 150; example++)
    {
        int forgeCount = rng.Next(1, 7), wanted = rng.Next(1, 150), yield = rng.Next(1, 5);
        Reset(forgeCount); XUiM_Recipes.All[0].count = yield;
        Order("cement", wanted);
        foreach (var forge in stations)
        {
            forge.Input[3].count = rng.Next(0, 8);
            if (rng.Next(2) == 0) forge.Input[0] = S(2, rng.Next(0, 8));
        }
        long startingStone = Count(2) + stations.Sum(s => s.Input.Take(3).Sum(i => i.count) + s.Input[3].count);
        for (int tick = 0; tick < 60; tick++)
        {
            Tick();
            Check(C().Smelting.Select(j => j.Position).Distinct().Count() == C().Smelting.Count, "Random: one assignment per forge");
            Check(C().Smelting.Sum(j => j.Count) <= C().Targets[0].Remaining + yield - 1, "Random: assignments cannot double-count order demand");
            Advance();
            if (C().Targets[0].Remaining == 0 && C().Smelting.Count == 0 && stations.All(s => !s.hasRecipeInQueue())) break;
        }
        long produced = ((wanted + yield - 1) / yield) * yield;
        Check(Count(4) == produced && C().Targets[0].Remaining == 0, "Random: exact requested whole-yield output");
        Check(startingStone == Count(2) + stations.Sum(s => s.Input.Take(3).Sum(i => i.count) + s.Input[3].count) + produced / yield, "Random: native units/raw materials conserved");
    }
    XUiM_Recipes.All[0].count = 1;
    Reset(); Order("cement", 20); Directory.CreateDirectory(Path.Combine(temporary.FullName, "workshops.json.tmp")); Tick();
    Check(!WorkshopStore.Writable && !C().Enabled && C().Targets[0].Remaining == 20, "Assignment persistence failure stops automation without reporting unsmelted work as crafted");
    Directory.Delete(Path.Combine(temporary.FullName, "workshops.json.tmp"));
    Console.WriteLine($"PASS: {checks} scheduler integration assertions (production scheduler/transactions/store; native API stubs).");
}
finally { temporary.Delete(true); }
