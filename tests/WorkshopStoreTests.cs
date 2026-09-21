using NearbyCraft;

int checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new Exception(message);
}
var temporary = Directory.CreateTempSubdirectory("nearbycraft-workshop-store-test-");
try
{
    string folder = temporary.FullName;
    string path = Path.Combine(folder, "workshops.json");
    var position = new Vector3i(10, 50, -20);
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Writable && WorkshopStore.Controllers.Count == 0, "New save starts empty");
    Check(WorkshopStore.Edit(position, c => c.Targets.Add(new WorkshopTarget { Item = "ammo", Target = 500 }), out _), "First save succeeds");
    Check(!WorkshopStore.Get(position).Enabled, "New controller starts paused");
    Check(WorkshopStore.Get(position).AutoFuel && WorkshopStore.Get(position).AutoCraft
        && WorkshopStore.Get(position).ExcludedStations.Count == 0 && !WorkshopStore.Get(position).Targets[0].Once,
        "Legacy targets stay stock-based; automation options have migration defaults");
    string first = File.ReadAllText(path);
    Check(WorkshopStore.Edit(position, c => { c.Enabled = true; c.Linked = true; c.ConsoleX = 12; c.ConsoleY = 50; c.ConsoleZ = -22; }, out _), "Second save succeeds");
    Check(File.ReadAllText(path + ".bak") == first, "Atomic update keeps the exact previous revision");
    WorkshopStore.Initialize(folder);
    var restored = WorkshopStore.Get(position);
    Check(restored.Enabled && restored.Linked && restored.ConsoleX == 12 && restored.ConsoleY == 50 && restored.ConsoleZ == -22
        && restored.Targets[0].Target == 500, "Restart preserves state, link and targets");
    Check(restored.Smelting.Count == 0, "Existing saves migrate with no speculative forge assignments");
    restored.Smelting.Add(new WorkshopSmeltAssignment { X = 1, Y = 2, Z = 3, Owner = "ammo", Item = "cement", RecipeKey = "cement/forge/1/0/unit_stone:1", Batches = 5, Count = 5 });
    Check(WorkshopStore.SaveProgress(out _), "Smelting allocation persists");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Get(position).Smelting.Single().Count == 5 && WorkshopStore.Get(position).Smelting[0].Owner == "ammo",
        "Restart retains assigned product, owner and quantity");

    Check(WorkshopStore.Get(position).Completed.Count == 0, "Older save migrates to empty completion history");
    var delivery = new WorkshopTarget { Item = "concrete", Target = 12, Once = true, TrackDelivery = true, Queued = 12 };
    WorkshopStore.Get(position).Targets.Add(delivery);
    Check(WorkshopStore.CreditDelivery(WorkshopStore.Get(position), "concrete", 5) && delivery.Returned == 5,
        "Partial collection credits only the actual amount");
    Check(WorkshopStore.CreditDelivery(WorkshopStore.Get(position), "concrete", 100) && delivery.Returned == 12,
        "Collection cannot exceed recorded production");
    Check(!WorkshopStore.CreditDelivery(WorkshopStore.Get(position), "concrete", 1), "Repeated collection cannot double-credit completed quantity");
    WorkshopStore.RecordCompletion(WorkshopStore.Get(position), delivery, true);
    WorkshopStore.RecordCompletion(WorkshopStore.Get(position), delivery, true);
    Check(WorkshopStore.Get(position).Completed.Count == 1, "One request is archived once");
    Check(WorkshopStore.SaveProgress(out _), "Delivery history persists");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Get(position).Completed.Single().Produced == 12
        && WorkshopStore.Get(position).Targets.Single(t => t.Item == "concrete").Returned == 12, "Receipt and delivered progress round trip");
    WorkshopStore.Get(position).Targets.RemoveAll(t => t.Item == "concrete");
    for (int i = 0; i < 80; i++) WorkshopStore.RecordCompletion(WorkshopStore.Get(position),
        new WorkshopTarget { Item = "history" + i, Target = 1 }, false);
    Check(WorkshopStore.Get(position).Completed.Count == 60 && WorkshopStore.Get(position).Completed[0].Item == "history79",
        "History is capped to 60 most recent records, newest first");
    Check(WorkshopStore.SaveProgress(out _), "Bounded history saves");

    GamePrefs.SaveName = "DifferentSave";
    Check(WorkshopStore.Get(position) == null, "Same position in another save is isolated");
    Check(WorkshopStore.Edit(position, c => c.Targets.Add(new WorkshopTarget { Item = "food", Target = 10 }), out _), "Second world can save");
    GamePrefs.SaveName = "TestSave";
    Check(WorkshopStore.Get(position).Targets[0].Item == "ammo", "First world's targets retained");

    // Force a real filesystem failure at the temporary-file write.
    Directory.CreateDirectory(path + ".tmp");
    string saved = File.ReadAllText(path);
    Check(!WorkshopStore.Edit(position, c => c.Targets[0].Target = 1, out _), "Failed save is reported");
    Check(WorkshopStore.Get(position).Targets[0].Target == 500 && File.ReadAllText(path) == saved, "Failed save rolls back memory and preserves disk");
    Directory.Delete(path + ".tmp");

    Check(WorkshopStore.Edit(position, c => {
        c.Targets.Add(new WorkshopTarget { Item = "cement", Target = 25, Once = true, Remaining = 15 });
        c.ExcludedStations.Add("(3, 4, 5)"); c.AutoFuel = false; c.AutoCraft = false;
    }, out _), "New order modes and machine exclusions save");
    WorkshopStore.Get(position).Targets[1].Remaining = 5;
    Check(WorkshopStore.SaveProgress(out _), "Committed queue progress persists");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Get(position).Targets[1].Once && WorkshopStore.Get(position).Targets[1].Remaining == 5
        && !WorkshopStore.Get(position).AutoFuel && !WorkshopStore.Get(position).AutoCraft
        && WorkshopStore.Get(position).ExcludedStations.Single() == "(3, 4, 5)", "Orders and machine options survive restart");
    Directory.CreateDirectory(path + ".tmp");
    WorkshopStore.Get(position).Targets[1].Remaining = 0;
    Check(!WorkshopStore.SaveProgress(out _) && !WorkshopStore.Writable && !WorkshopStore.Get(position).Enabled
        && WorkshopStore.Get(position).Targets[1].Remaining == 0, "Post-commit save failure stops automation without rolling back paid-for progress");
    Directory.Delete(path + ".tmp");
    WorkshopStore.Initialize(folder);

    Directory.CreateDirectory(path + ".tmp");
    saved = File.ReadAllText(path);
    Check(!WorkshopStore.Remove(position), "Failed controller removal is reported");
    Check(WorkshopStore.Get(position) != null && File.ReadAllText(path) == saved,
        "Failed controller removal restores memory and preserves disk");
    Directory.Delete(path + ".tmp");
    Check(WorkshopStore.Remove(position), "Controller removal succeeds after storage recovers");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Get(position) == null, "Removed controller stays forgotten after restart");
    GamePrefs.SaveName = "DifferentSave";
    Check(WorkshopStore.Get(position).Targets[0].Item == "food", "Removal leaves other saves alone");

    foreach (string invalid in new[] { "not json", "null", "{\"Format\":2}", "{\"Worlds\":null}",
        "{\"Worlds\":{\"bad\":null}}", "{\"Worlds\":{\"bad\":[{},{}]}}",
        "{\"Worlds\":{\"bad\":[{\"Targets\":[null]}]}}",
        "{\"Worlds\":{\"bad\":[{\"Targets\":[{\"Item\":\"ammo\"},{\"Item\":\"ammo\"}]}]}}",
        "{\"Worlds\":{\"bad\":[{\"Completed\":[null]}]}}",
        "{\"Worlds\":{\"bad\":[{\"Targets\":[{\"Item\":\"ammo\",\"Queued\":1,\"Returned\":2}]}]}}",
        "{\"Worlds\":{\"bad\":[{\"Smelting\":[null]}]}}",
        "{\"Worlds\":{\"bad\":[{\"Smelting\":[{\"Item\":\"cement\",\"Owner\":\"ammo\",\"RecipeKey\":\"x\",\"Batches\":0,\"Count\":1}]}]}}" })
    {
        File.WriteAllText(path, invalid);
        WorkshopStore.Initialize(folder);
        Check(!WorkshopStore.Writable, "Malformed settings fail closed");
        Check(!WorkshopStore.Edit(position, c => c.Enabled = true, out _), "Corrupt settings cannot be replaced by a UI edit");
        Check(File.ReadAllText(path) == invalid, "Corrupt original remains recoverable");
    }
    File.WriteAllText(path, "{\"Format\":1,\"Worlds\":{}}");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Writable, "Restoring a valid file recovers on restart");
    for (int i = 0; i < 128; i++)
        Check(WorkshopStore.Edit(new Vector3i(i, 1, 1), c => { }, out _), "Controller fits supported limit");
    Check(!WorkshopStore.Edit(new Vector3i(128, 1, 1), c => { }, out _), "Controller limit is enforced");
    WorkshopStore.Initialize(folder);
    Check(WorkshopStore.Controllers.Count == 128 && WorkshopStore.Writable, "Maximum-size save round trips");
    Console.WriteLine($"PASS: {checks} workshop persistence assertions against production store code.");
}
finally
{
    // This directory was allocated for this test; never points into game data.
    temporary.Delete(recursive: true);
}

public enum EnumGamePrefs { GameWorld, GameName }
public static class GamePrefs
{
    public static string SaveName = "TestSave";
    public static string GetString(EnumGamePrefs name) => name == EnumGamePrefs.GameWorld ? "TestWorld" : SaveName;
}
public static class Log
{
    public static void Error(string format, params object[] arguments) { }
}
public struct Vector3i
{
    public int x, y, z;
    public Vector3i(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    public static bool operator ==(Vector3i left, Vector3i right) => left.x == right.x && left.y == right.y && left.z == right.z;
    public static bool operator !=(Vector3i left, Vector3i right) => !(left == right);
    public override bool Equals(object value) => value is Vector3i other && this == other;
    public override int GetHashCode() => HashCode.Combine(x, y, z);
}
