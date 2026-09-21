using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    // One scheduler per network tick. Native queues and inventories remain the
    // source of truth; persisted leases describe only which forge is preparing
    // which future batch. They are included in capacity/demand reservations.
    internal sealed class WorkshopScheduler
    {
        private readonly StorageNetworkSession network;
        private readonly EntityPlayerLocal player;
        private readonly XUi xui;
        private readonly WorkshopControllerData controller;
        private readonly List<TileEntityWorkstation> stations;
        private readonly Dictionary<string, ILookup<int, Recipe>> recipes = new Dictionary<string, ILookup<int, Recipe>>();
        private readonly Dictionary<string, string> reasons = new Dictionary<string, string>();
        private int actions;

        private sealed class Candidate
        {
            internal TileEntityWorkstation Station;
            internal Recipe Recipe;
            internal int Ready, Covered;
            internal double Fit;
            internal double[] Finish = new double[WorkshopRules.BatchLimit + 1];
            internal int Assigned;
        }

        internal WorkshopScheduler(StorageNetworkSession network, EntityPlayerLocal player, XUi xui,
            WorkshopControllerData controller, List<TileEntityWorkstation> stations)
        { this.network = network; this.player = player; this.xui = xui; this.controller = controller; this.stations = stations; }

        internal void Run()
        {
            ServiceAssignments();
            // Every order gets a first opportunity before an earlier large
            // order can occupy the spare machines in the second pass.
            for (int pass = 0; pass < 2 && WorkshopStore.Writable; pass++)
                foreach (var target in controller.Targets)
                {
                    if (!target.Enabled || !WorkshopStore.Writable) continue;
                    long demand = Demand(target);
                    if (demand <= 0) continue;
                    actions = pass == 0 ? 1 : WorkshopRules.MaximumStations;
                    string reason;
                    Ensure(target, ItemClass.GetItem(target.Item, false).type, demand, target.Once,
                        new HashSet<int>(), out reason);
                    if (!string.IsNullOrEmpty(reason)) reasons[target.Item] = reason;
                }
            bool completed = false;
            foreach (var target in controller.Targets)
            {
                if (target.Once && target.TrackDelivery && target.CompletedUtcTicks == 0 && target.Remaining == 0
                    && target.Queued > 0 && target.Returned >= target.Queued
                    && !controller.Smelting.Any(j => j.Owner == target.Item))
                {
                    var item = ItemClass.GetItem(target.Item, false);
                    if (item != null && !item.IsEmpty() && Pending(item.type) == 0)
                    { WorkshopStore.RecordCompletion(controller, target, true); completed = true; }
                }
                Describe(target);
            }
            if (completed) Save();
        }

        private long Demand(WorkshopTarget target)
        {
            var item = ItemClass.GetItem(target.Item, false);
            if (item == null || item.IsEmpty()) return 0;
            return target.Once ? target.Remaining : target.Target - network.CountProduct(item.type) - Pending(item.type);
        }

        private long Pending(int type)
        { return stations.Sum(s => WorkshopManager.CountOutput(s, type) + WorkshopManager.CountQueued(s, type)); }

        private bool Save()
        {
            string error;
            if (WorkshopStore.SaveProgress(out error)) return true;
            WorkshopManager.Status[controller.Position] = error;
            actions = 0;
            return false;
        }

        private static string RecipeKey(Recipe recipe)
        {
            return recipe.GetName() + "/" + recipe.craftingArea + "/" + recipe.count + "/" + recipe.craftingToolType + "/"
                + string.Join(";", recipe.ingredients.Select(i => i.itemValue.ItemClass.GetItemName() + ":" + i.count));
        }

        private IEnumerable<Recipe> Recipes(TileEntityWorkstation station, int type)
        {
            string name = station.block.GetBlockName();
            ILookup<int, Recipe> list;
            if (!recipes.TryGetValue(name, out list))
                recipes[name] = list = XUiM_Recipes.FilterRecipesByWorkstation(name, XUiM_Recipes.GetRecipes())
                    .Where(WorkshopManager.Supported).ToLookup(r => r.itemValueType);
            return list[type];
        }

        private bool Usable(TileEntityWorkstation station, Recipe recipe, out string reason)
        {
            reason = "";
            if (!recipe.IsUnlocked(player)) { reason = "Unlock this recipe first"; return false; }
            if (recipe.craftingToolType != 0 && !station.Tools.Any(t => t != null && !t.IsEmpty() && t.itemValue.type == recipe.craftingToolType))
            { reason = "Install " + Localization.Get(ItemClass.GetForId(recipe.craftingToolType).GetItemName()); return false; }
            if (station.IsBesideWater) { reason = "Move the machine away from water"; return false; }
            return true;
        }

        private void ServiceAssignments()
        {
            foreach (var job in controller.Smelting.ToArray())
            {
                if (!WorkshopStore.Writable) return;
                var owner = controller.Targets.Find(t => t.Item == job.Owner);
                var station = stations.Find(s => s.ToWorldPos() == job.Position);
                var item = ItemClass.GetItem(job.Item, false);
                if (owner == null || !owner.Enabled || Demand(owner) <= 0 || station == null || item == null || item.IsEmpty()
                    || (job.Item != job.Owner && !controller.AutoCraft) || station.hasRecipeInQueue())
                { controller.Smelting.Remove(job); if (!Save()) return; continue; }
                string reason = "Recipe changed; assigning again";
                var recipe = Recipes(station, item.type).Where(r => Usable(station, r, out reason))
                    .Select(r => WorkshopManager.PrepareRecipe(r, xui, station)).FirstOrDefault(r => r.materialBasedRecipe && RecipeKey(r) == job.RecipeKey);
                // Never reinterpret an old allocation as a different recipe.
                if (recipe == null || checked(recipe.count * job.Batches) != job.Count)
                { controller.Smelting.Remove(job); if (!Save()) return; reasons[job.Owner] = reason; continue; }
                if (job.Item == job.Owner)
                {
                    int cap = owner.Once ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, Demand(owner)), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, Demand(owner)), 0, 0, 0, recipe.count);
                    if (cap < job.Batches)
                    {
                        job.Batches = cap; job.Count = cap * recipe.count;
                        if (cap == 0) controller.Smelting.Remove(job);
                        if (!Save()) return;
                        if (cap == 0) continue;
                    }
                }
                ItemStack missing;
                if (network.QueueBatch(station, Batch(recipe, job.Batches), ReservedOutputs(stations, controller, job), controller.AutoFuel, out missing, out reason))
                {
                    controller.Smelting.Remove(job);
                    if (owner.Once && job.Item == owner.Item)
                    { owner.Remaining = Math.Max(0, owner.Remaining - job.Count); owner.Queued = checked(owner.Queued + job.Count); }
                    if (!Save()) return;
                    reasons[job.Owner] = "Crafting " + Localization.Get(job.Item);
                    WorkshopManager.StationStatus[job.Position] = reasons[job.Owner];
                    continue;
                }
                if (missing != null && WorkshopMachines.MaterialBatches(station, recipe, true) < job.Batches)
                    network.FeedForge(station, recipe, job.Batches, controller.AutoFuel, out reason);
                else if (missing != null) reason = "Smelting for " + Localization.Get(job.Item) + " x" + job.Count;
                WorkshopManager.StationStatus[job.Position] = reason;
                reasons[job.Owner] = reason;
            }
        }

        private bool Ensure(WorkshopTarget owner, int type, long deficit, bool roundUp, HashSet<int> path, out string reason)
        {
            reason = "Waiting for a free machine";
            if (actions <= 0 || !WorkshopStore.Writable) return false;
            if (path.Count >= 8 || !path.Add(type)) { reason = "Recipe dependency cycle or depth limit"; return false; }
            try
            {
                bool root = type == ItemClass.GetItem(owner.Item, false).type;
                long leased = controller.Smelting.Where(j => ItemClass.GetItem(j.Item, false).type == type
                    && (!root || j.Owner == owner.Item)).Sum(j => (long)j.Count);
                long need = deficit - leased;
                if (need <= 0) { reason = "Smelting assigned materials"; return true; }
                var candidates = new List<Candidate>();
                var dependencyRecipes = new List<Recipe>();
                bool matching = false;
                foreach (var station in stations)
                {
                    if (station.IsUserAccessing()) continue;
                    foreach (var recipe in Recipes(station, type))
                    {
                        matching = true;
                        if (!Usable(station, recipe, out reason)) continue;
                        var staged = WorkshopManager.PrepareRecipe(recipe, xui, station);
                        if (!staged.materialBasedRecipe && !dependencyRecipes.Any(r => RecipeKey(r) == RecipeKey(staged))) dependencyRecipes.Add(staged);
                        if (station.hasRecipeInQueue() || controller.Smelting.Any(j => j.Position == station.ToWorldPos())) continue;
                        var candidate = new Candidate { Station = station, Recipe = staged,
                            Ready = staged.materialBasedRecipe ? WorkshopMachines.MaterialBatches(station, staged, false) : 0,
                            Covered = staged.materialBasedRecipe ? WorkshopMachines.MaterialBatches(station, staged, true) : 0,
                            Fit = staged.materialBasedRecipe ? WorkshopMachines.MaterialFit(station, staged) : 0 };
                        for (int amount = 1; amount <= WorkshopRules.BatchLimit; amount++)
                            candidate.Finish[amount] = Estimate(station, staged, amount);
                        candidates.Add(candidate);
                    }
                }
                if (!matching) reason = "Supply " + need + " " + Localization.Get(ItemClass.GetForId(type).GetItemName()) + " or add its crafting machine";
                // Estimate native completion times; material affinity breaks ties.
                candidates = candidates.Where(c => roundUp || c.Recipe.count <= need)
                    .OrderBy(c => c.Finish[1] / Math.Min(need, Math.Max(1, c.Recipe.count)))
                    .ThenByDescending(c => c.Ready > 0).ThenByDescending(c => c.Covered > 0)
                    .ThenByDescending(c => c.Fit).ThenBy(c => WorkshopMachines.HasSmeltingInput(c.Station) ? 1 : 0)
                    .GroupBy(c => c.Station).Select(g => g.First()).ToList();
                long toAssign = need;
                for (int cycle = 0; cycle < WorkshopRules.BatchLimit * candidates.Count && toAssign > 0; cycle++)
                {
                    var next = candidates.Where(c => c.Assigned < WorkshopRules.BatchLimit
                        && (roundUp || c.Recipe.count <= toAssign))
                        .OrderBy(c => c.Finish[c.Assigned + 1]).FirstOrDefault();
                    if (next == null || double.IsInfinity(next.Finish[next.Assigned + 1])) break;
                    next.Assigned++; toAssign -= next.Recipe.count;
                }
                // Retain unavailable candidates for precise transactional errors.
                candidates = candidates.OrderBy(c => c.Assigned > 0 ? 0 : 1)
                    .ThenBy(c => c.Finish[Math.Max(1, c.Assigned)]).ToList();
                bool progress = leased > 0;
                for (int index = 0; index < candidates.Count && need > 0 && actions > 0 && WorkshopStore.Writable; index++)
                {
                    var candidate = candidates[index]; var station = candidate.Station; var recipe = candidate.Recipe;
                    // Earlier dependency work may have taken this station.
                    if (station.hasRecipeInQueue() || controller.Smelting.Any(j => j.Position == station.ToWorldPos())) continue;
                    int max = roundUp ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, need), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, need), 0, 0, 0, recipe.count);
                    int share = candidate.Assigned > 0 ? Math.Min(max, candidate.Assigned)
                        : double.IsInfinity(candidate.Finish[1]) ? Math.Min(max, 1) : 0;
                    if (share == 0) { reason = "Less than one recipe yield needed"; continue; }
                    ItemStack missing = null;
                    bool queued = false;
                    for (int amount = share; amount >= 1; amount--)
                    {
                        if (!network.QueueBatch(station, Batch(recipe, amount), ReservedOutputs(stations, controller), controller.AutoFuel, out missing, out reason)) continue;
                        int count = amount * recipe.count;
                        if (root && owner.Once) { owner.Remaining = Math.Max(0, owner.Remaining - count); owner.Queued = checked(owner.Queued + count); if (!Save()) return true; }
                        need -= count; actions--; progress = queued = true;
                        WorkshopManager.StationStatus[station.ToWorldPos()] = "Crafting " + Localization.Get(recipe.GetName()) + " x" + count;
                        reason = WorkshopManager.StationStatus[station.ToWorldPos()];
                        break;
                    }
                    if (queued || missing == null) continue;
                    if (recipe.materialBasedRecipe)
                    {
                        for (int amount = share; amount >= 1; amount--)
                        {
                            int count = checked(recipe.count * amount);
                            if (!network.CanReserveProducts(ReservedOutputs(stations, controller), new ItemStack(new ItemValue(type), count)))
                            { reason = "Make room in connected storage"; continue; }
                            bool covered = WorkshopMachines.MaterialBatches(station, recipe, true) >= amount;
                            if (!covered && !network.FeedForge(station, recipe, amount, controller.AutoFuel, out reason)) continue;
                            var p = station.ToWorldPos();
                            controller.Smelting.Add(new WorkshopSmeltAssignment { X = p.x, Y = p.y, Z = p.z,
                                Item = recipe.GetName(), Owner = owner.Item, Batches = amount, Count = count, RecipeKey = RecipeKey(recipe) });
                            if (!Save()) return true;
                            need -= count; actions--; progress = true;
                            reason = "Smelting for " + Localization.Get(recipe.GetName()) + " x" + count;
                            WorkshopManager.StationStatus[p] = reason;
                            break;
                        }
                        continue;
                    }
                }
                // A busy parent machine must not hide the recipe's other inputs.
                // A single mixer making sand can still ask spare forges for cement.
                if (need > 0 && actions > 0 && controller.AutoCraft && WorkshopStore.Writable)
                foreach (var recipe in dependencyRecipes.OrderBy(r => r.craftingTime / Math.Max(1, r.count)).Take(1))
                {
                    int batches = roundUp ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, need), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, need), 0, 0, 0, recipe.count);
                    string blocked = null;
                    foreach (var ingredient in recipe.ingredients)
                    {
                        long inputNeed = (long)ingredient.count * batches - network.CountProduct(ingredient.itemValue.type) - Pending(ingredient.itemValue.type);
                        if (inputNeed <= 0) continue;
                        string dependency;
                        if (Ensure(owner, ingredient.itemValue.type, inputNeed, true, path, out dependency)) progress = true;
                        else if (blocked == null) blocked = dependency;
                        reason = "Preparing " + Localization.Get(ingredient.itemValue.ItemClass.GetItemName()) + " / " + dependency;
                    }
                    if (blocked != null && !progress) reason = blocked;
                }
                return progress;
            }
            finally { path.Remove(type); }
        }

        private double Estimate(TileEntityWorkstation station, Recipe recipe, int amount)
        {
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel))
            {
                var wood = ItemClass.GetItem("resourceWood", false);
                if (!controller.AutoFuel && !station.IsBurning) return double.PositiveInfinity;
                if (station.BurnTotalTimeLeft <= 0 && (!controller.AutoFuel || wood == null || wood.IsEmpty()
                    || network.CountProduct(wood.type) == 0)) return double.PositiveInfinity;
            }
            var output = new StorageTransferPlan();
            output.Add(station.Output, new bool[station.Output.Length]);
            int count = checked(recipe.count * amount);
            if (output.Deposit(new ItemStack(new ItemValue(recipe.itemValueType), count)) != count) return double.PositiveInfinity;
            double seconds = Math.Max(.05, recipe.craftingTime) * amount;
            if (!recipe.materialBasedRecipe)
                return recipe.ingredients.All(i => network.CountProduct(i.itemValue.type) >= (long)i.count * amount)
                    ? seconds : double.PositiveInfinity;
            WorkshopForgePlan plan; string reason;
            if (!WorkshopForgePlan.Create(station, recipe, amount, out plan, out reason)
                || plan.Supplies.Any(s => network.CountProduct(s.itemValue.type) < s.count)) return double.PositiveInfinity;
            // Ignore sub-millisecond binary-search noise when breaking ties.
            return Math.Round(seconds + plan.Seconds, 3);
        }

        private static RecipeQueueItem Batch(Recipe recipe, int amount)
        {
            return new RecipeQueueItem { Recipe = recipe, Multiplier = (short)amount, OneItemCraftTime = recipe.craftingTime,
                CraftingTimeLeft = recipe.craftingTime, IsCrafting = true, Quality = 0,
                StartingEntityId = GameManager.Instance.World.GetPrimaryPlayer().entityId };
        }

        internal static List<ItemStack> ReservedOutputs(List<TileEntityWorkstation> stations,
            WorkshopControllerData controller, WorkshopSmeltAssignment except = null)
        {
            var result = WorkshopManager.OutstandingOutputs(stations);
            foreach (var job in controller.Smelting)
            {
                if (ReferenceEquals(job, except)) continue;
                var item = ItemClass.GetItem(job.Item, false);
                if (item != null && !item.IsEmpty()) result.Add(new ItemStack(item, job.Count));
            }
            return result;
        }

        private void Describe(WorkshopTarget target)
        {
            string key = WorkshopManager.TargetKey(controller.Position, target.Item);
            var item = ItemClass.GetItem(target.Item, false);
            if (item == null || item.IsEmpty()) { WorkshopManager.TargetStatus[key] = "Item no longer exists"; return; }
            long pending = Pending(item.type);
            string state;
            if (!target.Enabled) state = "Paused";
            else if (target.Once && target.Remaining == 0) state = pending > 0 ? "Crafting / " + pending + " still in machines" : "Done / returned to storage";
            else if (!target.Once && Demand(target) <= 0) state = pending > 0 ? "Crafting / " + pending + " still in machines" : "In stock / automatic top-up ready";
            else if (!reasons.TryGetValue(target.Item, out state)) state = "Waiting for a free machine";
            var preparing = controller.Smelting.Where(j => j.Owner == target.Item).ToList();
            if (preparing.Count > 0) state = "Smelting / " + preparing.Count + " forge" + (preparing.Count == 1 ? "" : "s") + " preparing "
                + string.Join(", ", preparing.Select(j => Localization.Get(j.Item)).Distinct());
            WorkshopManager.TargetStatus[key] = state;
        }
    }
}
