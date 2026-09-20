namespace NearbyCraft
{
    public sealed class NearbyCraftConfig
    {
        public bool Enabled = true;
        public int Range = 15;
        public int CacheMilliseconds = 250;
        public bool IncludePlayerStorage = true;
        public bool IncludeWorkstationOutputs = true;
        public bool IncludeCollectors = true;
        public bool IncludeVehicles = true;
        public bool IncludeDrones = true;
        public bool RespectLockedSlots = true;
        public int TerminalRange = 15;
        public string TerminalSort = "Name";
        public bool TerminalAutoFocusSearch = true;
        public System.Collections.Generic.Dictionary<string, int> PersonalReserves = new System.Collections.Generic.Dictionary<string, int>();
        public bool DebugLogging = false;

        internal void Validate()
        {
            if (PersonalReserves == null)
                PersonalReserves = new System.Collections.Generic.Dictionary<string, int>();
            foreach (string key in new System.Collections.Generic.List<string>(PersonalReserves.Keys))
                PersonalReserves[key] = System.Math.Max(0, System.Math.Min(1000000, PersonalReserves[key]));
            if (Range < 1)
            {
                Range = 1;
            }
            else if (Range > 30)
            {
                Range = 30;
            }

            if (CacheMilliseconds < 100)
            {
                CacheMilliseconds = 100;
            }
            else if (CacheMilliseconds > 2000)
            {
                CacheMilliseconds = 2000;
            }

            if (TerminalRange < 1)
            {
                TerminalRange = 1;
            }
            else if (TerminalRange > 30)
            {
                TerminalRange = 30;
            }

            if (string.Equals(TerminalSort, "Count", System.StringComparison.OrdinalIgnoreCase))
            {
                TerminalSort = "Count";
            }
            else if (string.Equals(TerminalSort, "Type", System.StringComparison.OrdinalIgnoreCase))
            {
                TerminalSort = "Type";
            }
            else
            {
                TerminalSort = "Name";
            }
        }
    }
}
