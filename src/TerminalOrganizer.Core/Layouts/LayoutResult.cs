using System;
using TerminalOrganizer.Core.Geometry;

namespace TerminalOrganizer.Core.Layouts
{
    /// <summary>The three possible outcomes of a layout resolution (REQ-RES-004).</summary>
    public enum LayoutResultKind
    {
        Supported,
        Unsupported,
        Invalid
    }

    /// <summary>
    /// Reason codes. Unsupported reasons: Focus, Blank, UnknownType, CustomLayoutNotFound,
    /// NoAppliedLayout. Every other non-None value is an Invalid reason.
    /// </summary>
    public enum LayoutReason
    {
        None,
        Focus,
        Blank,
        UnknownType,
        CustomLayoutNotFound,
        NoAppliedLayout,
        MalformedJson,
        MalformedEntry,
        ShapeMismatch,
        NegativeZoneIndex,
        EdgeBelowMinimum,
        NegativeSize,
        DuplicateZoneIndex,
        InvalidWorkArea,
        // @MX:NOTE: [AUTO] SPEC-LAYOUT-002 REQ-INT-004: template zone-count guard at step 7, before any allocation.
        InvalidZoneCount,
        // @MX:NOTE: [AUTO] SPEC-LAYOUT-002 REQ-INT-004: above the FancyZones editor maximum of 128.
        ZoneCountOutOfRange,
        InvalidCanvasReference,
        EmptyLayout,
        ArithmeticOverflow
    }

    /// <summary>Editor-rule departures reported on a Supported grid (REQ-GEO-005), in this order.</summary>
    public enum LayoutWarning
    {
        PercentSumNot10000,
        PercentBelowOne,
        ZoneIndexGap,
        NonRectangularMerge
    }

    /// <summary>
    /// Immutable resolution result. Never null; Zones and Warnings are never null (empty arrays instead).
    /// </summary>
    public sealed class LayoutResult
    {
        private readonly LayoutResultKind kind;
        private readonly LayoutReason reason;
        private readonly string detail;
        private readonly string layoutType;
        private readonly string layoutName;
        private readonly int effectiveSpacing;
        private readonly Zone[] zones;
        private readonly LayoutWarning[] warnings;
        private readonly TemplateDescriptor template;

        private LayoutResult(LayoutResultKind kind, LayoutReason reason, string detail, string layoutType,
            string layoutName, int effectiveSpacing, Zone[] zones, LayoutWarning[] warnings, TemplateDescriptor template)
        {
            this.kind = kind;
            this.reason = reason;
            this.detail = detail ?? string.Empty;
            this.layoutType = layoutType;
            this.layoutName = layoutName;
            this.effectiveSpacing = effectiveSpacing;
            this.zones = zones == null ? new Zone[0] : (Zone[])zones.Clone();
            this.warnings = warnings == null ? new LayoutWarning[0] : (LayoutWarning[])warnings.Clone();
            this.template = template;
        }

        public LayoutResultKind Kind { get { return kind; } }
        public LayoutReason Reason { get { return reason; } }
        public string Detail { get { return detail; } }

        /// <summary>The applied entry's type, or the requested kind ("custom") for a direct resolution.</summary>
        public string LayoutType { get { return layoutType; } }

        /// <summary>The custom layout name; null when no custom layout was resolved.</summary>
        public string LayoutName { get { return layoutName; } }

        /// <summary>0 when show-spacing is false or the layout is a canvas.</summary>
        public int EffectiveSpacing { get { return effectiveSpacing; } }

        /// <summary>Screen rects in ascending zone-id order; empty unless Supported.</summary>
        public Zone[] Zones { get { return (Zone[])zones.Clone(); } }

        public LayoutWarning[] Warnings { get { return (LayoutWarning[])warnings.Clone(); } }

        /// <summary>The template descriptor; always null since SPEC-LAYOUT-002 computes template geometry inline.</summary>
        public TemplateDescriptor Template { get { return template; } }

        public static LayoutResult Supported(string layoutType, string layoutName, int effectiveSpacing, Zone[] zones, LayoutWarning[] warnings)
        {
            return new LayoutResult(LayoutResultKind.Supported, LayoutReason.None, string.Empty, layoutType, layoutName,
                effectiveSpacing, zones, warnings, null);
        }

        public static LayoutResult Unsupported(LayoutReason reason, string detail, string layoutType, TemplateDescriptor template)
        {
            return new LayoutResult(LayoutResultKind.Unsupported, reason, detail, layoutType, null, 0, null, null, template);
        }

        public static LayoutResult Invalid(LayoutReason reason, string detail, string layoutType)
        {
            return new LayoutResult(LayoutResultKind.Invalid, reason, detail, layoutType, null, 0, null, null, null);
        }

        public override string ToString()
        {
            return string.Format("{0}({1}) {2}", kind, reason, detail);
        }
    }
}
