using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Geometry
{
    /// <summary>
    /// Line-by-line port of FancyZones CalculateGridZones (LayoutConfigurator.cpp:107-192, v0.101.2362.0)
    /// with Int64 intermediates and a 32-bit range guard, plus the Zone::IsValid / AddZone checks.
    /// </summary>
    public static class GridZoneCalculator
    {
        // @MX:ANCHOR: [AUTO] Shared grid entry point: custom grids now, rows/columns/grid/priority-grid templates in SPEC-LAYOUT-002.
        // @MX:REASON: Pixel parity with FancyZones for every grid-shaped layout depends on this one routine.
        /// <summary>
        /// Computes relative zone rects for a grid work area of width x height.
        /// spacing is the EFFECTIVE spacing (0 when show-spacing is false); it is never DPI-scaled.
        /// expectedZoneCount feeds the ZoneIndexGap warning; pass a negative value to skip that check.
        /// Check order: 0 rows or 0 columns (EmptyLayout), boundaries (ArithmeticOverflow), then per zone in emission order: overflow,
        /// NegativeZoneIndex, EdgeBelowMinimum, NegativeSize, DuplicateZoneIndex (REQ-RES-005 steps 8-9).
        /// </summary>
        public static ZoneComputation Calculate(int width, int height, int[] rowsPercentage, int[] columnsPercentage,
            int[][] cellChildMap, int spacing, int expectedZoneCount)
        {
            ValidateShape(rowsPercentage, columnsPercentage, cellChildMap);
            int rows = rowsPercentage.Length;
            int columns = columnsPercentage.Length;

            // Step 7: a grid with 0 rows or 0 columns emits no zones; FancyZones would end with an empty layout.
            if (rows == 0 || columns == 0)
            {
                return ZoneComputation.Failure(LayoutReason.EmptyLayout,
                    string.Format("grid has {0} rows and {1} columns", rows, columns));
            }

            // Step 8: boundaries. Every intermediate product must fit FancyZones' 32-bit long.
            long[] rowStart;
            long[] rowEnd;
            long[] colStart;
            long[] colEnd;
            string rowOverflow = ComputeBoundaries(rowsPercentage, height, "row", out rowStart, out rowEnd);
            string columnOverflow = ComputeBoundaries(columnsPercentage, width, "column", out colStart, out colEnd);
            if (rowOverflow != null || columnOverflow != null)
            {
                return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow, rowOverflow ?? columnOverflow);
            }

            // Step 9: per-zone checks in emission order (row-major order of top-left cells).
            Dictionary<int, Zone> zonesById = new Dictionary<int, Zone>();
            List<int[]> spans = new List<int[]>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int i = cellChildMap[r][c];
                    bool isTopLeft = (r == 0 || cellChildMap[r - 1][c] != i) && (c == 0 || cellChildMap[r][c - 1] != i);
                    if (!isTopLeft)
                    {
                        continue;
                    }

                    int maxRow = r;
                    while (maxRow + 1 < rows && cellChildMap[maxRow + 1][c] == i)
                    {
                        maxRow++;
                    }
                    int maxCol = c;
                    while (maxCol + 1 < columns && cellChildMap[r][maxCol + 1] == i)
                    {
                        maxCol++;
                    }

                    // Index-based spacing rule (LayoutConfigurator.cpp:167-170): full spacing on the first/last
                    // row and column, spacing / 2 (truncating) elsewhere, regardless of pixel position.
                    long left = colStart[c] + (c == 0 ? spacing : spacing / 2);
                    long top = rowStart[r] + (r == 0 ? spacing : spacing / 2);
                    long right = colEnd[maxCol] - (maxCol == columns - 1 ? spacing : spacing / 2);
                    long bottom = rowEnd[maxRow] - (maxRow == rows - 1 ? spacing : spacing / 2);
                    long zoneWidth = right - left;
                    long zoneHeight = bottom - top;

                    string where = string.Format("zone {0} at cell ({1},{2})", i, r, c);
                    if (!FitsInt32(left) || !FitsInt32(top) || !FitsInt32(right) || !FitsInt32(bottom)
                        || !FitsInt32(zoneWidth) || !FitsInt32(zoneHeight))
                    {
                        return ZoneComputation.Failure(LayoutReason.ArithmeticOverflow, where + ": edge outside the 32-bit range");
                    }
                    if (i < 0)
                    {
                        return ZoneComputation.Failure(LayoutReason.NegativeZoneIndex, where + ": negative zone index");
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
                    if (zonesById.ContainsKey(i))
                    {
                        return ZoneComputation.Failure(LayoutReason.DuplicateZoneIndex, where + ": second top-left cell for this index");
                    }

                    zonesById.Add(i, new Zone(i, (int)left, (int)top, (int)zoneWidth, (int)zoneHeight));
                    spans.Add(new int[] { i, r, maxRow, c, maxCol });
                }
            }

            List<int> ids = new List<int>(zonesById.Keys);
            ids.Sort();
            Zone[] zones = new Zone[ids.Count];
            for (int k = 0; k < ids.Count; k++)
            {
                zones[k] = zonesById[ids[k]];
            }

            List<LayoutWarning> warnings = new List<LayoutWarning>();
            if (Sum(rowsPercentage) != FancyZonesConstants.PercentMultiplier || Sum(columnsPercentage) != FancyZonesConstants.PercentMultiplier)
            {
                warnings.Add(LayoutWarning.PercentSumNot10000);
            }
            if (AnyBelowOne(rowsPercentage) || AnyBelowOne(columnsPercentage))
            {
                warnings.Add(LayoutWarning.PercentBelowOne);
            }
            if (expectedZoneCount >= 0 && zones.Length != expectedZoneCount)
            {
                warnings.Add(LayoutWarning.ZoneIndexGap);
            }
            if (HasNonRectangularMerge(cellChildMap, spans))
            {
                warnings.Add(LayoutWarning.NonRectangularMerge);
            }
            return ZoneComputation.Success(zones, warnings.ToArray());
        }

        private static void ValidateShape(int[] rowsPercentage, int[] columnsPercentage, int[][] cellChildMap)
        {
            if (rowsPercentage == null) throw new ArgumentNullException("rowsPercentage");
            if (columnsPercentage == null) throw new ArgumentNullException("columnsPercentage");
            if (cellChildMap == null) throw new ArgumentNullException("cellChildMap");
            if (cellChildMap.Length != rowsPercentage.Length)
            {
                throw new ArgumentException("cellChildMap must have one row per rows-percentage value", "cellChildMap");
            }
            foreach (int[] row in cellChildMap)
            {
                if (row == null || row.Length != columnsPercentage.Length)
                {
                    throw new ArgumentException("every cellChildMap row must have one value per columns-percentage value", "cellChildMap");
                }
            }
        }

        /// <summary>
        /// Start/End = cumulative basis points x extent / 10000 with truncating division (C++ integer semantics).
        /// Returns an overflow description, or null when every intermediate value fits in 32 bits.
        /// </summary>
        private static string ComputeBoundaries(int[] percentages, int extent, string axis, out long[] start, out long[] end)
        {
            start = new long[percentages.Length];
            end = new long[percentages.Length];
            long total = 0;
            for (int k = 0; k < percentages.Length; k++)
            {
                long product = total * extent;
                if (!FitsInt32(product))
                {
                    return string.Format("{0} {1}: {2} x {3} exceeds the 32-bit range", axis, k, total, extent);
                }
                start[k] = product / FancyZonesConstants.PercentMultiplier;
                total += percentages[k];
                if (!FitsInt32(total))
                {
                    return string.Format("{0} {1}: cumulative percentage {2} exceeds the 32-bit range", axis, k, total);
                }
                product = total * extent;
                if (!FitsInt32(product))
                {
                    return string.Format("{0} {1}: {2} x {3} exceeds the 32-bit range", axis, k, total, extent);
                }
                end[k] = product / FancyZonesConstants.PercentMultiplier;
            }
            return null;
        }

        // @MX:NOTE: [AUTO] MSVC `long` is 32-bit (plan.md §B.5); values outside Int32 are reported as ArithmeticOverflow, not emulated.
        private static bool FitsInt32(long value)
        {
            return value >= int.MinValue && value <= int.MaxValue;
        }

        private static long Sum(int[] values)
        {
            long sum = 0;
            foreach (int v in values)
            {
                sum += v;
            }
            return sum;
        }

        private static bool AnyBelowOne(int[] values)
        {
            foreach (int v in values)
            {
                if (v < 1) return true;
            }
            return false;
        }

        /// <summary>
        /// Index-span rule (REQ-GEO-005): a zone's span runs from its top-left cell to its downward and rightward
        /// extent in row/column indices. Warn when a span cell carries another index, or a cell carrying a zone's
        /// index lies outside that zone's span. Spans are {id, firstRow, lastRow, firstCol, lastCol}.
        /// </summary>
        private static bool HasNonRectangularMerge(int[][] map, List<int[]> spans)
        {
            Dictionary<int, int[]> spanById = new Dictionary<int, int[]>();
            foreach (int[] span in spans)
            {
                spanById[span[0]] = span;
                for (int r = span[1]; r <= span[2]; r++)
                {
                    for (int c = span[3]; c <= span[4]; c++)
                    {
                        if (map[r][c] != span[0]) return true;
                    }
                }
            }
            for (int r = 0; r < map.Length; r++)
            {
                for (int c = 0; c < map[r].Length; c++)
                {
                    int[] span;
                    if (spanById.TryGetValue(map[r][c], out span)
                        && (r < span[1] || r > span[2] || c < span[3] || c > span[4]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
