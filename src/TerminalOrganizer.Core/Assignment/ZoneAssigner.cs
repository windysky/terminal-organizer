using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Geometry;

namespace TerminalOrganizer.Core.Assignment
{
    /// <summary>
    /// Computes the assignment plan (REQ-ASG-001..005). Pure: no Win32 calls, inputs are
    /// never mutated, the same inputs give the same plan (NFR-2, explicit deterministic
    /// comparisons). Zones are ordered by ZoneOrdering.Order with the default tolerance
    /// (SPEC-LAYOUT-001); the manager window, when present and identified, takes the first
    /// zone alone (spec A2; a multi-tab manager host is pinned like any manager); the
    /// exact normal-state occupants retain their zones and stacks before unmatched windows
    /// fill the remaining zones by current position — top edge, then left
    /// edge, ties broken by window id compared ordinally (D-A) — and overflow windows stack
    /// in the last zone of fill order, keeping their relative order (D-E; for a one-zone
    /// layout that single zone is the stack zone, D-B).
    /// </summary>
    public static class ZoneAssigner
    {
        // @MX:ANCHOR: [AUTO] The placement contract consumed by the tray app (SPEC-TRAY-007) and the overflow engine (SPEC-OVERFLOW-006).
        // @MX:REASON: Zone-to-window mapping, stacking and no-move semantics all live behind this call.
        public static AssignmentPlan Assign(Zone[] zones, WindowFact[] windows, string managerWindowName)
        {
            if (zones == null) throw new ArgumentNullException("zones");
            if (zones.Length == 0) throw new ArgumentException("at least one zone is required", "zones");

            List<WindowFact> facts = new List<WindowFact>();
            List<WindowFact> fullScreen = new List<WindowFact>();
            PartitionWindows(windows, facts, fullScreen);

            string skippedReason;
            WindowFact manager = FindManager(facts, managerWindowName, out skippedReason);
            if (manager == null && skippedReason == "window not found")
            {
                foreach (WindowFact window in fullScreen)
                    if (string.Equals(window.Name, managerWindowName, StringComparison.Ordinal))
                        skippedReason = window.MutationStateTrusted ? "window full screen" : "window state untrusted";
            }
            return AssignWithManager(zones, facts, fullScreen, manager, skippedReason);
        }

        /// <summary>
        /// B2 canonical-manager entry point: pins the window the ManagerResolution
        /// resolved and carries every non-resolved status as its REQ-ASG-004 skip reason
        /// (ambiguous is never a first match — the resolver already decided that).
        /// </summary>
        public static AssignmentPlan Assign(Zone[] zones, WindowFact[] windows, ManagerResolution manager)
        {
            if (zones == null) throw new ArgumentNullException("zones");
            if (zones.Length == 0) throw new ArgumentException("at least one zone is required", "zones");

            List<WindowFact> facts = new List<WindowFact>();
            List<WindowFact> fullScreen = new List<WindowFact>();
            PartitionWindows(windows, facts, fullScreen);

            WindowFact resolved = null;
            string skippedReason;
            if (manager == null || manager.Status == ManagerResolutionStatus.None)
            {
                skippedReason = "no choice";
            }
            else if (manager.Status == ManagerResolutionStatus.Resolved)
            {
                resolved = FindById(facts, manager.WindowId);
                if (resolved == null)
                {
                    skippedReason = "window not found";
                }
                else if (!resolved.Identified)
                {
                    skippedReason = "window unidentified";
                    resolved = null;
                }
                else
                {
                    skippedReason = null;
                }
            }
            else if (manager.Status == ManagerResolutionStatus.Ambiguous)
            {
                skippedReason = "ambiguous manager name";
            }
            else if (manager.Status == ManagerResolutionStatus.Unidentified)
            {
                skippedReason = "window unidentified";
            }
            else if (manager.Status == ManagerResolutionStatus.FullScreen)
            {
                skippedReason = "window full screen";
            }
            else
            {
                skippedReason = "window not found";
            }
            return AssignWithManager(zones, facts, fullScreen, resolved, skippedReason);
        }

        /// <summary>The shared fill body (B2 refactor): pin the manager when present, then the exact-ownership and positional fill.</summary>
        private static AssignmentPlan AssignWithManager(Zone[] zones, List<WindowFact> facts,
            List<WindowFact> fullScreen, WindowFact manager, string skippedReason)
        {
            int[] order = ZoneOrdering.Order(zones);
            Zone[] orderedZones = ResolveOrderedZones(zones, order);

            int firstFillIndex = 0;
            if (manager != null)
            {
                facts.Remove(manager);
                firstFillIndex = 1;
            }

            List<PlannedMove> moves = new List<PlannedMove>();
            if (manager != null)
            {
                moves.Add(PlanMove(manager, orderedZones[0], false));
            }
            // @MX:NOTE: exact ownership precedes positional fill; every member of an existing stack stays.
            HashSet<int> occupied = new HashSet<int>();
            for (int z = firstFillIndex; z < orderedZones.Length; z++)
            {
                Zone zone = orderedZones[z];
                List<WindowFact> exact = facts.FindAll(delegate(WindowFact fact) { return fact.StateIsNormal && RectEqualsZone(fact, zone); });
                exact.Sort(delegate(WindowFact a, WindowFact b) { return string.CompareOrdinal(a.Id, b.Id); });
                for (int i = 0; i < exact.Count; i++)
                {
                    moves.Add(PlanMove(exact[i], zone, i > 0));
                    facts.Remove(exact[i]);
                }
                if (exact.Count > 0) occupied.Add(zone.Id);
            }
            facts.Sort(new Comparison<WindowFact>(CompareByPosition));
            int nextZone = firstFillIndex;
            foreach (WindowFact fact in facts)
            {
                while (nextZone < orderedZones.Length && occupied.Contains(orderedZones[nextZone].Id)) nextZone++;
                bool stacked = nextZone >= orderedZones.Length;
                Zone zone = orderedZones[stacked ? orderedZones.Length - 1 : nextZone++];
                moves.Add(PlanMove(fact, zone, stacked));
            }
            fullScreen.Sort(new Comparison<WindowFact>(CompareByPosition));
            foreach (WindowFact fact in fullScreen)
            {
                moves.Add(new PlannedMove(fact.Id, fact.Handle, PlannedMove.NoZoneId,
                    fact.Left, fact.Top, fact.Width, fact.Height, false, false, false,
                    fact.MutationStateTrusted ? PlannedMoveSkipReason.FullScreen : PlannedMoveSkipReason.UntrustedState));
            }

            return new AssignmentPlan(moves.ToArray(), manager != null,
                manager == null ? null : manager.Id, skippedReason);
        }

        /// <summary>
        /// Resolves the manager window (spec A2): the first window whose name equals the
        /// persisted choice by exact ordinal equality, and only when it is identified. Fills
        /// skippedReason with the REQ-ASG-004 reason and returns null when the pin is skipped.
        /// </summary>
        private static WindowFact FindManager(List<WindowFact> facts, string managerWindowName, out string skippedReason)
        {
            skippedReason = null;
            if (string.IsNullOrEmpty(managerWindowName))
            {
                skippedReason = "no choice";
                return null;
            }
            WindowFact candidate = null;
            foreach (WindowFact fact in facts)
            {
                if (string.Equals(fact.Name, managerWindowName, StringComparison.Ordinal))
                {
                    if (candidate != null)
                    {
                        skippedReason = "ambiguous manager name";
                        return null;
                    }
                    candidate = fact;
                }
            }
            if (candidate != null)
            {
                if (candidate.Identified) return candidate;
                skippedReason = "window unidentified";
                return null;
            }
            skippedReason = "window not found";
            return null;
        }

        /// <summary>Splits the input windows into the movable set and the full-screen/untrusted set.</summary>
        private static void PartitionWindows(WindowFact[] windows, List<WindowFact> facts, List<WindowFact> fullScreen)
        {
            if (windows != null)
            {
                foreach (WindowFact window in windows)
                {
                    if (window != null)
                    {
                        if (!window.MayMove) fullScreen.Add(window);
                        else facts.Add(window);
                    }
                }
            }
        }

        /// <summary>One window by its id (the ManagerResolution contract), inside the movable set.</summary>
        private static WindowFact FindById(List<WindowFact> facts, string windowId)
        {
            if (string.IsNullOrEmpty(windowId))
            {
                return null;
            }
            foreach (WindowFact fact in facts)
            {
                if (fact != null && string.Equals(fact.Id, windowId, StringComparison.Ordinal))
                {
                    return fact;
                }
            }
            return null;
        }

        /// <summary>D-A position order: top edge, then left edge, ties by window id compared ordinally.</summary>
        private static int CompareByPosition(WindowFact a, WindowFact b)
        {
            if (a.Top != b.Top)
            {
                return a.Top < b.Top ? -1 : 1;
            }
            if (a.Left != b.Left)
            {
                return a.Left < b.Left ? -1 : 1;
            }
            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// Builds the per-window move (REQ-ASG-005): the target zone and rect with the
        /// no-move / restore-first / full-screen decisions derived from the fact's state.
        /// </summary>
        private static PlannedMove PlanMove(WindowFact window, Zone zone, bool stacked)
        {
            bool moveRequired = true;
            bool restoreFirst = false;
            restoreFirst = window.Maximized || window.Minimized;
            if (window.StateIsNormal && RectEqualsZone(window, zone)) moveRequired = false;
            return new PlannedMove(window.Id, window.Handle, zone.Id,
                zone.Left, zone.Top, zone.Width, zone.Height,
                moveRequired, restoreFirst, stacked, false);
        }

        /// <summary>D-F exact-rect equality: left, top, width and height all equal; no fuzzy checks.</summary>
        private static bool RectEqualsZone(WindowFact window, Zone zone)
        {
            return window.Left == zone.Left && window.Top == zone.Top
                && window.Width == zone.Width && window.Height == zone.Height;
        }

        /// <summary>Maps the ZoneOrdering order (zone ids) back to zone objects; ids always come from the input set.</summary>
        private static Zone[] ResolveOrderedZones(Zone[] zones, int[] order)
        {
            Zone[] ordered = new Zone[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                foreach (Zone zone in zones)
                {
                    if (zone.Id == order[i])
                    {
                        ordered[i] = zone;
                        break;
                    }
                }
            }
            return ordered;
        }
    }
}
