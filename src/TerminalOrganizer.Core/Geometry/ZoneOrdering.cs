using System;
using System.Collections.Generic;

namespace TerminalOrganizer.Core.Geometry
{
    /// <summary>
    /// Greedy near-tie ordering of zones by size (REQ-ORD-001, user decision D1), 64-bit integer arithmetic only.
    /// </summary>
    public static class ZoneOrdering
    {
        // @MX:NOTE: [AUTO] User decision D1 (2026-09-23): areas within 100 bp (1%) of the largest count as tied.
        public const int DefaultToleranceBp = 100;

        // @MX:ANCHOR: [AUTO] Default-tolerance ordering entry point consumed by zone assignment.
        // @MX:REASON: Later SPECs reserve the first zone of this order for the manager window.
        public static int[] Order(Zone[] zones)
        {
            return Order(zones, DefaultToleranceBp);
        }

        // @MX:ANCHOR: [AUTO] Explicit-tolerance ordering entry point (tolBp in 0..10000).
        // @MX:REASON: Zone assignment and its tests pin exact orders at tolBp 0, 100 and 10000.
        /// <summary>
        /// Repeatedly takes, among the remaining zones with area * 10000 >= largestRemainingArea * (10000 - tolBp),
        /// the one with the smallest top, then left, then zone id. The result depends only on the zone set.
        /// </summary>
        public static int[] Order(Zone[] zones, int toleranceBp)
        {
            if (zones == null) throw new ArgumentNullException("zones");
            if (toleranceBp < 0 || toleranceBp > 10000)
            {
                throw new ArgumentOutOfRangeException("toleranceBp", toleranceBp, "toleranceBp must be between 0 and 10000.");
            }

            List<Zone> remaining = new List<Zone>(zones);
            if (remaining.Contains(null)) throw new ArgumentException("zones must not contain null", "zones");
            int[] order = new int[remaining.Count];
            int keepFactor = 10000 - toleranceBp;
            for (int n = 0; n < order.Length; n++)
            {
                long largest = long.MinValue;
                foreach (Zone z in remaining)
                {
                    if (z.Area > largest) largest = z.Area;
                }

                Zone best = null;
                foreach (Zone z in remaining)
                {
                    if (!IsCandidate(z.Area, largest, keepFactor)) continue;
                    if (best == null || Precedes(z, best)) best = z;
                }
                order[n] = best.Id;
                remaining.Remove(best);
            }
            return order;
        }

        private static bool Precedes(Zone a, Zone b)
        {
            if (a.Top != b.Top) return a.Top < b.Top;
            if (a.Left != b.Left) return a.Left < b.Left;
            if (a.Id != b.Id) return a.Id < b.Id;
            // Fully tied (duplicate ids at one position): prefer the larger area for a deterministic choice.
            return a.Area > b.Area;
        }

        /// <summary>
        /// Exact test of area * 10000 >= largest * keepFactor without 64-bit overflow (0 &lt;= keepFactor &lt;= 10000).
        /// With largest = h * 10000 + l, the test becomes (area - h * keepFactor) * 10000 >= l * keepFactor.
        /// The largest zone is always a candidate; negative areas (never produced by the calculators) only tie exactly.
        /// </summary>
        private static bool IsCandidate(long area, long largest, int keepFactor)
        {
            if (area >= largest) return true;
            if (largest <= 0 || area < 0) return false;
            long high = largest / 10000;
            long low = largest % 10000;
            long d = area - high * keepFactor;
            if (d < 0) return false;
            if (d >= 10000) return true;
            return d * 10000 >= low * keepFactor;
        }
    }
}
