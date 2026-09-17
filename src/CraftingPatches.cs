using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace NearbyCraft
{
    internal static class CraftingBridge
    {
        internal static List<ItemStack> AppendStorage(List<ItemStack> stacks)
        {
            return StorageIndex.AppendAvailableStacks(stacks);
        }

        internal static ItemStack[] AppendStorage(ItemStack[] stacks)
        {
            return StorageIndex.AppendAvailableStacks(stacks);
        }

        internal static bool HasItems(XUiM_PlayerInventory inventory, IList<ItemStack> stacks, int multiplier)
        {
            return StorageIndex.HasItems(inventory, stacks, multiplier);
        }

        internal static bool HasItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> stacks, int multiplier)
        {
            return StorageIndex.HasItems(grid, stacks, multiplier);
        }

        internal static void RemoveItems(XUiM_PlayerInventory inventory, IList<ItemStack> stacks, int multiplier, IList<ItemStack> removedItems)
        {
            StorageIndex.RemoveItems(inventory, stacks, multiplier, removedItems);
        }

        internal static void RemoveItems(XUiC_WorkstationInputGrid grid, IList<ItemStack> stacks, int multiplier, IList<ItemStack> removedItems)
        {
            StorageIndex.RemoveItems(grid, stacks, multiplier, removedItems);
        }

        internal static int AddIngredientCount(int existingCount, XUiC_IngredientEntry entry)
        {
            ItemStack ingredient = entry == null ? null : entry.Ingredient;
            if (ingredient == null)
            {
                return existingCount;
            }

            long total = (long)existingCount + StorageIndex.GetCount(ingredient.itemValue);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }
    }

    [HarmonyPatch(typeof(ItemActionEntryCraft), "hasItems")]
    internal static class CraftHasItemsPatch
    {
        private static readonly MethodInfo GetAllStacks = AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks));
        private static readonly MethodInfo PlayerHasItems = AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.HasItems));
        private static readonly MethodInfo WorkstationHasItems = AccessTools.Method(typeof(XUiC_WorkstationInputGrid), nameof(XUiC_WorkstationInputGrid.HasItems));
        private static readonly MethodInfo Append = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.AppendStorage), new[] { typeof(List<ItemStack>) });
        private static readonly MethodInfo PlayerHelper = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.HasItems), new[] { typeof(XUiM_PlayerInventory), typeof(IList<ItemStack>), typeof(int) });
        private static readonly MethodInfo WorkstationHelper = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.HasItems), new[] { typeof(XUiC_WorkstationInputGrid), typeof(IList<ItemStack>), typeof(int) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int appendCount = 0;
            int helperCount = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                if (Calls(codes[i], GetAllStacks))
                {
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, Append));
                    appendCount++;
                }
                else if (Calls(codes[i], PlayerHasItems))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = PlayerHelper;
                    helperCount++;
                }
                else if (Calls(codes[i], WorkstationHasItems))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = WorkstationHelper;
                    helperCount++;
                }
            }

            if (appendCount != 1 || helperCount != 2)
            {
                Log.Error("[NearbyCraft] V3.2 craft-availability patch did not match the expected game code ({0} stack hooks, {1} checks).", appendCount, helperCount);
            }
            return codes;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo target)
        {
            return target != null && Equals(instruction.operand, target);
        }
    }

    [HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.OnActivated))]
    internal static class CraftActivationPatch
    {
        private static readonly MethodInfo PlayerRemoveItems = AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.RemoveItems));
        private static readonly MethodInfo WorkstationRemoveItems = AccessTools.Method(typeof(XUiC_WorkstationInputGrid), nameof(XUiC_WorkstationInputGrid.RemoveItems));
        private static readonly MethodInfo PlayerHelper = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.RemoveItems), new[] { typeof(XUiM_PlayerInventory), typeof(IList<ItemStack>), typeof(int), typeof(IList<ItemStack>) });
        private static readonly MethodInfo WorkstationHelper = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.RemoveItems), new[] { typeof(XUiC_WorkstationInputGrid), typeof(IList<ItemStack>), typeof(int), typeof(IList<ItemStack>) });

        [HarmonyPrefix]
        private static void Prefix()
        {
            StorageIndex.ForceFreshForCraft();
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (PlayerRemoveItems != null && Equals(codes[i].operand, PlayerRemoveItems))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = PlayerHelper;
                    replacements++;
                }
                else if (WorkstationRemoveItems != null && Equals(codes[i].operand, WorkstationRemoveItems))
                {
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = WorkstationHelper;
                    replacements++;
                }
            }

            if (replacements != 2)
            {
                Log.Error("[NearbyCraft] V3.2 ingredient-removal patch expected 2 calls and found {0}.", replacements);
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(XUiC_RecipeCraftCount), "calcMaxCraftable")]
    internal static class MaximumCraftCountPatch
    {
        private static readonly MethodInfo GetAllStacks = AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks));
        private static readonly MethodInfo GetWorkstationSlots = AccessTools.Method(typeof(XUiC_ItemStackGrid), nameof(XUiC_ItemStackGrid.GetSlots));
        private static readonly MethodInfo AppendList = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.AppendStorage), new[] { typeof(List<ItemStack>) });
        private static readonly MethodInfo AppendArray = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.AppendStorage), new[] { typeof(ItemStack[]) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int hooks = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (GetAllStacks != null && Equals(codes[i].operand, GetAllStacks))
                {
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, AppendList));
                    hooks++;
                }
                else if (GetWorkstationSlots != null && Equals(codes[i].operand, GetWorkstationSlots))
                {
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, AppendArray));
                    hooks++;
                }
            }

            if (hooks != 2)
            {
                Log.Error("[NearbyCraft] V3.2 maximum-craft-count patch expected 2 branches and found {0}.", hooks);
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(XUiC_RecipeList), "BuildRecipeInfosList")]
    internal static class RecipeListPatch
    {
        [HarmonyPrefix]
        private static void Prefix(List<ItemStack> _items)
        {
            StorageIndex.AppendAvailableStacks(_items);
        }
    }

    [HarmonyPatch(typeof(XUiC_IngredientEntry), "GetBindingValueInternal")]
    internal static class IngredientDisplayPatch
    {
        private static readonly MethodInfo PlayerCount = AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetItemCount), new[] { typeof(ItemValue) });
        private static readonly MethodInfo WorkstationCount = AccessTools.Method(typeof(XUiC_WorkstationInputGrid), nameof(XUiC_WorkstationInputGrid.GetItemCount));
        private static readonly MethodInfo AddCount = AccessTools.Method(typeof(CraftingBridge), nameof(CraftingBridge.AddIngredientCount));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int hooks = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if ((PlayerCount != null && Equals(codes[i].operand, PlayerCount))
                    || (WorkstationCount != null && Equals(codes[i].operand, WorkstationCount)))
                {
                    codes.Insert(++i, new CodeInstruction(OpCodes.Ldarg_0));
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, AddCount));
                    hooks++;
                }
            }

            if (hooks != 6)
            {
                Log.Error("[NearbyCraft] V3.2 ingredient-display patch expected 6 count sites and found {0}.", hooks);
            }
            return codes;
        }
    }

    [HarmonyPatch(typeof(XUiC_RecipeTrackerIngredientEntry), nameof(XUiC_RecipeTrackerIngredientEntry.Ingredient), MethodType.Setter)]
    internal static class RecipeTrackerPatch
    {
        [HarmonyPostfix]
        private static void Postfix(XUiC_RecipeTrackerIngredientEntry __instance)
        {
            if (__instance == null || __instance.Ingredient == null || __instance.Owner == null)
            {
                return;
            }

            long available = (long)__instance.currentCount + StorageIndex.GetCount(__instance.Ingredient.itemValue);
            __instance.currentCount = available > int.MaxValue ? int.MaxValue : (int)available;
            long required = (long)__instance.Ingredient.count * __instance.Owner.Count;
            __instance.IsComplete = __instance.currentCount >= required;
            __instance.IsDirty = true;
        }
    }

    [HarmonyPatch(typeof(XUiC_CraftingInfoWindow), nameof(XUiC_CraftingInfoWindow.Init))]
    internal static class CraftingToggleInitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(XUiC_CraftingInfoWindow __instance)
        {
            XUiController toggle = __instance == null ? null : __instance.GetChildById("nearbyCraftToggle");
            if (toggle == null)
            {
                Log.Warning("[NearbyCraft] The crafting-screen toggle was not found. Check the XUi XML patch.");
                return;
            }

            toggle.OnPress -= TogglePressed;
            toggle.OnPress += TogglePressed;
        }

        private static void TogglePressed(XUiController sender, int mouseButton)
        {
            bool enabled = !NearbyCraftMod.Config.Enabled;
            NearbyCraftMod.SetEnabled(enabled);

            XUiWindowGroup group = sender == null ? null : sender.windowGroup;
            XUiController root = group == null ? null : group.Controller;
            if (root != null)
            {
                XUiC_RecipeList recipeList = root.GetChildByType<XUiC_RecipeList>();
                if (recipeList != null)
                {
                    recipeList.resortRecipes = true;
                    recipeList.pageChanged = true;
                    recipeList.IsDirty = true;
                }

                XUiC_CraftingInfoWindow info = root.GetChildByType<XUiC_CraftingInfoWindow>();
                if (info != null)
                {
                    info.IsDirty = true;
                }

                root.SetAllChildrenDirty();
            }

            EntityPlayerLocal player = sender == null || sender.xui == null || sender.xui.playerUI == null
                ? null
                : sender.xui.playerUI.entityPlayer;
            if (player != null)
            {
                GameManager.ShowTooltip(player, enabled ? "Nearby crafting enabled" : "Nearby crafting disabled");
            }
        }
    }

    [HarmonyPatch(typeof(XUiC_CraftingInfoWindow), "GetBindingValueInternal")]
    internal static class CraftingToggleBindingsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref string value, string bindingName, ref bool __result)
        {
            bool enabled = NearbyCraftMod.Config != null && NearbyCraftMod.Config.Enabled;
            switch (bindingName)
            {
                case "nearbycraft_enabled":
                    value = enabled.ToString();
                    __result = true;
                    break;
                case "nearbycraft_status":
                    value = enabled ? "NEARBY: ON" : "NEARBY: OFF";
                    __result = true;
                    break;
                case "nearbycraft_status_color":
                    value = enabled ? "70,190,90,255" : "125,125,125,255";
                    __result = true;
                    break;
                case "nearbycraft_tooltip":
                    value = enabled
                        ? "Nearby storage is enabled. Click to use only your normal crafting inventory."
                        : "Nearby storage is disabled. Click to include accessible storage within range.";
                    __result = true;
                    break;
            }
        }
    }
}
