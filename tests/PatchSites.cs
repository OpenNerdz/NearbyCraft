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
foreach (var (type, method, targetType, targetMethod, expected) in checks) {
    var body = module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method).Body;
    int count = body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == targetType && m.Name == targetMethod);
    Console.WriteLine($"{(count == expected ? "PASS" : "FAIL")} {type}.{method} -> {targetType}.{targetMethod}: {count}, expected {expected}");
    if (count != expected) failed++;
}
foreach (var name in new[] { "HandleStackSwap", "HandlePartialStackPickup", "HandleMoveToPreferredLocation" }) {
    bool found = module.Types.Single(t => t.Name == "XUiC_ItemStack").Methods.Count(m => m.Name == name && m.Parameters.Count == 0 && m.HasBody) == 1;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Cursor hook: {name}");
    if (!found) failed++;
}
foreach (var name in new[] { "RefreshEnabled", "OnActivated", "OnDisabledActivate" }) {
    bool found = module.Types.Single(t => t.Name == "ItemActionEntryUse").Methods.Count(m => m.Name == name && m.Parameters.Count == 0 && m.HasBody) == 1;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Terminal use-action hook: {name}");
    if (!found) failed++;
}
var press = module.Types.Single(t => t.Name == "XUiController").Methods.Single(m => m.Name == "Pressed");
foreach (string eventField in new[] { "OnPress", "OnRightPress" }) {
    bool found = press.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == eventField);
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} Native click event dispatcher: {eventField}");
    if (!found) failed++;
}
Console.WriteLine($"Patch checks: {17 - failed}/17 passed (metadata only; not a runtime Harmony test)");
Environment.ExitCode = failed == 0 ? 0 : 1;
