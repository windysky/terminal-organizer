using System;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Geometry
{
    /// <summary>
    /// Port of the FancyZones built-in template geometry (LayoutConfigurator.cpp:14-92 and 235-398,
    /// v0.101.2362.0). Rows and columns apply the equal-split formulas directly with full spacing
    /// between zones (REQ-TPL-001/002); grid and priority-grid are reduced to percentages and a
    /// cell-child-map for the shared grid routine (REQ-TPL-003/004). Never throws for data content;
    /// the resolver guards the zone count (step 7) before calling here.
    /// </summary>
    public static class TemplateZoneCalculator
    {
        // @MX:NOTE: [AUTO] editor TemplateZoneCountMaximum (LayoutModel.cs:262-268 at v0.101.2362.0); a deviation guard, see SPEC-LAYOUT-002 spec.md §6.
        public const int MaxTemplateZoneCount = 128;

        // @MX:ANCHOR: [AUTO] Template entry point: resolver dispatch for rows, columns, grid and priority-grid.
        // @MX:REASON: SPEC-LAYOUT-002 REQ-INT-001/002; the retired TemplateGeometryPending branch resolves here.
        /// <summary>
        /// Computes relative zone rects for a built-in template against a work area of width x height.
        /// spacing is the EFFECTIVE spacing (0 when show-spacing is false); it is never DPI-scaled
        /// (REQ-TPL-005). Step 8 pre-checks every intermediate product of the formula against the
        /// 32-bit range before any zone is emitted (REQ-INT-005).
        /// </summary>
        public static ZoneComputation Calculate(int width, int height, string type, int zoneCount, int spacing)
        {
            if (type == "rows")
            {
                return CalculateSplit(width, height, zoneCount, spacing, true);
            }
            if (type == "columns")
            {
                return CalculateSplit(width, height, zoneCount, spacing, false);
            }

            // Grid and priority-grid share SPEC-LAYOUT-001's grid routine (REQ-TPL-003/004).
            int[] rowsPercentage;
            int[] columnsPercentage;
            int[][] cellChildMap;
            // @MX:NOTE: [AUTO] LayoutConfigurator.cpp:392: the predefined table serves zoneCount < 11 only; 11 and above fall back to the derived grid.
            if (type == "priority-grid" && zoneCount < 11)
            {
                CopyPriorityTable(zoneCount, out rowsPercentage, out columnsPercentage, out cellChildMap);
            }
            else
            {
                DeriveGrid(zoneCount, out rowsPercentage, out columnsPercentage, out cellChildMap);
            }
            return GridZoneCalculator.Calculate(width, height, rowsPercentage, columnsPercentage, cellChildMap,
                spacing, zoneCount);
        }

        /// <summary>
        /// Rows and columns (LayoutConfigurator.cpp:235-279 and 281-325). The split axis gets
        /// total = extent - s*(n+1); each zone ends at top + (i+1)*total/n - i*total/n with C++-style
        /// truncation toward zero, and the next zone starts at the previous end + s. The fixed axis
        /// spans s to (extent - 2s) + s.
        /// </summary>
        private static ZoneComputation CalculateSplit(int width, int height, int zoneCount, int spacing, bool rows)
        {
            long s = spacing;
            long n = zoneCount;
            long splitExtent = rows ? height : width;
            long fixedExtent = rows ? width : height;
            long splitTotal = splitExtent - s * (n + 1);
            long fixedTotal = fixedExtent - 2 * s;

            // Step 8 pre-check (REQ-INT-005): s*(n+1), 2s, the extent minus those terms, and
            // (i+1)*total and i*total for every i, all before any zone is computed.
            string overflow = CheckInt32(s * (n + 1), "s*(n+1)");
            if (overflow == null) overflow = CheckInt32(2 * s, "2*s");
            if (overflow == null) overflow = CheckInt32(fixedTotal, rows ? "width-2s" : "height-2s");
            if (overflow == null) overflow = CheckInt32(splitTotal, rows ? "height-s*(n+1)" : "width-s*(n+1)");
            for (int i = 0; overflow == null && i < zoneCount; i++)
            {
                overflow = CheckInt32((i + 1) * splitTotal, string.Format("({0}+1)*total", i));
                if (overflow == null) overflow = CheckInt32((long)i * splitTotal, string.Format("{0}*total", i));
            }
            if (overflow != null)
            {
                return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow,
                    string.Format("{0} template: {1}", rows ? "rows" : "columns", overflow));
            }

            // Step 9: per-zone checks in id order 0..n-1 (REQ-INT-005), same check order as the grid routine.
            Zone[] zones = new Zone[zoneCount];
            long fixedLow = s;
            long fixedHigh = fixedTotal + s;
            long splitPos = s;
            for (int i = 0; i < zoneCount; i++)
            {
                long splitEnd = splitPos + (i + 1) * splitTotal / n - i * splitTotal / n;
                long left = rows ? fixedLow : splitPos;
                long top = rows ? splitPos : fixedLow;
                long right = rows ? fixedHigh : splitEnd;
                long bottom = rows ? splitEnd : fixedHigh;
                long zoneWidth = right - left;
                long zoneHeight = bottom - top;

                string where = string.Format("zone {0}", i);
                if (!FitsInt32(left) || !FitsInt32(top) || !FitsInt32(right) || !FitsInt32(bottom)
                    || !FitsInt32(zoneWidth) || !FitsInt32(zoneHeight))
                {
                    return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow, where + ": edge outside the 32-bit range");
                }
                if (left < FancyZonesConstants.MaxNegativeSpacing || top < FancyZonesConstants.MaxNegativeSpacing
                    || right < FancyZonesConstants.MaxNegativeSpacing || bottom < FancyZonesConstants.MaxNegativeSpacing)
                {
                    return ZoneComputation.Failure(LayoutReason.EdgeBelowMinimum,
                        string.Format("{0}: edge below {1} (l={2}, t={3}, r={4}, b={5})", where,
                            FancyZonesConstants.MaxNegativeSpacing, left, top, right, bottom));
                }
                if (zoneWidth < 0 || zoneHeight < 0)
                {
                    return ZoneComputation.Failure(LayoutReason.NegativeSize,
                        string.Format("{0}: negative size {1}x{2}", where, zoneWidth, zoneHeight));
                }
                zones[i] = new Zone(i, (int)left, (int)top, (int)zoneWidth, (int)zoneHeight);
                splitPos = splitEnd + s;
            }
            return ZoneComputation.Success(zones, new LayoutWarning[0]);
        }

        /// <summary>
        /// Grid shape derivation (LayoutConfigurator.cpp:327-382). Rows start at 1 and grow while
        /// n/rows &gt;= rows, then step back by 1; columns are n/rows plus 1 when n is not divisible.
        /// Segment i of k gets 10000*(i+1)/k - 10000*i/k percent; the map is filled row-major with
        /// 0, 1, 2 and so on, and the last index repeats once n-1 is reached.
        /// </summary>
        private static void DeriveGrid(int zoneCount, out int[] rowsPercentage, out int[] columnsPercentage, out int[][] cellChildMap)
        {
            int rows = 1;
            while (zoneCount / rows >= rows)
            {
                rows++;
            }
            rows--;
            int columns = zoneCount / rows;
            if (zoneCount % rows != 0)
            {
                columns++;
            }

            rowsPercentage = Percentages(rows);
            columnsPercentage = Percentages(columns);
            cellChildMap = new int[rows][];
            int counter = 0;
            for (int r = 0; r < rows; r++)
            {
                cellChildMap[r] = new int[columns];
                for (int c = 0; c < columns; c++)
                {
                    cellChildMap[r][c] = counter < zoneCount ? counter : zoneCount - 1;
                    counter++;
                }
            }
        }

        private static int[] Percentages(int segments)
        {
            int[] result = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                result[i] = FancyZonesConstants.PercentMultiplier * (i + 1) / segments
                    - FancyZonesConstants.PercentMultiplier * i / segments;
            }
            return result;
        }

        // @MX:NOTE: [AUTO] Verbatim copy of the predefined table (LayoutConfigurator.cpp:14-92 at v0.101.2362.0); entry 11 is unreachable (zoneCount < 11).
        private static void CopyPriorityTable(int zoneCount, out int[] rowsPercentage, out int[] columnsPercentage, out int[][] cellChildMap)
        {
            switch (zoneCount)
            {
                case 1:
                    rowsPercentage = new int[] { 10000 };
                    columnsPercentage = new int[] { 10000 };
                    cellChildMap = new int[][] { new int[] { 0 } };
                    break;
                case 2:
                    rowsPercentage = new int[] { 10000 };
                    columnsPercentage = new int[] { 6667, 3333 };
                    cellChildMap = new int[][] { new int[] { 0, 1 } };
                    break;
                case 3:
                    rowsPercentage = new int[] { 10000 };
                    columnsPercentage = new int[] { 2500, 5000, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2 } };
                    break;
                case 4:
                    rowsPercentage = new int[] { 5000, 5000 };
                    columnsPercentage = new int[] { 2500, 5000, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2 }, new int[] { 0, 1, 3 } };
                    break;
                case 5:
                    rowsPercentage = new int[] { 5000, 5000 };
                    columnsPercentage = new int[] { 2500, 5000, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2 }, new int[] { 3, 1, 4 } };
                    break;
                case 6:
                    rowsPercentage = new int[] { 3333, 3334, 3333 };
                    columnsPercentage = new int[] { 2500, 5000, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2 }, new int[] { 0, 1, 3 }, new int[] { 4, 1, 5 } };
                    break;
                case 7:
                    rowsPercentage = new int[] { 3333, 3334, 3333 };
                    columnsPercentage = new int[] { 2500, 5000, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2 }, new int[] { 3, 1, 4 }, new int[] { 5, 1, 6 } };
                    break;
                case 8:
                    rowsPercentage = new int[] { 3333, 3334, 3333 };
                    columnsPercentage = new int[] { 2500, 2500, 2500, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2, 3 }, new int[] { 4, 1, 2, 5 }, new int[] { 6, 1, 2, 7 } };
                    break;
                case 9:
                    rowsPercentage = new int[] { 3333, 3334, 3333 };
                    columnsPercentage = new int[] { 2500, 2500, 2500, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2, 3 }, new int[] { 4, 1, 2, 5 }, new int[] { 6, 1, 7, 8 } };
                    break;
                default:
                    rowsPercentage = new int[] { 3333, 3334, 3333 };
                    columnsPercentage = new int[] { 2500, 2500, 2500, 2500 };
                    cellChildMap = new int[][] { new int[] { 0, 1, 2, 3 }, new int[] { 4, 1, 5, 6 }, new int[] { 7, 1, 8, 9 } };
                    break;
            }
        }

        // @MX:NOTE: [AUTO] MSVC `long` is 32-bit; template intermediates outside Int32 are reported as ArithmeticOverflow (GridZoneCalculator has the same rule).
        private static string CheckInt32(long value, string what)
        {
            if (!FitsInt32(value))
            {
                return string.Format("{0} = {1} exceeds the 32-bit range", what, value);
            }
            return null;
        }

        private static bool FitsInt32(long value)
        {
            return value >= int.MinValue && value <= int.MaxValue;
        }
    }
}
