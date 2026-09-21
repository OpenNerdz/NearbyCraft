using NearbyCraft;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void Define(int id, string name, string category = null, int weight = 1, int fuel = 0, int max = 100)
    => ItemClass.Registry[id] = new ItemClass { Id = id, Name = name, Weight = weight, Fuel = fuel, MaxCount = max, MadeOfMaterial = new MaterialBlock { ForgeCategory = category } };
Define(1, "resourceWood", fuel: 10);
Define(2, "ingredient"); Define(3, "product"); Define(4, "unit_iron", "iron", max: 30000);
Define(5, "resourceScrapIron", "iron", 5); Define(6, "drinkJarEmpty"); Define(7, "drinkJarRiverWater");
ItemStack S(int type, int count) => new(new ItemValue(type), count);
RecipeQueueItem Batch(bool forge = false, int quantity = 2) => new() {
    Recipe = new Recipe { itemValueType = 3, count = 1, materialBasedRecipe = forge, ingredients = new() { S(forge ? 4 : 2, 3) } },
    Multiplier = (short)quantity, OneItemCraftTime = 10, CraftingTimeLeft = 10
};
var world = new World();
TileEntityWorkstation Station(bool forge = false)
{
    var s = new TileEntityWorkstation { Position = new Vector3i(1, 2, 3) };
    s.isModuleUsed[(int)TileEntityWorkstation.Module.Output] = true;
    s.isModuleUsed[(int)TileEntityWorkstation.Module.Fuel] = true;
    if (forge) {
        s.isModuleUsed[(int)TileEntityWorkstation.Module.Material_Input] = true;
        s.Input = new[] { ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), S(4, 0) };
    }
    world.Devices[s.Position] = s;
    return s;
}
var s = Station();
var chest = new[] { S(2, 20), S(1, 10), ItemStack.Empty.Clone() };
var network = new StorageNetworkSession(world, chest);
Check(network.QueueBatch(s, Batch(), Array.Empty<ItemStack>(), true, out _, out _), "Native queue accepts fully paid recipe and automatic fuel");
Check(chest[0].count == 14 && chest[1].count == 8 && s.Fuel[0].count == 2 && s.IsBurning && s.Queue[3].Multiplier == 2,
    "Ingredients, wood, fuel slots and queue commit together");
Check(!network.QueueBatch(s, Batch(), Array.Empty<ItemStack>(), true, out _, out _) && chest[0].count == 14, "Busy station cannot be charged again");

foreach (string failure in new[] { "no fuel", "locked wood", "output full", "network full", "in use", "water", "removed", "changed output" })
{
    s = Station(); chest = new[] { S(2, 100), S(1, 100), ItemStack.Empty.Clone() };
    network = new StorageNetworkSession(world, chest);
    if (failure == "no fuel") chest[1] = ItemStack.Empty.Clone();
    if (failure == "locked wood") network.Locks[1] = true;
    if (failure == "output full") s.Output = Enumerable.Range(0, 6).Select(_ => S(2, 100)).ToArray();
    if (failure == "network full") chest[2] = S(2, 100);
    if (failure == "in use") s.Busy = true;
    if (failure == "water") s.IsBesideWater = true;
    if (failure == "removed") network.BeforeCommit = () => world.Devices.Remove(s.Position);
    if (failure == "changed output") network.BeforeCommit = () => s.Output[0] = S(2, 1);
    int total = chest.Sum(i => i.count);
    Check(!network.QueueBatch(s, Batch(), Array.Empty<ItemStack>(), true, out _, out _), failure + " blocks queue");
    Check(chest.Sum(i => i.count) == total && !s.hasRecipeInQueue() && s.Fuel.All(i => i.IsEmpty()), failure + " leaves payment and queue untouched");
}

s = Station(true); s.Input[3] = S(4, 20);
chest = new[] { S(1, 10), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
Check(network.QueueBatch(s, Batch(true), Array.Empty<ItemStack>(), true, out _, out _) && s.Input[3].count == 14, "Forge consumes actual smelted units and queues native work");
s = Station(true); s.Input[0] = S(5, 10);
chest = new[] { S(1, 10), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
Check(!network.QueueBatch(s, Batch(true), Array.Empty<ItemStack>(), true, out var missing, out _) && missing.itemValue.type == 4
    && s.Input[0].count == 10 && chest[0].count == 10, "Forge cannot spend unsmelted raw items or take fuel on rejection");
s = Station(true); chest = new[] { S(5, 20), S(1, 20), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
Check(network.FeedForge(s, Batch(true).Recipe, 2, true, out _), "Forge feed accepts real raw ore and wood");
Check(chest[0].count == 18 && s.Input[0].count == 1 && s.Input[1].count == 1 && s.Input[3].count == 0 && s.IsBurning, "Feeding splits input across native lanes without minting units");
Check(!network.FeedForge(s, Batch(true).Recipe, 2, true, out _) && chest[0].count == 18, "Pending raw ore prevents repeated forge feeding");
s = Station(true); chest = new[] { S(5, 20), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
Check(!network.FeedForge(s, Batch(true).Recipe, 2, true, out _) && chest[0].count == 20 && s.Input[0].IsEmpty(), "Missing fuel rejects the entire forge feed");

s = Station(); s.Queue[3] = Batch(); chest = new[] { S(1, 20), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
Check(network.MaintainFuel(s, true, out _) && s.IsBurning && chest[0].count == 18, "Existing native queues refuel and relight");
Check(network.MaintainFuel(s, true, out _) && chest[0].count == 18, "A sufficient fuel buffer is not repeatedly filled");
s.Queue[3] = null;
Check(network.MaintainFuel(s, true, out _) && !s.IsBurning && s.Fuel[0].count == 2, "Idle fires switch off without deleting unused fuel");
s.Output[0] = S(3, 10); chest[1] = S(3, 97);
Check(network.CollectOutput(s) == 3 && s.Output[0].count == 7 && chest[1].count == 100, "Partial workstation export retains exact remainder");
chest[1].count = 98; int credited = 0;
Check(network.CollectOutput(s, (type, count) => { Check(type == 3, "Delivery receipt uses the collected item type"); credited += count; }) == 2
    && credited == 2 && s.Output[0].count == 5, "Partial collection callback credits only committed output");
Check(network.CollectOutput(s, (_, count) => credited += count) == 0 && credited == 2, "Full storage never reports a delivery receipt");

var collector = new TileEntityCollector { Position = new Vector3i(4, 5, 6) };
collector.block.Name = "cntDewCollector";
collector.collector.Fuels["jar"] = new BlockCollector.FuelType { Items = new[] { "drinkJarEmpty" } };
collector.collector.OrderedSlotOutputs[new BlockCollector.OutputType { Fuel = "jar", OutputItem = "drinkJarRiverWater", OutputItemModded = "drinkJarRiverWater" }] = new() { 0, 1, 2 };
world.Devices[collector.Position] = collector;
collector.Items[0] = S(7, 3);
chest = new[] { S(6, 10), ItemStack.Empty.Clone() }; network = new StorageNetworkSession(world, chest);
network.ServiceCollector(collector, true, Array.Empty<ItemStack>(), out _);
Check(chest[0].count == 9 && chest[1].itemValue.type == 7 && chest[1].count == 3 && collector.Items[0].IsEmpty()
    && collector.FuelSlots[0].count == 1, "Collector atomically exports water and receives real jars");
network.ServiceCollector(collector, true, Array.Empty<ItemStack>(), out _);
Check(chest[0].count == 9, "Collector does not hoard inputs beyond its buffer");
collector.FuelSlots[0] = ItemStack.Empty.Clone(); collector.isBlocked = true;
network.ServiceCollector(collector, true, Array.Empty<ItemStack>(), out _);
Check(chest[0].count == 9, "Blocked collector receives no new inputs");
collector.isBlocked = false; collector.Items[1] = S(7, 1); collector.InProgress.Add(1);
network.ServiceCollector(collector, false, Array.Empty<ItemStack>(), out _);
Check(collector.Items[1].count == 1 && chest[1].count == 3, "Collector's in-progress slot is never exported");
collector.InProgress.Clear(); collector.Items[1] = ItemStack.Empty.Clone(); chest[1] = S(2, 100);
network.ServiceCollector(collector, true, Array.Empty<ItemStack>(), out _);
Check(chest[0].count == 9 && collector.FuelSlots.All(i => i.IsEmpty()), "Full network blocks fresh collector feed");
Console.WriteLine($"PASS: {checks} production machine transaction assertions (world/API stubs; not an in-game test).");
