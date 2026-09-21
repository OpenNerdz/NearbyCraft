using System;

namespace NearbyCraft
{
    internal static class WorkshopRules
    {
        internal const int VisibleRows = 6;
        internal const int MaximumTargets = 24;
        internal const int MaximumHistory = 60;
        internal const int MaximumStations = 24;
        internal const int MaximumTarget = 100000;
        internal const int BatchLimit = 10;

        // Once usable preloaded material has been considered, share the rest
        // across free machines. A small order cannot be assigned twice.
        internal static int ShareBatches(long products, int yield, int machines, bool roundUp)
        {
            if (products <= 0 || yield <= 0 || machines <= 0) return 0;
            long batches = roundUp ? (products - 1) / yield + 1 : products / yield;
            return batches <= 0 ? 0 : (int)Math.Min(BatchLimit, (batches - 1) / machines + 1);
        }

        internal static int OrderBatches(int remaining, int yield)
        {
            return yield <= 0 || remaining <= 0 ? 0
                : (int)Math.Min(BatchLimit, ((long)remaining + yield - 1) / yield);
        }

        internal static int FeedCount(long requiredUnits, long availableUnits, long pendingUnits, int weight, int room)
        {
            if (weight <= 0 || room <= 0) return 0;
            long deficit = requiredUnits - availableUnits - pendingUnits;
            return deficit <= 0 ? 0 : (int)Math.Min(room, (deficit + weight - 1) / weight);
        }

        internal static int FuelCount(float neededSeconds, float availableSeconds, int secondsPerItem, int room)
        {
            if (secondsPerItem <= 0 || room <= 0 || float.IsNaN(neededSeconds) || float.IsNaN(availableSeconds)) return 0;
            // A short buffer limits waste, even for very long native queues.
            double deficit = Math.Min(120d, Math.Max(0d, neededSeconds)) - Math.Max(0d, availableSeconds);
            return deficit <= 0d ? 0 : (int)Math.Min(room, Math.Ceiling(deficit / secondsPerItem));
        }

        // Count queued and waiting output as stock. Never queue a partial recipe
        // or exceed the target, including recipes whose yield is greater than one.
        internal static int BatchesNeeded(int target, long stored, long waiting, long queued, int yield)
        {
            if (yield <= 0) return 0;
            long maximum = Math.Max(0, Math.Min(MaximumTarget, target));
            long missing = maximum - Math.Min(maximum, Math.Max(0, stored))
                - Math.Min(maximum, Math.Max(0, waiting)) - Math.Min(maximum, Math.Max(0, queued));
            return missing <= 0 ? 0 : (int)Math.Min(BatchLimit, missing / yield);
        }

        internal static int ClampTarget(int target)
        {
            return Math.Max(1, Math.Min(MaximumTarget, target));
        }
    }
}
