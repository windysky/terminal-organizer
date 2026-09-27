using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// Computes the merge plan (REQ-MRG-001/002, spec D-2). Pure: no Win32 calls, inputs are
    /// never mutated, the same inputs give the same plan (NFR-2). For each zone with stacked
    /// windows, the merge target is the zone's FIRST non-manager occupant in the assignment
    /// plan's occupant order (fill order — SPEC-ZONE-005's stack order); the manager window is
    /// never a target, and when no non-manager occupant exists the zone plans no merges. When
    /// the base is not identified the zone is marked stack-only — merging into an unidentified
    /// window is never attempted. Sources are the subsequent occupants that are mergeable
    /// (single tab AND identified AND tmux-backed, per SPEC-WIN-004's Mergeable flag); windows
    /// in never-close classes (manager, multi-tab, unidentified, Windows-native) are never
    /// sources. The base is never itself a source.
    /// </summary>
    public static class MergePlanner
    {
        // @MX:ANCHOR: overflow contract — never-close rules live here (plan.md H).
        // @MX:REASON: every merge decision (target choice, source eligibility, stack-only fallback) flows from this call; a wrong plan closes the wrong window.
        public static MergePlan Plan(AssignmentPlan assignmentPlan, WindowSnapshot[] snapshots)
        {
            if (assignmentPlan == null)
            {
                throw new ArgumentNullException("assignmentPlan");
            }
            Dictionary<long, WindowSnapshot> byHandle = IndexSnapshots(snapshots);

            List<PlannedMerge> merges = new List<PlannedMerge>();
            List<int> stackOnlyZones = new List<int>();

            PlannedMove[] moves = assignmentPlan.Moves;
            List<int> zoneOrder = new List<int>();
            Dictionary<int, List<PlannedMove>> zones = new Dictionary<int, List<PlannedMove>>();
            for (int i = 0; i < moves.Length; i++)
            {
                PlannedMove move = moves[i];
                if (move == null || !move.OccupiesZone || move.FullScreenSkipped)
                {
                    continue;
                }
                List<PlannedMove> occupants;
                if (!zones.TryGetValue(move.ZoneId, out occupants))
                {
                    occupants = new List<PlannedMove>();
                    zones.Add(move.ZoneId, occupants);
                    zoneOrder.Add(move.ZoneId);
                }
                occupants.Add(move);
            }

            for (int z = 0; z < zoneOrder.Count; z++)
            {
                int zoneId = zoneOrder[z];
                List<PlannedMove> occupants = zones[zoneId];
                if (!HasStacked(occupants))
                {
                    continue;
                }
                PlannedMove target = FindTarget(occupants, assignmentPlan.ManagerWindowId);
                if (target == null)
                {
                    // One-zone layout with no non-manager occupant: no merges (D-2).
                    stackOnlyZones.Add(zoneId);
                    continue;
                }
                WindowSnapshot targetSnapshot = Lookup(byHandle, target.Handle);
                if (targetSnapshot == null || !targetSnapshot.Identified || !targetSnapshot.HasTrustedCompleteTabs
                    || targetSnapshot.Identity == null || !targetSnapshot.Identity.EqualsForMutation(targetSnapshot.Identity))
                {
                    // REQ-MRG-002: unidentified base -> stack-only, never merge into it.
                    stackOnlyZones.Add(zoneId);
                    continue;
                }
                for (int i = 0; i < occupants.Count; i++)
                {
                    if (ReferenceEquals(occupants[i], target) || occupants[i].WindowId == target.WindowId)
                    {
                        // Everything after the base is a source candidate; the base never is.
                        for (int s = i + 1; s < occupants.Count; s++)
                        {
                            PlannedMerge merge = TryPlanSource(occupants[s], assignmentPlan.ManagerWindowId, target, byHandle);
                            if (merge != null)
                            {
                                merges.Add(merge);
                            }
                        }
                        break;
                    }
                }
            }

            return new MergePlan(merges.ToArray(), stackOnlyZones.ToArray());
        }

        /// <summary>The zone's first non-manager occupant (D-2); the manager is never a merge target.</summary>
        private static PlannedMove FindTarget(List<PlannedMove> occupants, string managerWindowId)
        {
            for (int i = 0; i < occupants.Count; i++)
            {
                if (managerWindowId == null || occupants[i].WindowId != managerWindowId)
                {
                    return occupants[i];
                }
            }
            return null;
        }

        /// <summary>One source candidate: subsequent occupant, not the manager, with a mergeable
        /// single-tab snapshot carrying a matched session (REQ-MRG-001 never-close classes).</summary>
        private static PlannedMerge TryPlanSource(PlannedMove candidate, string managerWindowId,
            PlannedMove target, Dictionary<long, WindowSnapshot> byHandle)
        {
            if (candidate == null || candidate.FullScreenSkipped || target.FullScreenSkipped)
            {
                return null;
            }
            if (managerWindowId != null && candidate.WindowId == managerWindowId)
            {
                return null;
            }
            WindowSnapshot snapshot = Lookup(byHandle, candidate.Handle);
            if (snapshot == null || !snapshot.Mergeable || !snapshot.HasTrustedCompleteTabs
                || snapshot.Identity == null || !snapshot.Identity.EqualsForMutation(snapshot.Identity))
            {
                return null;
            }
            TabSnapshot[] tabs = snapshot.Tabs;
            if (tabs.Length != 1 || tabs[0] == null || tabs[0].Session == null || string.IsNullOrWhiteSpace(tabs[0].Session.CommandLine))
            {
                return null;
            }
            WindowSnapshot targetSnapshot = Lookup(byHandle, target.Handle);
            if (targetSnapshot == null || !targetSnapshot.HasTrustedCompleteTabs) return null;
            return new PlannedMerge(candidate.WindowId, snapshot.Identity,
                target.WindowId, targetSnapshot.Identity, tabs[0].Session, tabs[0].Title);
        }

        private static bool HasStacked(List<PlannedMove> occupants)
        {
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != null && occupants[i].Stacked)
                {
                    return true;
                }
            }
            return false;
        }

        private static WindowSnapshot Lookup(Dictionary<long, WindowSnapshot> byHandle, IntPtr handle)
        {
            WindowSnapshot snapshot;
            return byHandle.TryGetValue(handle.ToInt64(), out snapshot) ? snapshot : null;
        }

        private static Dictionary<long, WindowSnapshot> IndexSnapshots(WindowSnapshot[] snapshots)
        {
            Dictionary<long, WindowSnapshot> byHandle = new Dictionary<long, WindowSnapshot>();
            if (snapshots == null)
            {
                return byHandle;
            }
            foreach (WindowSnapshot snapshot in snapshots)
            {
                if (snapshot != null && !byHandle.ContainsKey(snapshot.Handle.ToInt64()))
                {
                    byHandle.Add(snapshot.Handle.ToInt64(), snapshot);
                }
            }
            return byHandle;
        }
    }
}
