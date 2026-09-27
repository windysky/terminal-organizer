using System;
using System.Collections.Generic;
using System.Globalization;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Monitors;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// One monitor's captured layout for cross-monitor planning (C2): its stable key,
    /// the physical display label, and the zone rects in absolute screen coordinates
    /// (the LayoutResolver work-area offset is already applied). The monitor position
    /// used for destination ordering is the minimum zone top/left — the zones tile from
    /// the work-area origin, so it tracks the monitor's place in the virtual screen.
    /// Immutable; Zones returns a copy.
    /// </summary>
    public sealed class MonitorLayoutSnapshot
    {
        private readonly MonitorKey monitorKey;
        private readonly string physicalLabel;
        private readonly Zone[] zones;

        public MonitorLayoutSnapshot(MonitorKey monitorKey, string physicalLabel, Zone[] zones)
        {
            this.monitorKey = monitorKey;
            this.physicalLabel = physicalLabel;
            this.zones = zones == null ? new Zone[0] : (Zone[])zones.Clone();
        }

        public MonitorKey MonitorKey { get { return monitorKey; } }
        public string PhysicalLabel { get { return physicalLabel; } }
        public Zone[] Zones { get { return (Zone[])zones.Clone(); } }

        /// <summary>The monitor's top edge proxy: the smallest zone top (0 for no zones).</summary>
        public int Top
        {
            get
            {
                int value = 0;
                bool seen = false;
                foreach (Zone zone in zones)
                {
                    if (zone == null) continue;
                    if (!seen || zone.Top < value) value = zone.Top;
                    seen = true;
                }
                return seen ? value : 0;
            }
        }

        /// <summary>The monitor's left edge proxy: the smallest zone left (0 for no zones).</summary>
        public int Left
        {
            get
            {
                int value = 0;
                bool seen = false;
                foreach (Zone zone in zones)
                {
                    if (zone == null) continue;
                    if (!seen || zone.Left < value) value = zone.Left;
                    seen = true;
                }
                return seen ? value : 0;
            }
        }
    }

    /// <summary>
    /// One window's captured cross-monitor inputs (C2): pure data captured before
    /// planning — identity and rect, the trust flags acquired by discovery (A3), the
    /// A2 stable/overflow classification from the local assignment, the zone it
    /// currently occupies, and the C1 resolved priority. Immutable.
    /// </summary>
    public sealed class CrossMonitorWindow
    {
        private readonly string windowId;
        private readonly IntPtr handle;
        private readonly WindowIdentity identity;
        private readonly MonitorKey monitorKey;
        private readonly bool currentDesktopVerified;
        private readonly bool mutationStateTrusted;
        private readonly bool fullScreen;
        private readonly bool manager;
        private readonly bool stableOccupant;
        private readonly bool overflow;
        private readonly int currentZoneId;
        private readonly int left;
        private readonly int top;
        private readonly int width;
        private readonly int height;
        private readonly ResolvedPriority priority;

        public CrossMonitorWindow(string windowId, IntPtr handle, WindowIdentity identity,
            MonitorKey monitorKey, bool currentDesktopVerified, bool mutationStateTrusted,
            bool fullScreen, bool manager, bool stableOccupant, bool overflow, int currentZoneId,
            int left, int top, int width, int height, ResolvedPriority priority)
        {
            this.windowId = windowId;
            this.handle = handle;
            this.identity = identity;
            this.monitorKey = monitorKey;
            this.currentDesktopVerified = currentDesktopVerified;
            this.mutationStateTrusted = mutationStateTrusted;
            this.fullScreen = fullScreen;
            this.manager = manager;
            this.stableOccupant = stableOccupant;
            this.overflow = overflow;
            this.currentZoneId = currentZoneId;
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
            this.priority = priority;
        }

        public string WindowId { get { return windowId; } }
        public IntPtr Handle { get { return handle; } }
        public WindowIdentity Identity { get { return identity; } }
        public MonitorKey MonitorKey { get { return monitorKey; } }
        public bool CurrentDesktopVerified { get { return currentDesktopVerified; } }
        public bool MutationStateTrusted { get { return mutationStateTrusted; } }
        public bool FullScreen { get { return fullScreen; } }
        public bool Manager { get { return manager; } }
        public bool StableOccupant { get { return stableOccupant; } }
        public bool Overflow { get { return overflow; } }

        /// <summary>The zone the window currently occupies; -1 when it occupies none.</summary>
        public int CurrentZoneId { get { return currentZoneId; } }

        public int Left { get { return left; } }
        public int Top { get { return top; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        /// <summary>The C1 resolved priority; null marks an unresolvable window.</summary>
        public ResolvedPriority Priority { get { return priority; } }
    }

    /// <summary>
    /// The whole captured planning world (C2): the current desktop id and every
    /// monitor layout and window the planner may consider. Desktop identity is carried
    /// for the commit signature and never inferred from a monitor. Immutable; arrays
    /// return copies.
    /// </summary>
    public sealed class CrossMonitorSnapshot
    {
        private readonly string currentDesktopId;
        private readonly MonitorLayoutSnapshot[] monitors;
        private readonly CrossMonitorWindow[] windows;

        public CrossMonitorSnapshot(string currentDesktopId,
            MonitorLayoutSnapshot[] monitors, CrossMonitorWindow[] windows)
        {
            this.currentDesktopId = currentDesktopId;
            this.monitors = monitors == null ? new MonitorLayoutSnapshot[0] : (MonitorLayoutSnapshot[])monitors.Clone();
            this.windows = windows == null ? new CrossMonitorWindow[0] : (CrossMonitorWindow[])windows.Clone();
        }

        public string CurrentDesktopId { get { return currentDesktopId; } }
        public MonitorLayoutSnapshot[] Monitors { get { return (MonitorLayoutSnapshot[])monitors.Clone(); } }
        public CrossMonitorWindow[] Windows { get { return (CrossMonitorWindow[])windows.Clone(); } }
    }

    /// <summary>
    /// One planned cross-monitor move (C2): the source window, the destination monitor
    /// and zone with its absolute target rect, and the short priority reason. The rect
    /// is exactly the destination zone, so an applied move makes the window a stable
    /// occupant of that zone on the next acquisition. Immutable.
    /// </summary>
    public sealed class CrossMonitorMove
    {
        private readonly string windowId;
        private readonly IntPtr handle;
        private readonly string sourceMonitorKey;
        private readonly string sourceMonitorLabel;
        private readonly string destinationMonitorKey;
        private readonly string destinationMonitorLabel;
        private readonly int destinationZoneId;
        private readonly int targetLeft;
        private readonly int targetTop;
        private readonly int targetWidth;
        private readonly int targetHeight;
        private readonly string priorityReason;

        public CrossMonitorMove(string windowId, IntPtr handle,
            string sourceMonitorKey, string sourceMonitorLabel,
            string destinationMonitorKey, string destinationMonitorLabel,
            int destinationZoneId, int targetLeft, int targetTop, int targetWidth, int targetHeight,
            string priorityReason)
        {
            this.windowId = windowId;
            this.handle = handle;
            this.sourceMonitorKey = sourceMonitorKey;
            this.sourceMonitorLabel = sourceMonitorLabel;
            this.destinationMonitorKey = destinationMonitorKey;
            this.destinationMonitorLabel = destinationMonitorLabel;
            this.destinationZoneId = destinationZoneId;
            this.targetLeft = targetLeft;
            this.targetTop = targetTop;
            this.targetWidth = targetWidth;
            this.targetHeight = targetHeight;
            this.priorityReason = priorityReason;
        }

        public string WindowId { get { return windowId; } }
        public IntPtr Handle { get { return handle; } }
        public string SourceMonitorKey { get { return sourceMonitorKey; } }
        public string SourceMonitorLabel { get { return sourceMonitorLabel; } }
        public string DestinationMonitorKey { get { return destinationMonitorKey; } }
        public string DestinationMonitorLabel { get { return destinationMonitorLabel; } }
        public int DestinationZoneId { get { return destinationZoneId; } }
        public int TargetLeft { get { return targetLeft; } }
        public int TargetTop { get { return targetTop; } }
        public int TargetWidth { get { return targetWidth; } }
        public int TargetHeight { get { return targetHeight; } }

        /// <summary>Short deterministic reason, e.g. "declared rank 8".</summary>
        public string PriorityReason { get { return priorityReason; } }

        /// <summary>
        /// The pinned two-line -WhatIf text (C2 design): source and destination labels,
        /// then the destination zone rect and the priority reason.
        /// </summary>
        public string ToWhatIfText()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "redistribute: {0} | {1} -> {2}\n| zone {3} @ {4},{5} {6}x{7} | {8}",
                windowId, sourceMonitorLabel, destinationMonitorLabel, destinationZoneId,
                targetLeft, targetTop, targetWidth, targetHeight,
                priorityReason == null ? string.Empty : priorityReason);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0}->{1}:z{2}@{3},{4} {5}x{6}|{7}",
                windowId, destinationMonitorKey, destinationZoneId,
                targetLeft, targetTop, targetWidth, targetHeight, priorityReason);
        }
    }

    /// <summary>
    /// The pure redistribution result (C2): the planned moves in source-priority order
    /// and the overflow window ids retained on their local stack (no free zone
    /// elsewhere), sorted ordinally. Immutable; arrays return copies.
    /// </summary>
    public sealed class CrossMonitorPlan
    {
        private readonly CrossMonitorMove[] moves;
        private readonly string[] unchangedOverflowWindowIds;

        public CrossMonitorPlan(CrossMonitorMove[] moves, string[] unchangedOverflowWindowIds)
        {
            this.moves = moves == null ? new CrossMonitorMove[0] : (CrossMonitorMove[])moves.Clone();
            this.unchangedOverflowWindowIds = unchangedOverflowWindowIds == null
                ? new string[0] : (string[])unchangedOverflowWindowIds.Clone();
        }

        public CrossMonitorMove[] Moves { get { return (CrossMonitorMove[])moves.Clone(); } }
        public string[] UnchangedOverflowWindowIds { get { return (string[])unchangedOverflowWindowIds.Clone(); } }
        public bool HasMoves { get { return moves.Length > 0; } }
    }

    /// <summary>
    /// The pure cross-monitor redistribution planner (C2, night-design-2026-09-25):
    /// moves only movable overflow windows into genuinely free zones on OTHER
    /// current-desktop monitors, deterministically and idempotently. Pure — no monitor
    /// enumeration, no Win32 call, no I/O; every input is captured in the snapshot.
    /// The planner never displaces an occupant to improve priority, and it computes
    /// but never applies: C3 selects the policy that consumes this plan.
    /// </summary>
    public static class CrossMonitorPlanner
    {
        // @MX:ANCHOR: [AUTO] C2 planning entry — consumed by the WhatIf tools, the OrganizePlan composition and the C3 choice flow.
        // @MX:REASON: every cross-monitor decision passes through this one pure function; a divergent copy would move different windows per caller.
        public static CrossMonitorPlan Plan(CrossMonitorSnapshot snapshot)
        {
            List<PlannerMonitor> monitors = BuildMonitors(snapshot == null ? null : snapshot.Monitors);
            List<CrossMonitorMove> moves = new List<CrossMonitorMove>();
            List<string> unchanged = new List<string>();
            if (monitors.Count == 0)
            {
                return new CrossMonitorPlan(moves.ToArray(), unchanged.ToArray());
            }

            // Occupied zones per monitor (by sorted index): stable occupants and managers
            // mark their zone, and any current-desktop trusted window whose normal rect
            // exactly equals a zone marks that zone. Stacked occupants share one zone,
            // marked once by the set. A full-screen window is outside the zone model and
            // never marks individual zones.
            HashSet<int>[] occupied = new HashSet<int>[monitors.Count];
            for (int i = 0; i < occupied.Length; i++)
            {
                occupied[i] = new HashSet<int>();
            }
            foreach (CrossMonitorWindow window in snapshot.Windows)
            {
                MarkOccupancy(window, monitors, occupied);
            }

            // Sources: movable overflow only. Every exclusion is a captured flag —
            // manager, full-screen, unverified desktop, untrusted state, stable occupant,
            // invalid identity/handle — plus overflow itself and a resolvable priority.
            // A window whose monitor is not in the snapshot is outside this planner's
            // world and is not a candidate.
            List<CrossMonitorWindow> sources = new List<CrossMonitorWindow>();
            foreach (CrossMonitorWindow window in snapshot.Windows)
            {
                if (!IsCandidateSource(window, monitors))
                {
                    continue;
                }
                sources.Add(window);
            }
            sources.Sort(delegate(CrossMonitorWindow x, CrossMonitorWindow y)
            {
                return PriorityResolver.CompareForRedistribution(x.Priority, y.Priority);
            });

            // For each source (lowest priority first): the first free zone on the FIRST
            // different monitor in (top, left, stable key) order; reserve and continue.
            // No free zone anywhere else: the source keeps its local stack.
            HashSet<int>[] reserved = new HashSet<int>[monitors.Count];
            for (int i = 0; i < reserved.Length; i++)
            {
                reserved[i] = new HashSet<int>();
            }
            foreach (CrossMonitorWindow source in sources)
            {
                int sourceIndex = IndexOfMonitor(monitors, source.MonitorKey);
                CrossMonitorMove move = null;
                for (int m = 0; m < monitors.Count && move == null; m++)
                {
                    if (m == sourceIndex)
                    {
                        continue;
                    }
                    foreach (Zone zone in monitors[m].OrderedZones)
                    {
                        if (occupied[m].Contains(zone.Id) || reserved[m].Contains(zone.Id))
                        {
                            continue;
                        }
                        PlannerMonitor destination = monitors[m];
                        PlannerMonitor home = monitors[sourceIndex];
                        move = new CrossMonitorMove(source.WindowId, source.Handle,
                            source.MonitorKey.CanonicalValue, home.Layout.PhysicalLabel,
                            destination.Layout.MonitorKey.CanonicalValue, destination.Layout.PhysicalLabel,
                            zone.Id, zone.Left, zone.Top, zone.Width, zone.Height,
                            PriorityReason(source.Priority));
                        reserved[m].Add(zone.Id);
                        break;
                    }
                }
                if (move != null)
                {
                    moves.Add(move);
                }
                else
                {
                    unchanged.Add(source.WindowId);
                }
            }
            unchanged.Sort(delegate(string x, string y)
            {
                return string.CompareOrdinal(x, y);
            });
            return new CrossMonitorPlan(moves.ToArray(), unchanged.ToArray());
        }

        /// <summary>
        /// The destination monitors in pinned order — top, left, then stable key ordinal —
        /// with their zones normalized through ZoneOrdering (input order never leaks in).
        /// Duplicate stable keys keep the first entry.
        /// </summary>
        private static List<PlannerMonitor> BuildMonitors(MonitorLayoutSnapshot[] layouts)
        {
            List<PlannerMonitor> monitors = new List<PlannerMonitor>();
            foreach (MonitorLayoutSnapshot layout in layouts ?? new MonitorLayoutSnapshot[0])
            {
                if (layout == null || layout.MonitorKey == null
                    || string.IsNullOrEmpty(layout.MonitorKey.CanonicalValue))
                {
                    continue;
                }
                Zone[] zones = layout.Zones;
                if (zones.Length == 0)
                {
                    continue;
                }
                monitors.Add(new PlannerMonitor(layout, ResolveOrderedZones(zones, ZoneOrdering.Order(zones))));
            }
            monitors.Sort(delegate(PlannerMonitor x, PlannerMonitor y)
            {
                if (x.Layout.Top != y.Layout.Top)
                {
                    return x.Layout.Top.CompareTo(y.Layout.Top);
                }
                if (x.Layout.Left != y.Layout.Left)
                {
                    return x.Layout.Left.CompareTo(y.Layout.Left);
                }
                return string.CompareOrdinal(x.Layout.MonitorKey.CanonicalValue, y.Layout.MonitorKey.CanonicalValue);
            });
            List<PlannerMonitor> unique = new List<PlannerMonitor>();
            foreach (PlannerMonitor monitor in monitors)
            {
                if (unique.Count > 0 && string.Equals(
                    unique[unique.Count - 1].Layout.MonitorKey.CanonicalValue,
                    monitor.Layout.MonitorKey.CanonicalValue, StringComparison.Ordinal))
                {
                    continue;
                }
                unique.Add(monitor);
            }
            return unique;
        }

        private static void MarkOccupancy(CrossMonitorWindow window, List<PlannerMonitor> monitors, HashSet<int>[] occupied)
        {
            if (window == null || window.MonitorKey == null)
            {
                return;
            }
            // Only current-desktop, trusted-state windows occupy zones.
            if (!window.CurrentDesktopVerified || !window.MutationStateTrusted)
            {
                return;
            }
            // A full-screen window is outside the zone model: it never marks zones.
            if (window.FullScreen)
            {
                return;
            }
            int index = IndexOfMonitor(monitors, window.MonitorKey);
            if (index < 0)
            {
                return;
            }
            PlannerMonitor monitor = monitors[index];
            // A manager or stable occupant occupies its current zone; otherwise an exact
            // normal rect equal to a zone marks that zone (the ZoneOrdering iteration
            // keeps the choice independent of input order).
            if ((window.Manager || window.StableOccupant) && monitor.HasZone(window.CurrentZoneId))
            {
                occupied[index].Add(window.CurrentZoneId);
                return;
            }
            foreach (Zone zone in monitor.OrderedZones)
            {
                if (window.Left == zone.Left && window.Top == zone.Top
                    && window.Width == zone.Width && window.Height == zone.Height)
                {
                    occupied[index].Add(zone.Id);
                    return;
                }
            }
        }

        private static bool IsCandidateSource(CrossMonitorWindow window, List<PlannerMonitor> monitors)
        {
            if (window == null || !window.Overflow)
            {
                return false;
            }
            if (window.Manager || window.FullScreen || window.StableOccupant)
            {
                return false;
            }
            if (!window.CurrentDesktopVerified || !window.MutationStateTrusted)
            {
                return false;
            }
            if (window.Priority == null)
            {
                return false;
            }
            if (!IdentityValid(window))
            {
                return false;
            }
            return IndexOfMonitor(monitors, window.MonitorKey) >= 0;
        }

        /// <summary>The identity/handle guard: a live handle plus pid and start-time ticks.</summary>
        private static bool IdentityValid(CrossMonitorWindow window)
        {
            if (window == null || window.Handle == IntPtr.Zero)
            {
                return false;
            }
            WindowIdentity identity = window.Identity;
            return identity != null && identity.Handle != IntPtr.Zero
                && identity.ProcessId > 0 && identity.ProcessStartTimeUtcTicks > 0;
        }

        private static int IndexOfMonitor(List<PlannerMonitor> monitors, MonitorKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.CanonicalValue))
            {
                return -1;
            }
            for (int i = 0; i < monitors.Count; i++)
            {
                if (string.Equals(monitors[i].Layout.MonitorKey.CanonicalValue, key.CanonicalValue,
                    StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        private static string PriorityReason(ResolvedPriority priority)
        {
            if (priority == null)
            {
                return string.Empty;
            }
            string source = priority.Source == PrioritySource.Manual ? "manual"
                : priority.Source == PrioritySource.Declared ? "declared" : "derived";
            return source + " rank " + priority.Rank.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Maps a ZoneOrdering order (zone ids) back to zone objects in that order.</summary>
        private static Zone[] ResolveOrderedZones(Zone[] zones, int[] order)
        {
            Zone[] ordered = new Zone[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                foreach (Zone zone in zones)
                {
                    if (zone != null && zone.Id == order[i])
                    {
                        ordered[i] = zone;
                        break;
                    }
                }
            }
            return ordered;
        }

        /// <summary>One destination monitor with its ZoneOrdering-normalized zones.</summary>
        private sealed class PlannerMonitor
        {
            public readonly MonitorLayoutSnapshot Layout;
            public readonly Zone[] OrderedZones;

            public PlannerMonitor(MonitorLayoutSnapshot layout, Zone[] orderedZones)
            {
                this.Layout = layout;
                this.OrderedZones = orderedZones;
            }

            public bool HasZone(int zoneId)
            {
                foreach (Zone zone in OrderedZones)
                {
                    if (zone.Id == zoneId)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}
