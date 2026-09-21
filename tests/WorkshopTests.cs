using NearbyCraft;

internal static class WorkshopTests
{
    internal static void Run(Action<bool, string> check)
    {
        ItemStack Stack(int type, int count) => new ItemStack(new ItemValue { type = type }, count);
        StorageTransferPlan Plan(ItemStack[] items, params bool[] locks)
        {
            var plan = new StorageTransferPlan();
            plan.Add(items, locks.Length == 0 ? new bool[items.Length] : locks);
            return plan;
        }

        check(WorkshopRules.BatchesNeeded(500, 300, 40, 160, 1) == 0, "Workshop counts stored + output + native queue toward target");
        check(WorkshopRules.BatchesNeeded(20, 19, 0, 0, 2) == 0, "Workshop never overshoots a target with a multi-output recipe");
        check(WorkshopRules.BatchesNeeded(500, 300, 0, 0, 1) == 10, "Workshop bounds each queue batch");
        check(WorkshopRules.BatchesNeeded(5, 0, 0, 0, 0) == 0, "Malformed recipe cannot divide by zero");
        check(WorkshopRules.BatchesNeeded(500, long.MaxValue, 0, 0, 1) == 0, "Huge stock count cannot wrap into a craft request");
        check(WorkshopRules.OrderBatches(1, 2) == 1, "One-shot orders round to one complete recipe yield");
        check(WorkshopRules.OrderBatches(0, 2) == 0 && WorkshopRules.OrderBatches(1, 0) == 0, "Completed and invalid orders cannot schedule");
        check(WorkshopRules.OrderBatches(int.MaxValue, 1) == 10, "Large orders are bounded without overflow");
        check(WorkshopRules.ShareBatches(24, 1, 3, true) == 8, "Demand splits evenly across spare machines");
        check(WorkshopRules.ShareBatches(1, 2, 3, true) == 1 && WorkshopRules.ShareBatches(1, 2, 3, false) == 0,
            "Whole-yield rounding only applies to one-time orders");
        check(WorkshopRules.ShareBatches(long.MaxValue, 2, 24, true) == 10, "Huge demand cannot overflow batch sharing");
        check(WorkshopRules.ShareBatches(1, 0, 3, true) == 0 && WorkshopRules.ShareBatches(10, 1, 0, true) == 0,
            "Invalid yield or missing machines cannot allocate");
        check(WorkshopRules.FeedCount(50, 10, 20, 5, 100) == 4, "Forge counts already smelted and currently smelting units");
        check(WorkshopRules.FeedCount(51, 10, 20, 5, 100) == 5, "Forge rounds raw feed to whole weighted items");
        check(WorkshopRules.FeedCount(51, 10, 20, 5, 2) == 2, "Forge internal material capacity bounds feeding");
        check(WorkshopRules.FeedCount(5, 10, 20, 5, 100) == 0 && WorkshopRules.FeedCount(50, 0, 0, 0, 100) == 0, "No excess or zero-weight forge feeding");
        check(WorkshopRules.FuelCount(10000, 0, 10, 100) == 12, "Fuel buffer never loads the entire long queue");
        check(WorkshopRules.FuelCount(15, 10, 10, 100) == 1 && WorkshopRules.FuelCount(15, 20, 10, 100) == 0, "Fuel includes burning and inventory time");
        check(WorkshopRules.FuelCount(15, 0, 0, 100) == 0 && WorkshopRules.FuelCount(float.NaN, 0, 10, 100) == 0, "Invalid fuel cannot divide or overflow");

        var forge = new[] { Stack(1, 20), ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), Stack(2, 30), Stack(3, 10) };
        var forgeAfter = ItemStack.Clone(forge);
        ItemStack materialMissing;
        check(WorkshopPlanner.ConsumeMaterials(forgeAfter, 3, new[] { Stack(2, 3), Stack(3, 1) }, 10, out materialMissing), "Forge pays from real smelted-unit slots");
        check(forgeAfter[3].count == 0 && forgeAfter[3].itemValue.type == 2 && forgeAfter[4].itemValue.type == 3, "Zero material slots retain vanilla unit identities");
        check(forgeAfter[0].count == 20 && forge[3].count == 30, "Forge planning leaves raw slots and live input unchanged");
        forgeAfter = ItemStack.Clone(forge);
        check(!WorkshopPlanner.ConsumeMaterials(forgeAfter, 3, new[] { Stack(1, 1) }, 1, out materialMissing), "Raw inputs are not spendable until smelted");
        forgeAfter = ItemStack.Clone(forge);
        check(!WorkshopPlanner.ConsumeMaterials(forgeAfter, 3, new[] { Stack(2, 20), Stack(2, 20) }, 1, out materialMissing)
            && materialMissing.count == 10 && forge[3].count == 30, "Duplicate ingredients cannot double spend smelted units");

        var supplyChest = new[] { Stack(1, 100), Stack(2, 20) };
        var inputs = new[] { Stack(1, 98), ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), Stack(2, 20) };
        var feed = Plan(supplyChest);
        check(WorkshopPlanner.Supply(feed, inputs, 3, Stack(1, 1), 8) == 8 && inputs[0].count == 100 && inputs[1].count == 6,
            "Input supply fills matching stacks before empty slots");
        check(inputs[3].count == 20 && feed.TryCommit(_ => true) && supplyChest[0].count == 92, "Input supply respects raw-slot boundary and conserves items");
        feed = Plan(supplyChest, true, false);
        check(WorkshopPlanner.Supply(feed, inputs, 3, Stack(1, 1), 10) == 0, "Automatic fuel/input cannot withdraw locked supplies");

        // Running out of the final ingredient must leave ALL live chests untouched.
        var chest = new[] { Stack(1, 30), Stack(2, 2), ItemStack.Empty.Clone() };
        var transaction = Plan(chest);
        ItemStack missing;
        var recipe = new[] { Stack(1, 3), Stack(2, 1) };
        check(!WorkshopPlanner.Consume(transaction, recipe, 5, out missing) && missing.itemValue.type == 2,
            "Workshop reports the missing ingredient after staging earlier ingredients");
        check(chest[0].count == 30 && chest[1].count == 2, "Failed workshop consumption is side-effect-free");

        transaction = Plan(chest, true, false, false);
        check(!WorkshopPlanner.Consume(transaction, recipe, 1, out missing), "Workshop cannot consume a locked ingredient slot");
        check(chest[0].count == 30, "Locked ingredient was retained");

        // Capacity checks account for all previously queued output, but never mint it.
        chest = new[] { Stack(1, 10), Stack(3, 95) };
        transaction = Plan(chest);
        var capacity = Plan(chest);
        recipe = new[] { Stack(1, 1) };
        check(WorkshopPlanner.Consume(transaction, recipe, 2, out missing), "Real consumption is staged");
        check(WorkshopPlanner.Consume(capacity, recipe, 2, out missing), "Capacity projection stages the same costs");
        check(!WorkshopPlanner.Reserve(capacity, new[] { Stack(3, 4) }, Stack(3, 2)), "Existing queued output reserves the final free space");
        check(chest[0].count == 10 && chest[1].count == 95, "Rejecting full storage consumes nothing and creates nothing");
        capacity = Plan(chest);
        WorkshopPlanner.Consume(capacity, recipe, 2, out missing);
        check(WorkshopPlanner.Reserve(capacity, new[] { Stack(3, 3) }, Stack(3, 2)), "Exactly fitting output is accepted");
        check(transaction.TryCommit(_ => true), "Only the consumption plan commits");
        check(chest[0].count == 8 && chest[1].count == 95, "Queued products are not present before vanilla crafts them");

        chest = new[] { Stack(1, 2), Stack(3, 100) };
        capacity = Plan(chest);
        WorkshopPlanner.Consume(capacity, recipe, 2, out missing);
        check(WorkshopPlanner.Reserve(capacity, Array.Empty<ItemStack>(), Stack(4, 2)), "Consumed input can free output capacity");
        check(chest[0].count == 2, "Capacity-only planning never commits consumption");

        // Collection must transfer only what fits and retain every output remainder.
        chest = new[] { Stack(3, 95) };
        var output = new[] { Stack(3, 12), Stack(4, 9) };
        var after = ItemStack.Clone(output);
        transaction = Plan(chest);
        check(WorkshopPlanner.Collect(transaction, after) == 5, "Partial workshop collection is exact");
        check(after[0].count == 7 && after[1].count == 9 && output[0].count == 12, "Planning preserves the real output slots");
        check(transaction.TryCommit(_ => true) && chest[0].count == 100, "Collection fills only the available chest space");

        chest = new[] { ItemStack.Empty.Clone() };
        output = new[] { Stack(3, 12) };
        after = ItemStack.Clone(output);
        transaction = Plan(chest);
        WorkshopPlanner.Collect(transaction, after);
        check(!transaction.TryCommit(_ => false), "Vanished or in-use source rejects collection before any writes");
        check(chest[0].IsEmpty() && output[0].count == 12, "Aborted collection conserves both sides");

        // Completed goods must keep their metadata; merging unrelated variants loses it.
        chest = new[] { Stack(3, 99), ItemStack.Empty.Clone() };
        output = new[] { Stack(3, 5) };
        output[0].itemValue.Metadata = 123;
        after = ItemStack.Clone(output);
        transaction = Plan(chest);
        check(WorkshopPlanner.Collect(transaction, after) == 5 && transaction.TryCommit(_ => true), "Variant output can be collected");
        check(chest[0].count == 99 && chest[1].itemValue.Metadata == 123, "Workshop output metadata survives intact");

        var random = new Random(150025157);
        for (int i = 0; i < 2000; i++)
        {
            int target = random.Next(1, 100001), stored = random.Next(0, 100001);
            int waiting = random.Next(0, 5000), queued = random.Next(0, 5000), yield = random.Next(1, 1001);
            int batches = WorkshopRules.BatchesNeeded(target, stored, waiting, queued, yield);
            check(batches >= 0 && batches <= 10 && (batches == 0 || (long)stored + waiting + queued + batches * yield <= target),
                "Randomized workshop target cannot overproduce");
            chest = new[] { Stack(3, random.Next(1, 101)), ItemStack.Empty.Clone() };
            output = new[] { Stack(3, random.Next(1, 301)), Stack(4, random.Next(1, 301)) };
            long beforeTotal = chest.Sum(s => (long)s.count) + output.Sum(s => (long)s.count);
            after = ItemStack.Clone(output);
            transaction = Plan(chest);
            WorkshopPlanner.Collect(transaction, after);
            check(transaction.TryCommit(_ => true), "Randomized collection commits");
            check(beforeTotal == chest.Sum(s => (long)s.count) + after.Sum(s => (long)s.count), "Randomized workshop collection conserves every item");

            int units = random.Next(0, 1000), raw = random.Next(0, 300), weight = random.Next(1, 20), room = random.Next(0, 300);
            int feedCount = WorkshopRules.FeedCount(target, units, (long)raw * weight, weight, room);
            check(feedCount >= 0 && feedCount <= room && (feedCount == 0 || units + (long)raw * weight + (long)(feedCount - 1) * weight < target),
                "Randomized forge feed cannot oversupply beyond one raw item's yield");
            var supply = new[] { Stack(1, random.Next(0, 201)), Stack(2, random.Next(0, 101)) };
            var dest = new[] { Stack(1, random.Next(0, 101)), ItemStack.Empty.Clone(), Stack(2, 1000) };
            long supplyBefore = supply.Sum(s => (long)s.count) + dest.Sum(s => (long)s.count);
            var supplyPlan = Plan(supply);
            WorkshopPlanner.Supply(supplyPlan, dest, 2, Stack(1, 1), random.Next(0, 301));
            check(supplyPlan.TryCommit(_ => true) && supplyBefore == supply.Sum(s => (long)s.count) + dest.Sum(s => (long)s.count)
                && dest[2].count == 1000 && dest.Take(2).All(s => s.count <= 100), "Randomized input supply conserves every item and respects slots/stack caps");
        }
    }
}
