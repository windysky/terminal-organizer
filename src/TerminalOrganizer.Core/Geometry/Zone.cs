using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Geometry
{
    /// <summary>An immutable zone rectangle with its FancyZones zone id.</summary>
    public sealed class Zone
    {
        private readonly int id;
        private readonly int left;
        private readonly int top;
        private readonly int width;
        private readonly int height;

        public Zone(int id, int left, int top, int width, int height)
        {
            this.id = id;
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
        }

        public int Id { get { return id; } }
        public int Left { get { return left; } }
        public int Top { get { return top; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        /// <summary>Width times height in 64-bit arithmetic.</summary>
        public long Area { get { return (long)width * height; } }

        /// <summary>Formats as "z{id}:{W}x{H}@{L},{T}" (the research.md notation).</summary>
        public override string ToString()
        {
            return string.Format("z{0}:{1}x{2}@{3},{4}", id, width, height, left, top);
        }
    }

    /// <summary>A monitor work area in physical pixels (taskbar excluded) and its DPI.</summary>
    public sealed class WorkArea
    {
        private readonly int left;
        private readonly int top;
        private readonly int width;
        private readonly int height;
        private readonly int dpi;

        public WorkArea(int left, int top, int width, int height, int dpi)
        {
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
            this.dpi = dpi;
        }

        public int Left { get { return left; } }
        public int Top { get { return top; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public int Dpi { get { return dpi; } }

        /// <summary>REQ-RES-005 step 4: width, height and DPI all positive.</summary>
        public bool IsValid { get { return width > 0 && height > 0 && dpi > 0; } }
    }

    /// <summary>
    /// Output of a zone calculator: relative rects in ascending id order plus warnings, or an Invalid reason.
    /// </summary>
    public sealed class ZoneComputation
    {
        private readonly LayoutReason reason;
        private readonly string detail;
        private readonly Zone[] zones;
        private readonly LayoutWarning[] warnings;

        private ZoneComputation(LayoutReason reason, string detail, Zone[] zones, LayoutWarning[] warnings)
        {
            this.reason = reason;
            this.detail = detail ?? string.Empty;
            this.zones = zones == null ? new Zone[0] : (Zone[])zones.Clone();
            this.warnings = warnings == null ? new LayoutWarning[0] : (LayoutWarning[])warnings.Clone();
        }

        public bool IsValid { get { return reason == LayoutReason.None; } }

        /// <summary>None on success; otherwise the Invalid reason.</summary>
        public LayoutReason Reason { get { return reason; } }
        public string Detail { get { return detail; } }

        /// <summary>Rects relative to the work-area origin, ascending zone id; empty when invalid.</summary>
        public Zone[] Zones { get { return (Zone[])zones.Clone(); } }
        public LayoutWarning[] Warnings { get { return (LayoutWarning[])warnings.Clone(); } }

        public static ZoneComputation Success(Zone[] zones, LayoutWarning[] warnings)
        {
            return new ZoneComputation(LayoutReason.None, string.Empty, zones, warnings);
        }

        public static ZoneComputation Failure(LayoutReason reason, string detail)
        {
            return new ZoneComputation(reason, detail, null, null);
        }
    }
}
