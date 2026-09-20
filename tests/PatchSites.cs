using System;
using System.Linq;
using Mono.Cecil;

string game = args.Length == 0 ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.local/share/Steam/steamapps/common/7 Days To Die" : args[0];
using var module = ModuleDefinition.ReadModule(game + "/7DaysToDie_Data/Managed/Assembly-CSharp.dll");
var checks = new[] {
    ("ItemActionEntryCraft", "hasItems", "XUiM_PlayerInventory", "GetAllItemStacks", 1),
    ("ItemActionEntryCraft", "hasItems", "XUiM_PlayerInventory", "HasItems", 1),
    ("ItemActionEntryCraft", "hasItems", "XUiC_WorkstationInputGrid", "HasItems", 1),
    ("ItemActionEntryCraft", "OnActivated", "XUiM_PlayerInventory", "RemoveItems", 1),
    ("ItemActionEntryCraft", "OnActivated", "XUiC_WorkstationInputGrid", "RemoveItems", 1),
    ("XUiC_RecipeCraftCount", "calcMaxCraftable", "XUiM_PlayerInventory", "GetAllItemStacks", 1),
    ("XUiC_RecipeCraftCount", "calcMaxCraftable", "XUiC_ItemStackGrid", "GetSlots", 1),
    ("XUiC_IngredientEntry", "GetBindingValueInternal", "XUiM_PlayerInventory", "GetItemCount", 4),
    ("XUiC_IngredientEntry", "GetBindingValueInternal", "XUiC_WorkstationInputGrid", "GetItemCount", 2),
};
int failed = 0;
int total = 0;
foreach (var (type, method, targetType, targetMethod, expected) in checks) {
    var body = module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method).Body;
    int count = body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == targetType && m.Name == targetMethod);
    Console.WriteLine($"{(count == expected ? "PASS" : "FAIL")} {type}.{method} -> {targetType}.{targetMethod}: {count}, expected {expected}");
    total++;
    if (count != expected) failed++;
}
foreach (var name in new[] { "HandleStackSwap", "HandlePartialStackPickup", "HandleMoveToPreferredLocation" }) {
    bool found = module.Types.Single(t => t.Name == "XUiC_ItemStack").Methods.Count(m => m.Name == name && m.Parameters.Count == 0 && m.HasBody) == 1;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Cursor hook: {name}");
    total++;
    if (!found) failed++;
}
foreach (var name in new[] { "RefreshEnabled", "OnActivated", "OnDisabledActivate" }) {
    bool found = module.Types.Single(t => t.Name == "ItemActionEntryUse").Methods.Count(m => m.Name == name && m.Parameters.Count == 0 && m.HasBody) == 1;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Terminal use-action hook: {name}");
    total++;
    if (!found) failed++;
}
var press = module.Types.Single(t => t.Name == "XUiController").Methods.Single(m => m.Name == "Pressed");
foreach (string eventField in new[] { "OnPress", "OnRightPress" }) {
    bool found = press.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == eventField);
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Native click event dispatcher: {eventField}");
    total++;
    if (!found) failed++;
}

var requiredLoadoutMethods = new[] {
    ("XUiM_PlayerInventory", "GetBackpackItemStacks", 0),
    ("XUiM_PlayerInventory", "GetToolbeltItemStacks", 0),
    ("XUiM_PlayerInventory", "SetBackpackItemStacks", 1),
    ("XUiM_PlayerInventory", "SetToolbeltItemStacks", 1),
    ("Inventory", "CanMoveToSlot", 2),
    ("Equipment", "GetSlotCount", 0),
    ("Equipment", "GetSlotItem", 1),
    ("Equipment", "SetSlotItemRaw", 2),
    ("Equipment", "Clone", 0),
    ("Equipment", "Apply", 2),
    ("ItemStack", "Write", 1),
    ("ItemStack", "Read", 1),
    ("GamePrefs", "GetString", 1),
};
foreach (var (typeName, methodName, parameterCount) in requiredLoadoutMethods) {
    bool found = module.GetTypes().Where(t => t.Name == typeName)
        .Any(t => t.Methods.Any(m => m.Name == methodName && m.Parameters.Count == parameterCount));
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Loadout API: {typeName}.{methodName}/{parameterCount}");
    total++;
    if (!found) failed++;
}
Console.WriteLine($"Patch checks: {total - failed}/{total} passed (metadata only; not a runtime Harmony test)");
Environment.ExitCode = failed == 0 ? 0 : 1;
