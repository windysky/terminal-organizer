using System;

namespace TerminalOrganizer.Core.Layouts
{
    /// <summary>Constants mirrored from PowerToys FancyZones v0.101.2362.0.</summary>
    public static class FancyZonesConstants
    {
        // @MX:NOTE: [AUTO] C_MULTIPLIER (LayoutConfigurator.cpp:11); grid percentages are integer basis points of 10000.
        public const int PercentMultiplier = 10000;

        // @MX:NOTE: [AUTO] ZoneConstants::MAX_NEGATIVE_SPACING (Zone.h:5); checked on RELATIVE rects, never screen rects.
        public const int MaxNegativeSpacing = -20;

        // @MX:NOTE: [AUTO] DPIAware::DEFAULT_DPI; canvas ref values and zone values are in 96-DPI logical units.
        public const int DefaultDpi = 96;

        // @MX:NOTE: [AUTO] Parser defaults for absent custom-grid fields (LayoutDefaults.h:5-8, CustomLayouts.cpp:90-92).
        public const bool DefaultShowSpacing = true;
        public const int DefaultSpacing = 16;
        public const int DefaultSensitivityRadius = 20;
    }

    /// <summary>
    /// The five-field FancyZones device key. Matching is exact (REQ-RES-001): monitor, monitor-instance
    /// and serial-number by ordinal string equality, virtual-desktop by GUID value, monitor-number by integer.
    /// </summary>
    public sealed class DeviceKey
    {
        private readonly string monitor;
        private readonly string monitorInstance;
        private readonly int monitorNumber;
        private readonly string serialNumber;
        private readonly string virtualDesktop;

        public DeviceKey(string monitor, string monitorInstance, int monitorNumber, string serialNumber, string virtualDesktop)
        {
            this.monitor = monitor;
            this.monitorInstance = monitorInstance;
            this.monitorNumber = monitorNumber;
            this.serialNumber = serialNumber;
            this.virtualDesktop = virtualDesktop;
        }

        public string Monitor { get { return monitor; } }
        public string MonitorInstance { get { return monitorInstance; } }
        public int MonitorNumber { get { return monitorNumber; } }
        public string SerialNumber { get { return serialNumber; } }
        public string VirtualDesktop { get { return virtualDesktop; } }

        /// <summary>
        /// Exact match. Virtual desktops compare by GUID value when both parse as GUIDs, otherwise ordinally
        /// (only a rejected entry can carry a non-GUID desktop).
        /// </summary>
        public bool Matches(DeviceKey other)
        {
            if (other == null)
            {
                return false;
            }
            return string.Equals(monitor, other.monitor, StringComparison.Ordinal)
                && string.Equals(monitorInstance, other.monitorInstance, StringComparison.Ordinal)
                && string.Equals(serialNumber, other.serialNumber, StringComparison.Ordinal)
                && monitorNumber == other.monitorNumber
                && GuidOrOrdinalEquals(virtualDesktop, other.virtualDesktop);
        }

        internal static bool GuidOrOrdinalEquals(string a, string b)
        {
            Guid ga;
            Guid gb;
            if (Guid.TryParse(a, out ga) && Guid.TryParse(b, out gb))
            {
                return ga == gb;
            }
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return string.Format("{0}|{1}|{2}|{3}|{4}", monitor, monitorInstance, monitorNumber, serialNumber, virtualDesktop);
        }
    }

    /// <summary>An accepted applied-layouts.json entry.</summary>
    public sealed class AppliedLayoutEntry
    {
        private readonly int position;
        private readonly DeviceKey device;
        private readonly string uuid;
        private readonly string type;
        private readonly bool showSpacing;
        private readonly int spacing;
        private readonly int zoneCount;
        private readonly int sensitivityRadius;

        public AppliedLayoutEntry(int position, DeviceKey device, string uuid, string type, bool showSpacing,
            int spacing, int zoneCount, int sensitivityRadius)
        {
            this.position = position;
            this.device = device;
            this.uuid = uuid;
            this.type = type;
            this.showSpacing = showSpacing;
            this.spacing = spacing;
            this.zoneCount = zoneCount;
            this.sensitivityRadius = sensitivityRadius;
        }

        /// <summary>0-based position in the applied-layouts array.</summary>
        public int Position { get { return position; } }
        public DeviceKey Device { get { return device; } }
        public string Uuid { get { return uuid; } }
        public string Type { get { return type; } }
        public bool ShowSpacing { get { return showSpacing; } }
        public int Spacing { get { return spacing; } }
        public int ZoneCount { get { return zoneCount; } }
        public int SensitivityRadius { get { return sensitivityRadius; } }
    }

    /// <summary>Grid info of a custom layout. Array properties return copies.</summary>
    public sealed class GridLayoutInfo
    {
        private readonly int rows;
        private readonly int columns;
        private readonly int[] rowsPercentage;
        private readonly int[] columnsPercentage;
        private readonly int[][] cellChildMap;
        private readonly bool showSpacing;
        private readonly int spacing;
        private readonly int sensitivityRadius;

        public GridLayoutInfo(int rows, int columns, int[] rowsPercentage, int[] columnsPercentage, int[][] cellChildMap,
            bool showSpacing, int spacing, int sensitivityRadius)
        {
            if (rowsPercentage == null) throw new ArgumentNullException("rowsPercentage");
            if (columnsPercentage == null) throw new ArgumentNullException("columnsPercentage");
            if (cellChildMap == null) throw new ArgumentNullException("cellChildMap");
            this.rows = rows;
            this.columns = columns;
            this.rowsPercentage = (int[])rowsPercentage.Clone();
            this.columnsPercentage = (int[])columnsPercentage.Clone();
            this.cellChildMap = CopyMap(cellChildMap);
            this.showSpacing = showSpacing;
            this.spacing = spacing;
            this.sensitivityRadius = sensitivityRadius;
        }

        public int Rows { get { return rows; } }
        public int Columns { get { return columns; } }
        public int[] RowsPercentage { get { return (int[])rowsPercentage.Clone(); } }
        public int[] ColumnsPercentage { get { return (int[])columnsPercentage.Clone(); } }
        public int[][] CellChildMap { get { return CopyMap(cellChildMap); } }
        public bool ShowSpacing { get { return showSpacing; } }
        public int Spacing { get { return spacing; } }
        public int SensitivityRadius { get { return sensitivityRadius; } }

        /// <summary>The custom zone count: the largest cell-child-map value plus 1, or 0 for an empty map.</summary>
        public int ZoneCount
        {
            get
            {
                int max = -1;
                foreach (int[] row in cellChildMap)
                {
                    foreach (int value in row)
                    {
                        if (value > max) max = value;
                    }
                }
                return max + 1;
            }
        }

        internal static int[][] CopyMap(int[][] map)
        {
            int[][] copy = new int[map.Length][];
            for (int r = 0; r < map.Length; r++)
            {
                copy[r] = map[r] == null ? null : (int[])map[r].Clone();
            }
            return copy;
        }
    }

    /// <summary>One canvas zone in 96-DPI logical units relative to the canvas reference size.</summary>
    public sealed class CanvasZone
    {
        private readonly int x;
        private readonly int y;
        private readonly int width;
        private readonly int height;

        public CanvasZone(int x, int y, int width, int height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public int X { get { return x; } }
        public int Y { get { return y; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }
    }

    /// <summary>Canvas info of a custom layout. The zones array property returns a copy.</summary>
    public sealed class CanvasLayoutInfo
    {
        private readonly int refWidth;
        private readonly int refHeight;
        private readonly CanvasZone[] zones;
        private readonly int sensitivityRadius;

        public CanvasLayoutInfo(int refWidth, int refHeight, CanvasZone[] zones, int sensitivityRadius)
        {
            if (zones == null) throw new ArgumentNullException("zones");
            this.refWidth = refWidth;
            this.refHeight = refHeight;
            this.zones = (CanvasZone[])zones.Clone();
            this.sensitivityRadius = sensitivityRadius;
        }

        public int RefWidth { get { return refWidth; } }
        public int RefHeight { get { return refHeight; } }
        public CanvasZone[] Zones { get { return (CanvasZone[])zones.Clone(); } }
        public int SensitivityRadius { get { return sensitivityRadius; } }
    }

    /// <summary>An accepted custom-layouts.json entry: exactly one of Grid and Canvas is non-null.</summary>
    public sealed class CustomLayout
    {
        private readonly int position;
        private readonly string uuid;
        private readonly string name;
        private readonly string type;
        private readonly GridLayoutInfo grid;
        private readonly CanvasLayoutInfo canvas;

        public CustomLayout(int position, string uuid, string name, GridLayoutInfo grid)
            : this(position, uuid, name, "grid", grid, null)
        {
        }

        public CustomLayout(int position, string uuid, string name, CanvasLayoutInfo canvas)
            : this(position, uuid, name, "canvas", null, canvas)
        {
        }

        private CustomLayout(int position, string uuid, string name, string type, GridLayoutInfo grid, CanvasLayoutInfo canvas)
        {
            this.position = position;
            this.uuid = uuid;
            this.name = name;
            this.type = type;
            this.grid = grid;
            this.canvas = canvas;
        }

        public int Position { get { return position; } }
        public string Uuid { get { return uuid; } }
        public string Name { get { return name; } }

        /// <summary>"grid" or "canvas".</summary>
        public string Type { get { return type; } }
        public GridLayoutInfo Grid { get { return grid; } }
        public CanvasLayoutInfo Canvas { get { return canvas; } }

        /// <summary>REQ-RES-002 custom zone count: max cell index + 1 (grid) or the zones-array length (canvas).</summary>
        public int ZoneCount { get { return grid != null ? grid.ZoneCount : canvas.Zones.Length; } }
    }

    /// <summary>An entry FancyZones' own parser would skip, kept so resolution can report why (plan.md §B.2).</summary>
    public sealed class RejectedEntry
    {
        private readonly int position;
        private readonly string uuid;
        private readonly DeviceKey device;
        private readonly LayoutReason reason;
        private readonly string detail;

        public RejectedEntry(int position, string uuid, DeviceKey device, LayoutReason reason, string detail)
        {
            this.position = position;
            this.uuid = uuid;
            this.device = device;
            this.reason = reason;
            this.detail = detail ?? string.Empty;
        }

        /// <summary>0-based position in the document array.</summary>
        public int Position { get { return position; } }

        /// <summary>The raw uuid string when present; null otherwise.</summary>
        public string Uuid { get { return uuid; } }

        /// <summary>The device key when readable (applied entries only); null otherwise.</summary>
        public DeviceKey Device { get { return device; } }

        /// <summary>ShapeMismatch or MalformedEntry.</summary>
        public LayoutReason Reason { get { return reason; } }
        public string Detail { get { return detail; } }
    }

    /// <summary>Type, zone-count, show-spacing and spacing of an applied template entry (rows, columns, grid, priority-grid).</summary>
    public sealed class TemplateDescriptor
    {
        private readonly string type;
        private readonly int zoneCount;
        private readonly bool showSpacing;
        private readonly int spacing;

        public TemplateDescriptor(string type, int zoneCount, bool showSpacing, int spacing)
        {
            this.type = type;
            this.zoneCount = zoneCount;
            this.showSpacing = showSpacing;
            this.spacing = spacing;
        }

        public string Type { get { return type; } }
        public int ZoneCount { get { return zoneCount; } }
        public bool ShowSpacing { get { return showSpacing; } }
        public int Spacing { get { return spacing; } }
    }

    /// <summary>Parsed custom-layouts.json. When IsValid is false, Reason is MalformedJson and both arrays are empty.</summary>
    public sealed class CustomLayoutsDocument
    {
        private readonly LayoutReason reason;
        private readonly string detail;
        private readonly CustomLayout[] layouts;
        private readonly RejectedEntry[] rejected;

        public CustomLayoutsDocument(LayoutReason reason, string detail, CustomLayout[] layouts, RejectedEntry[] rejected)
        {
            this.reason = reason;
            this.detail = detail ?? string.Empty;
            this.layouts = layouts == null ? new CustomLayout[0] : (CustomLayout[])layouts.Clone();
            this.rejected = rejected == null ? new RejectedEntry[0] : (RejectedEntry[])rejected.Clone();
        }

        public bool IsValid { get { return reason == LayoutReason.None; } }
        public LayoutReason Reason { get { return reason; } }
        public string Detail { get { return detail; } }
        public CustomLayout[] Layouts { get { return (CustomLayout[])layouts.Clone(); } }
        public RejectedEntry[] Rejected { get { return (RejectedEntry[])rejected.Clone(); } }
    }

    /// <summary>Parsed applied-layouts.json. When IsValid is false, Reason is MalformedJson and both arrays are empty.</summary>
    public sealed class AppliedLayoutsDocument
    {
        private readonly LayoutReason reason;
        private readonly string detail;
        private readonly AppliedLayoutEntry[] entries;
        private readonly RejectedEntry[] rejected;

        public AppliedLayoutsDocument(LayoutReason reason, string detail, AppliedLayoutEntry[] entries, RejectedEntry[] rejected)
        {
            this.reason = reason;
            this.detail = detail ?? string.Empty;
            this.entries = entries == null ? new AppliedLayoutEntry[0] : (AppliedLayoutEntry[])entries.Clone();
            this.rejected = rejected == null ? new RejectedEntry[0] : (RejectedEntry[])rejected.Clone();
        }

        public bool IsValid { get { return reason == LayoutReason.None; } }
        public LayoutReason Reason { get { return reason; } }
        public string Detail { get { return detail; } }
        public AppliedLayoutEntry[] Entries { get { return (AppliedLayoutEntry[])entries.Clone(); } }
        public RejectedEntry[] Rejected { get { return (RejectedEntry[])rejected.Clone(); } }
    }
}
