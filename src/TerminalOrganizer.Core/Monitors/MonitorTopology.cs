using System;
using System.Collections.Generic;
using System.Globalization;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Monitors
{
    public sealed class MonitorKey
    {
        public MonitorKey(string serial, string interfacePath, string monitorId, string instance)
        {
            Serial = serial; InterfacePath = interfacePath; MonitorId = monitorId; Instance = instance;
            CanonicalValue = !string.IsNullOrEmpty(serial) ? "serial:" + Part(serial) + Part(monitorId) + Part(instance)
                : !string.IsNullOrEmpty(interfacePath) ? "path:" + Part(interfacePath)
                : !string.IsNullOrEmpty(monitorId) && !string.IsNullOrEmpty(instance) ? "instance:" + Part(monitorId) + Part(instance) : null;
        }
        public string Serial { get; private set; }
        public string InterfacePath { get; private set; }
        public string MonitorId { get; private set; }
        public string Instance { get; private set; }
        public string CanonicalValue { get; private set; }
        private static string Part(string value)
        {
            return value == null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }
        public bool EqualsKey(MonitorKey other)
        {
            return other != null && CanonicalValue != null && string.Equals(CanonicalValue, other.CanonicalValue, StringComparison.Ordinal);
        }
    }

    public static class MonitorSelector
    {
        public static MonitorInfo ResolveUnique(MonitorInfo[] monitors, MonitorKey key)
        {
            if (key == null || key.CanonicalValue == null) return null;
            MonitorInfo result = null;
            foreach (MonitorInfo monitor in monitors ?? new MonitorInfo[0])
                if (monitor != null && key.EqualsKey(monitor.StableKey))
                {
                    if (result != null) return null;
                    result = monitor;
                }
            return result;
        }
    }

    public sealed class MonitorTopologyEntry
    {
        public MonitorTopologyEntry(string monitorKey, bool primary, int monitorLeft, int monitorTop, int monitorWidth, int monitorHeight, int workLeft, int workTop, int workWidth, int workHeight, int dpi)
        {
            MonitorKey = monitorKey;
            Primary = primary;
            MonitorLeft = monitorLeft;
            MonitorTop = monitorTop;
            MonitorWidth = monitorWidth;
            MonitorHeight = monitorHeight;
            WorkLeft = workLeft;
            WorkTop = workTop;
            WorkWidth = workWidth;
            WorkHeight = workHeight;
            Dpi = dpi;
        }
        public string MonitorKey { get; private set; }
        public bool Primary { get; private set; }
        public int MonitorLeft { get; private set; }
        public int MonitorTop { get; private set; }
        public int MonitorWidth { get; private set; }
        public int MonitorHeight { get; private set; }
        public int WorkLeft { get; private set; }
        public int WorkTop { get; private set; }
        public int WorkWidth { get; private set; }
        public int WorkHeight { get; private set; }
        public int Dpi { get; private set; }
        internal bool Same(MonitorTopologyEntry other)
        {
            return other != null && string.Equals(MonitorKey, other.MonitorKey, StringComparison.Ordinal)
                && Primary == other.Primary
                && MonitorLeft == other.MonitorLeft
                && MonitorTop == other.MonitorTop
                && MonitorWidth == other.MonitorWidth
                && MonitorHeight == other.MonitorHeight
                && WorkLeft == other.WorkLeft
                && WorkTop == other.WorkTop
                && WorkWidth == other.WorkWidth
                && WorkHeight == other.WorkHeight
                && Dpi == other.Dpi;
        }
    }

    public sealed class MonitorTopologySignature
    {
        private readonly MonitorTopologyEntry[] entries;
        public MonitorTopologySignature(MonitorTopologyEntry[] entries)
        {
            this.entries = entries == null ? new MonitorTopologyEntry[0] : (MonitorTopologyEntry[])entries.Clone();
            Array.Sort(this.entries, delegate(MonitorTopologyEntry a, MonitorTopologyEntry b) { return string.CompareOrdinal(a == null ? null : a.MonitorKey, b == null ? null : b.MonitorKey); });
        }
        public MonitorTopologyEntry[] Entries { get { return (MonitorTopologyEntry[])entries.Clone(); } }
        public bool EqualsSignature(MonitorTopologySignature other)
        {
            if (other == null || entries.Length == 0 || entries.Length != other.entries.Length) return false;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] == null || string.IsNullOrEmpty(entries[i].MonitorKey) || !entries[i].Same(other.entries[i])
                    || (i > 0 && string.Equals(entries[i-1].MonitorKey, entries[i].MonitorKey, StringComparison.Ordinal))) return false;
            return true;
        }
        public static MonitorTopologySignature Capture(MonitorInfo[] monitors)
        {
            List<MonitorTopologyEntry> result = new List<MonitorTopologyEntry>();
            foreach (MonitorInfo m in monitors ?? new MonitorInfo[0])
            {
                if (m == null || m.WorkArea == null) { result.Add(null); continue; }
                WorkArea w = m.WorkArea;
                result.Add(new MonitorTopologyEntry(m.StableKey.CanonicalValue, m.Primary,
                    m.MonitorLeft, m.MonitorTop, m.MonitorWidth, m.MonitorHeight, w.Left, w.Top, w.Width, w.Height, w.Dpi));
            }
            return new MonitorTopologySignature(result.ToArray());
        }
    }

    public sealed class LayoutSignature
    {
        private readonly string monitorKey, desktopId, layoutType;
        private readonly LayoutResultKind kind;
        private readonly Zone[] zones;
        public LayoutSignature(string selectedMonitorKey, string currentDesktopId, LayoutResultKind resultKind, string layoutType, Zone[] zones)
        {
            monitorKey = selectedMonitorKey; desktopId = currentDesktopId; kind = resultKind; this.layoutType = layoutType;
            this.zones = zones == null ? new Zone[0] : (Zone[])zones.Clone();
            Array.Sort(this.zones, delegate(Zone a, Zone b) { return a.Id.CompareTo(b.Id); });
        }
        public bool EqualsSignature(LayoutSignature other)
        {
            if (other == null || string.IsNullOrEmpty(monitorKey) || !string.Equals(monitorKey, other.monitorKey, StringComparison.Ordinal)
                || !string.Equals(desktopId, other.desktopId, StringComparison.Ordinal) || kind != other.kind
                || !string.Equals(layoutType, other.layoutType, StringComparison.Ordinal) || zones.Length != other.zones.Length) return false;
            for (int i = 0; i < zones.Length; i++)
            {
                Zone a = zones[i], b = other.zones[i];
                if (a.Id != b.Id || a.Left != b.Left || a.Top != b.Top || a.Width != b.Width || a.Height != b.Height) return false;
            }
            return true;
        }
    }

    public sealed class CommitSignature
    {
        public CommitSignature(MonitorTopologySignature topology, LayoutSignature layout) { Topology = topology; Layout = layout; }
        public MonitorTopologySignature Topology { get; private set; }
        public LayoutSignature Layout { get; private set; }
        public bool EqualsSignature(CommitSignature other)
        {
            return other != null && Topology != null && Layout != null && Topology.EqualsSignature(other.Topology) && Layout.EqualsSignature(other.Layout);
        }
    }

    public delegate CommitSignature CommitSignatureSource();
    public sealed class CommitGuard
    {
        private readonly CommitSignature expected;
        private readonly CommitSignatureSource current;
        public CommitGuard(CommitSignature expected, CommitSignatureSource current) { this.expected = expected; this.current = current; }
        public string LastMismatch { get; private set; }
        // @MX:NOTE: once invalidated, this operation stays aborted even if topology returns.
        public bool IsCurrent()
        {
            if (LastMismatch != null) return false;
            try
            {
                if (expected != null && current != null && expected.EqualsSignature(current())) return true;
                LastMismatch = "monitor topology, desktop or layout changed";
            }
            catch (Exception ex) { LastMismatch = ex.Message; }
            return false;
        }
    }
}
