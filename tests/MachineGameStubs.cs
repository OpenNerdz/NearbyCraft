// Minimal world/API model: the production machine transaction, fuel, forge and
// collector implementation is compiled unchanged. These are not Unity tests.
public sealed partial class ItemClass
{
    public static readonly Dictionary<int, ItemClass> Registry = new();
    public int Id, Weight = 1, Fuel;
    public float MeltTimePerUnit = 1, SmeltMultiplier = 1;
    public string Name;
    public MaterialBlock MadeOfMaterial = new();
    public int GetWeight() => Weight;
    public string GetItemName() => Name;
    public string GetLocalizedItemName() => Name;
    public static ItemClass GetForId(int id) => Registry.GetValueOrDefault(id);
    public static ItemValue GetItem(string name, bool _) => Registry.Values.FirstOrDefault(c => c.Name == name) is { } c ? new ItemValue(c.Id) : new ItemValue();
    public static int GetFuelValue(ItemValue value) => value.ItemClass.Fuel;
}
public sealed partial class ItemValue
{
    public ItemValue() { }
    public ItemValue(int id, int min = 0, int max = 0) { type = id; ItemClassOrMissing = ItemClass.GetForId(id) ?? new ItemClass(); }
    public ItemClass ItemClass => ItemClassOrMissing;
    public bool IsEmpty() => type == 0;
    public void ModifyValue(object entity, object recipe, PassiveEffects effect, ref float value, ref float multiplier, FastTags<TagGroup.Global> tags)
    { multiplier *= ItemClass.SmeltMultiplier; }
}
public sealed partial class ItemStack
{
    public static ItemStack[] CreateArray(int count) => Enumerable.Range(0, count).Select(_ => Empty.Clone()).ToArray();
}
public class MaterialBlock { public string ForgeCategory; }
public record struct Vector3i(int x, int y, int z);
public class Block
{
    public string Name = "workbench";
    public string GetBlockName() => Name;
}
public class TileEntity
{
    public bool IsRemoving, Busy, Allowed = true;
    public int Notifications;
    public Vector3i Position;
    public Block block = new();
    public bool IsUserAccessing() => Busy;
    public Vector3i ToWorldPos() => Position;
    public void SetModified() => Notifications++;
    public void NotifyListeners() { }
}
public partial class TileEntityWorkstation : TileEntity
{
    public ItemStack[] Tools = ItemStack.CreateArray(3);
    public float[] Timers = new float[] { -1, -1, -1 };
    public float GetTimerForSlot(int slot) => slot < Timers.Length ? Timers[slot] : -1;
    public enum Module { Tools, Input, Output, Fuel, Material_Input, Count }
    public bool[] isModuleUsed = new bool[5];
    public ItemStack[] Output = ItemStack.CreateArray(6), Fuel = ItemStack.CreateArray(3), Input = ItemStack.CreateArray(3);
    public RecipeQueueItem[] Queue = new RecipeQueueItem[4];
    public bool IsPlayerPlaced = true, IsBurning, IsBesideWater;
    public ulong lastTickTime;
    public float BurnTimeLeft;
    public float BurnTotalTimeLeft => BurnTimeLeft + Fuel.Where(s => s != null && !s.IsEmpty()).Sum(s => s.count * ItemClass.GetFuelValue(s.itemValue));
    public int InputSlotCount { get; set; } = 3;
    public bool hasRecipeInQueue() => Queue.Any(q => q?.Recipe != null && q.Multiplier > 0);
    public void ResetTickTime() => lastTickTime = GameTimer.Instance.ticks;
    public bool AcceptsMaterial(MaterialBlock m) => m.ForgeCategory != null;
}
public partial class Recipe
{
    public int itemValueType, count;
    public bool materialBasedRecipe;
    public List<ItemStack> ingredients = new();
    public string GetName() => ItemClass.GetForId(itemValueType).Name;
}
public partial class RecipeQueueItem
{
    public Recipe Recipe;
    public ItemValue RepairItem;
    public short Multiplier;
    public int Quality;
    public float OneItemCraftTime, CraftingTimeLeft;
}
public partial class World
{
    public Dictionary<Vector3i, TileEntity> Devices = new();
    public TileEntity GetTileEntity(Vector3i position) => Devices.GetValueOrDefault(position);
}
public class GameTimer { public static GameTimer Instance = new(); public ulong ticks = 100; }
public static partial class XUiM_Recipes { public static bool DisableSmelter; }
public static class Localization { public static string Get(string name) => name; }
public enum PassiveEffects { CraftingSmeltTime }
public static class TagGroup { public class Global { } }
public struct FastTags<T> { public static FastTags<T> Parse(string s) => default; }
public static class Log { public static void Error(string f, params object[] args) { } }
public class BlockCollector : Block
{
    public class OutputType { public string Fuel, OutputItem, OutputItemModded; public int Cost = 1; }
    public class FuelType { public string[] Items; }
    public Dictionary<OutputType, List<int>> OrderedSlotOutputs = new();
    public Dictionary<string, FuelType> Fuels = new();
    public bool UsesFuel() => true;
    public FuelType GetFuelType(string name) => Fuels[name];
    public int GetSandboxModifiedFuelNeeded(int cost) => cost;
}
public class TileEntityCollector : TileEntity
{
    public BlockCollector collector = new();
    public ItemStack[] Items = ItemStack.CreateArray(3), FuelSlots = ItemStack.CreateArray(3);
    public bool isUnderwater, isBlocked, HasModConvert, visibleChanged;
    public int Count = 1;
    public HashSet<int> InProgress = new();
    public bool IsCurrentStack(int slot) => InProgress.Contains(slot);
    public int getCurrentConvertCount(BlockCollector.OutputType output) => Count;
    public int fuelCost(BlockCollector.OutputType output, int count) => output.Cost * count;
}
namespace NearbyCraft
{
    internal static partial class WorkshopManager { internal static bool Accessible(TileEntity entity) => entity != null && !entity.IsRemoving && entity.Allowed; }
    internal static class StorageIndex { internal static void Invalidate() { } }
    internal static class StorageTerminalManager { internal static void RequestItemsRefresh() { } }
    internal sealed partial class StorageNetworkSession
    {
        private World world;
        private ItemStack[] chests;
        private class TestStorage { internal ItemStack[] items; }
        private class TestSource { internal Vector3i Position; internal TestStorage Storage; }
        private readonly List<TestSource> sources = new();
        private bool IsSourceValid(TestSource source) => IsAvailable;
        private bool IsSlotLocked(TestSource source, int index) => Locks[index];
        internal bool IsAvailable = true, AutomationBusy = false;
        internal bool[] Locks;
        internal Action BeforeCommit = null;
        internal StorageNetworkSession(World world, ItemStack[] items) {
            this.world = world; chests = items; Locks = new bool[items.Length];
            sources.Add(new TestSource { Position = new Vector3i(), Storage = new TestStorage { items = items } });
        }
        private class Transaction { internal StorageTransferPlan Plan = new(); }
        private Transaction BeginTransaction()
        {
            var t = new Transaction();
            t.Plan.Add(chests, (bool[])Locks.Clone());
            return t;
        }
        private bool Commit(Transaction t, Func<bool> validate = null, Action apply = null)
        {
            BeforeCommit?.Invoke();
            if (!IsAvailable || AutomationBusy || (validate != null && !validate()) || !t.Plan.TryCommit(_ => true)) return false;
            apply?.Invoke();
            return true;
        }
    }
}
