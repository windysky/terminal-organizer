using System;
using TerminalOrganizer.Core.Geometry;

namespace TerminalOrganizer.Core.Layouts
{
    /// <summary>
    /// Resolves the layout applied to a device key, or a custom layout by uuid, into zone rects (REQ-RES-001..005).
    /// Returns exactly one of Supported / Unsupported / Invalid; never null, never throws for data content.
    /// Fixed precedence: 1 applied document, 2 device-key lookup, 3 type classification, 4 work area,
    /// 5 custom document, 6 custom uuid lookup, 7 layout-level checks, 8 grid boundaries, 9 per-zone checks.
    /// </summary>
    public static class LayoutResolver
    {
        private const string CustomType = "custom";

        // @MX:ANCHOR: [AUTO] Public contract: resolve the FancyZones layout applied to one monitor/desktop key.
        // @MX:REASON: Its reason codes drive tray notices; SPEC-LAYOUT-002 extends step 3 with template geometry.
        public static LayoutResult ResolveByDeviceKey(string appliedLayoutsJson, string customLayoutsJson, DeviceKey key, WorkArea workArea)
        {
            if (key == null) throw new ArgumentNullException("key");
            if (workArea == null) throw new ArgumentNullException("workArea");

            // Step 1: applied document validity.
            AppliedLayoutsDocument applied = FancyZonesJsonReader.ParseAppliedLayouts(appliedLayoutsJson);
            if (!applied.IsValid)
            {
                return LayoutResult.Invalid(LayoutReason.MalformedJson, "applied-layouts.json: " + applied.Detail, null);
            }

            // Step 2: device-key lookup. The first accepted match wins (AppliedLayouts.cpp:215).
            AppliedLayoutEntry entry = null;
            foreach (AppliedLayoutEntry candidate in applied.Entries)
            {
                if (candidate.Device.Matches(key))
                {
                    entry = candidate;
                    break;
                }
            }
            if (entry == null)
            {
                foreach (RejectedEntry rejected in applied.Rejected)
                {
                    if (rejected.Device != null && rejected.Device.Matches(key))
                    {
                        return LayoutResult.Invalid(rejected.Reason,
                            string.Format("applied-layouts.json entry {0} for this device was rejected: {1}", rejected.Position, rejected.Detail), null);
                    }
                }
                return LayoutResult.Unsupported(LayoutReason.NoAppliedLayout, "no applied layout for device " + key, null, null);
            }

            // Step 3: type classification (TypeFromString, FancyZonesDataTypes.cpp:57-87).
            string type = entry.Type;
            switch (type)
            {
                case CustomType:
                    break;
                case "rows":
                case "columns":
                case "grid":
                case "priority-grid":
                    return ResolveTemplateSteps(type, entry.ZoneCount, entry.ShowSpacing, entry.Spacing, workArea);
                case "focus":
                    return LayoutResult.Unsupported(LayoutReason.Focus, "the focus layout is not supported", type, null);
                case "blank":
                    return LayoutResult.Unsupported(LayoutReason.Blank, "the blank layout has no zones", type, null);
                default:
                    return LayoutResult.Unsupported(LayoutReason.UnknownType, "unknown layout type: " + type, type, null);
            }

            // Steps 4-9. The applied snapshot's show-spacing, spacing and zone-count are ignored for custom layouts.
            return ResolveCustomSteps(customLayoutsJson, entry.Uuid, workArea, type);
        }

        // @MX:ANCHOR: [AUTO] Public contract: resolve a custom layout directly by uuid (starts at step 4).
        // @MX:REASON: Used by tests and by callers that already know the layout; shares steps 4-9 with key resolution.
        public static LayoutResult ResolveCustomLayout(string customLayoutsJson, string uuid, WorkArea workArea)
        {
            if (workArea == null) throw new ArgumentNullException("workArea");
            return ResolveCustomSteps(customLayoutsJson, uuid, workArea, CustomType);
        }

        // @MX:ANCHOR: [AUTO] Public contract: resolve a built-in template directly (REQ-INT-002; starts at step 4).
        // @MX:REASON: The reported type is the requested template type (never "custom"); templates skip steps 5-6.
        public static LayoutResult ResolveTemplate(string type, int zoneCount, bool showSpacing, int spacing, WorkArea workArea)
        {
            if (workArea == null) throw new ArgumentNullException("workArea");
            if (type != "rows" && type != "columns" && type != "grid" && type != "priority-grid")
            {
                return LayoutResult.Unsupported(LayoutReason.UnknownType, "unknown layout type: " + type, type, null);
            }
            return ResolveTemplateSteps(type, zoneCount, showSpacing, spacing, workArea);
        }

        /// <summary>
        /// Template precedence (REQ-INT-005): steps 1-4, 7, 8 and 9; steps 5-6 are skipped, so the
        /// custom-layouts document is neither read nor needed.
        /// </summary>
        private static LayoutResult ResolveTemplateSteps(string type, int zoneCount, bool showSpacing, int spacing, WorkArea workArea)
        {
            // Step 4: work area.
            if (!workArea.IsValid)
            {
                return InvalidWorkAreaResult(workArea, type);
            }

            // Step 7: zone-count guard (REQ-INT-004), before any cell map is allocated.
            if (zoneCount <= 0)
            {
                return LayoutResult.Invalid(LayoutReason.InvalidZoneCount,
                    string.Format("template \"{0}\": zone-count {1} must be positive", type, zoneCount), type);
            }
            if (zoneCount > TemplateZoneCalculator.MaxTemplateZoneCount)
            {
                return LayoutResult.Invalid(LayoutReason.ZoneCountOutOfRange,
                    string.Format("template \"{0}\": zone-count {1} exceeds the maximum of {2}", type, zoneCount,
                        TemplateZoneCalculator.MaxTemplateZoneCount), type);
            }

            // Steps 8-9: the template geometry with its overflow pre-check and per-zone checks.
            int effectiveSpacing = showSpacing ? spacing : 0;
            ZoneComputation computation = TemplateZoneCalculator.Calculate(workArea.Width, workArea.Height, type,
                zoneCount, effectiveSpacing);
            if (!computation.IsValid)
            {
                return LayoutResult.Invalid(computation.Reason,
                    string.Format("template \"{0}\": {1}", type, computation.Detail), type);
            }
            string offsetDetail;
            Zone[] screen = OffsetToScreen(computation.Zones, workArea, "template \"" + type + "\"", out offsetDetail);
            if (screen == null)
            {
                return LayoutResult.Invalid(LayoutReason.ArithmeticOverflow, offsetDetail, type);
            }
            return LayoutResult.Supported(type, null, effectiveSpacing, screen, computation.Warnings);
        }

        private static LayoutResult ResolveCustomSteps(string customLayoutsJson, string uuid, WorkArea workArea, string layoutType)
        {
            // Step 4: work area.
            if (!workArea.IsValid)
            {
                return InvalidWorkAreaResult(workArea, layoutType);
            }

            // Step 5: custom document validity.
            CustomLayoutsDocument custom = FancyZonesJsonReader.ParseCustomLayouts(customLayoutsJson);
            if (!custom.IsValid)
            {
                return LayoutResult.Invalid(LayoutReason.MalformedJson, "custom-layouts.json: " + custom.Detail, layoutType);
            }

            // Step 6: uuid lookup by GUID value. The last accepted layout wins (CustomLayouts.cpp:173).
            CustomLayout layout = null;
            foreach (CustomLayout candidate in custom.Layouts)
            {
                if (DeviceKey.GuidOrOrdinalEquals(candidate.Uuid, uuid))
                {
                    layout = candidate;
                }
            }
            if (layout == null)
            {
                RejectedEntry lastRejected = null;
                foreach (RejectedEntry rejected in custom.Rejected)
                {
                    if (rejected.Uuid != null && DeviceKey.GuidOrOrdinalEquals(rejected.Uuid, uuid))
                    {
                        lastRejected = rejected;
                    }
                }
                if (lastRejected != null)
                {
                    return LayoutResult.Invalid(lastRejected.Reason,
                        string.Format("custom-layouts.json entry {0} with uuid {1} was rejected: {2}", lastRejected.Position, uuid, lastRejected.Detail),
                        layoutType);
                }
                return LayoutResult.Unsupported(LayoutReason.CustomLayoutNotFound, "custom layout not found: " + uuid, layoutType, null);
            }

            // Steps 7-9: geometry on the relative rect, then the origin offset.
            ZoneComputation computation;
            int effectiveSpacing;
            if (layout.Grid != null)
            {
                GridLayoutInfo grid = layout.Grid;
                effectiveSpacing = grid.ShowSpacing ? grid.Spacing : 0;
                computation = GridZoneCalculator.Calculate(workArea.Width, workArea.Height, grid.RowsPercentage,
                    grid.ColumnsPercentage, grid.CellChildMap, effectiveSpacing, grid.ZoneCount);
            }
            else
            {
                CanvasLayoutInfo canvas = layout.Canvas;
                effectiveSpacing = 0;
                computation = CanvasZoneCalculator.Calculate(workArea.Width, workArea.Height, workArea.Dpi,
                    canvas.RefWidth, canvas.RefHeight, canvas.Zones);
            }
            if (!computation.IsValid)
            {
                return LayoutResult.Invalid(computation.Reason,
                    string.Format("layout \"{0}\": {1}", layout.Name, computation.Detail), layoutType);
            }

            Zone[] relative = computation.Zones;
            string offsetDetail;
            Zone[] screen = OffsetToScreen(relative, workArea, "layout \"" + layout.Name + "\"", out offsetDetail);
            if (screen == null)
            {
                return LayoutResult.Invalid(LayoutReason.ArithmeticOverflow, offsetDetail, layoutType);
            }
            return LayoutResult.Supported(layoutType, layout.Name, effectiveSpacing, screen, computation.Warnings);
        }

        private static LayoutResult InvalidWorkAreaResult(WorkArea workArea, string layoutType)
        {
            return LayoutResult.Invalid(LayoutReason.InvalidWorkArea,
                string.Format("work area {0}x{1} at DPI {2} must be positive", workArea.Width, workArea.Height, workArea.Dpi),
                layoutType);
        }

        /// <summary>
        /// Offsets relative rects by the work-area origin into screen rects, checking that every
        /// screen position fits the 32-bit range. Returns null and fills overflowDetail otherwise.
        /// </summary>
        private static Zone[] OffsetToScreen(Zone[] relative, WorkArea workArea, string context, out string overflowDetail)
        {
            overflowDetail = null;
            Zone[] screen = new Zone[relative.Length];
            for (int k = 0; k < relative.Length; k++)
            {
                Zone z = relative[k];
                long screenLeft = (long)z.Left + workArea.Left;
                long screenTop = (long)z.Top + workArea.Top;
                if (screenLeft < int.MinValue || screenLeft > int.MaxValue || screenTop < int.MinValue || screenTop > int.MaxValue)
                {
                    overflowDetail = string.Format("{0}: zone {1} screen position outside the 32-bit range", context, z.Id);
                    return null;
                }
                screen[k] = new Zone(z.Id, (int)screenLeft, (int)screenTop, z.Width, z.Height);
            }
            return screen;
        }
    }
}
