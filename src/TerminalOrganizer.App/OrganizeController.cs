using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Layouts;
using TerminalOrganizer.Core.Monitors;
using TerminalOrganizer.Core.Overflow;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.App
{
    public delegate CommitGuard CommitGuardCreation(MonitorInfo monitor, string desktop, LayoutResult layout);
    public delegate WindowPlacementResult[] GuardedPlacementPass(AssignmentPlan plan, MutationGuard guard, WindowSnapshot[] snapshots);
    /// <summary>Injectable seam (REQ-PIPE-001 step 1): the current virtual desktop id text; null when unknown.</summary>
    public delegate string CurrentDesktopSource();

    /// <summary>Injectable seam (REQ-PIPE-001 step 2): select the applied entry and resolve zones for one monitor.</summary>
    public delegate LayoutResult LayoutResolution(MonitorInfo monitor, string currentDesktop);

    /// <summary>Injectable seam (REQ-PIPE-001 step 3): discover the organize inputs on one monitor+desktop.</summary>
    public delegate DiscoveredWindows WindowDiscovery(MonitorInfo monitor, string currentDesktop);

    /// <summary>Injectable seam (REQ-PIPE-001 step 5): plan merges from an assignment and the snapshots (default MergePlanner.Plan).</summary>
    public delegate MergePlan MergePlanning(AssignmentPlan plan, WindowSnapshot[] snapshots);

    /// <summary>
    /// Injectable seam (REQ-PIPE-005): true = the attach helper exists (merge proceeds);
    /// false = definitively absent (skip the merge + HelperMissing notice);
    /// null = the probe itself failed (FAIL OPEN — the merge proceeds and an absent helper
    /// degrades to the existing MergeNotConfirmed path).
    /// </summary>
    public delegate bool? HelperExistsProbe(PlannedMerge merge);

    /// <summary>The one user-facing notice channel: the balloon and the log receive this identical text (REQ-NOTI-001).</summary>
    public delegate void NoticeSink(string text);

    /// <summary>
    /// One organize run's pure planning result (C2): the local assignment, the merge
    /// plan, the cross-monitor redistribution plan, and the commit signature the
    /// mutations must still be guarded against. C2 computes the redistribution but
    /// never applies it — C3 places the user's choice between this plan and the
    /// A4-guarded mutation. Immutable.
    /// </summary>
    public sealed class OrganizePlan
    {
        private readonly AssignmentPlan localAssignment;
        private readonly MergePlan mergePlan;
        private readonly CrossMonitorPlan redistributionPlan;
        private readonly CommitSignature commitSignature;

        public OrganizePlan(AssignmentPlan localAssignment, MergePlan mergePlan,
            CrossMonitorPlan redistributionPlan, CommitSignature commitSignature)
        {
            this.localAssignment = localAssignment;
            this.mergePlan = mergePlan;
            this.redistributionPlan = redistributionPlan;
            this.commitSignature = commitSignature;
        }

        public AssignmentPlan LocalAssignment { get { return localAssignment; } }
        public MergePlan MergePlan { get { return mergePlan; } }
        public CrossMonitorPlan RedistributionPlan { get { return redistributionPlan; } }
        public CommitSignature CommitSignature { get { return commitSignature; } }

        /// <summary>True when cross-monitor planning saw overflow at all — moved or retained.</summary>
        public bool HasOverflow
        {
            get
            {
                return redistributionPlan != null
                    && (redistributionPlan.Moves.Length > 0
                        || redistributionPlan.UnchangedOverflowWindowIds.Length > 0);
            }
        }
    }

    /// <summary>
    /// One organize run's captured planning state (C3): everything pure planning
    /// produced — the local assignment, the merge plan and the C2 redistribution plan —
    /// plus the captured discovery, the preliminary status and the commit guard holding
    /// the signature every mutation must still be validated against. Prepare computes
    /// this and mutates NOTHING (no merge executor, no PostMessage, no SetWindowPos);
    /// the choice dialog sits between this object and the first Commit validation. When
    /// Prepare stopped at a gate (layout stop, no windows, cancellation) the Plan is null
    /// and TerminalResult carries the stopped outcome. Immutable; arrays return copies.
    /// </summary>
    public sealed class PreparedOrganizeRun
    {
        private readonly OrganizeRequest request;
        private readonly OrganizePlan plan;
        private readonly DiscoveredWindows discovered;
        private readonly LastOperationStatus preliminaryStatus;
        private readonly OrganizeRunResult terminalResult;
        private readonly MonitorInfo monitor;
        private readonly string desktop;
        private readonly LayoutResult layout;
        private readonly string managerWindowName;
        private readonly string monitorLabel;
        private readonly string logPath;
        private readonly DateTime startedUtc;
        private readonly CommitGuard commitGuard;
        private readonly bool mergeGateEnabled;
        private readonly CapturedMonitorRow[] rows;
        private readonly WindowFact[] allFacts;
        private readonly WindowSnapshot[] allSnapshots;

        /// <summary>The pinned public shape (C3 design): the request, the plan, the
        /// discovery and the preliminary status. The planning context rides the internal
        /// constructor Prepare uses.</summary>
        public PreparedOrganizeRun(OrganizeRequest request, OrganizePlan plan,
            DiscoveredWindows discovered, LastOperationStatus preliminaryStatus)
        {
            this.request = request;
            this.plan = plan;
            this.discovered = discovered;
            this.preliminaryStatus = preliminaryStatus;
        }

        internal PreparedOrganizeRun(OrganizeRequest request, OrganizePlan plan,
            DiscoveredWindows discovered, LastOperationStatus preliminaryStatus,
            OrganizeRunResult terminalResult, MonitorInfo monitor, string desktop,
            LayoutResult layout, string managerWindowName, string monitorLabel, string logPath,
            DateTime startedUtc, CommitGuard commitGuard, bool mergeGateEnabled,
            CapturedMonitorRow[] rows, WindowFact[] allFacts, WindowSnapshot[] allSnapshots)
        {
            this.request = request;
            this.plan = plan;
            this.discovered = discovered;
            this.preliminaryStatus = preliminaryStatus;
            this.terminalResult = terminalResult;
            this.monitor = monitor;
            this.desktop = desktop;
            this.layout = layout;
            this.managerWindowName = managerWindowName;
            this.monitorLabel = monitorLabel;
            this.logPath = logPath;
            this.startedUtc = startedUtc;
            this.commitGuard = commitGuard;
            this.mergeGateEnabled = mergeGateEnabled;
            this.rows = rows == null ? new CapturedMonitorRow[0] : (CapturedMonitorRow[])rows.Clone();
            this.allFacts = allFacts == null ? new WindowFact[0] : (WindowFact[])allFacts.Clone();
            this.allSnapshots = allSnapshots == null
                ? new WindowSnapshot[0] : (WindowSnapshot[])allSnapshots.Clone();
        }

        public OrganizeRequest Request { get { return request; } }
        public OrganizePlan Plan { get { return plan; } }
        public DiscoveredWindows Discovered { get { return discovered; } }
        public LastOperationStatus PreliminaryStatus { get { return preliminaryStatus; } }

        /// <summary>
        /// True when the run saw overflow at all (C3): the C2 planner planned moves or
        /// retained overflow, OR the local assignment stacked any window. This is the
        /// BROADER signal by design — a stacked window the planner excluded from
        /// candidacy (unidentified, full-screen, untrusted) still asks; C2's
        /// OrganizePlan.HasOverflow alone does not see planner-excluded overflow.
        /// </summary>
        public bool HasOverflow
        {
            get
            {
                if (plan == null)
                {
                    return false;
                }
                if (plan.HasOverflow)
                {
                    return true;
                }
                foreach (PlannedMove move in plan.LocalAssignment.Moves)
                {
                    if (move != null && move.Stacked)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal OrganizeRunResult TerminalResult { get { return terminalResult; } }
        internal MonitorInfo Monitor { get { return monitor; } }
        internal string Desktop { get { return desktop; } }
        internal string ManagerWindowName { get { return managerWindowName; } }
        internal string MonitorLabel { get { return monitorLabel; } }
        internal string LogPath { get { return logPath; } }
        internal DateTime StartedUtc { get { return startedUtc; } }
        internal Zone[] Zones { get { return layout == null ? new Zone[0] : layout.Zones; } }
        internal CommitGuard Guard { get { return commitGuard; } }
        internal bool MergeGateEnabled { get { return mergeGateEnabled; } }
        internal CapturedMonitorRow[] Rows { get { return (CapturedMonitorRow[])rows.Clone(); } }
        internal WindowFact[] AllFacts { get { return (WindowFact[])allFacts.Clone(); } }
        internal WindowSnapshot[] AllSnapshots { get { return (WindowSnapshot[])allSnapshots.Clone(); } }
    }

    /// <summary>
    /// One OTHER monitor's captured planning row (C3): the monitor, its resolved zones,
    /// its physical display label and its discovery. The organized target's own row is
    /// built by Prepare from the run's own layout and discovery — the world capture
    /// never re-discovers the target. Immutable; arrays return copies.
    /// </summary>
    public sealed class CapturedMonitorRow
    {
        private readonly MonitorInfo monitor;
        private readonly Zone[] zones;
        private readonly string label;
        private readonly DiscoveredWindows discovered;

        public CapturedMonitorRow(MonitorInfo monitor, Zone[] zones, string label, DiscoveredWindows discovered)
        {
            this.monitor = monitor;
            this.zones = zones == null ? new Zone[0] : (Zone[])zones.Clone();
            this.label = label == null ? string.Empty : label;
            this.discovered = discovered;
        }

        public MonitorInfo Monitor { get { return monitor; } }
        public Zone[] Zones { get { return (Zone[])zones.Clone(); } }
        public string Label { get { return label; } }
        public DiscoveredWindows Discovered { get { return discovered; } }
    }

    /// <summary>
    /// The captured multi-monitor planning world (C3): every OTHER monitor's row plus
    /// the persisted manual priority overrides (the composition-level capture loads the
    /// settings; the controller itself has no settings dependency). Immutable; arrays
    /// return copies.
    /// </summary>
    public sealed class CapturedOverflowWorld
    {
        private readonly CapturedMonitorRow[] otherMonitors;
        private readonly PriorityOverride[] priorityOverrides;

        public CapturedOverflowWorld(CapturedMonitorRow[] otherMonitors, PriorityOverride[] priorityOverrides)
        {
            this.otherMonitors = otherMonitors == null
                ? new CapturedMonitorRow[0] : (CapturedMonitorRow[])otherMonitors.Clone();
            this.priorityOverrides = priorityOverrides == null
                ? new PriorityOverride[0] : (PriorityOverride[])priorityOverrides.Clone();
        }

        public CapturedMonitorRow[] OtherMonitors { get { return (CapturedMonitorRow[])otherMonitors.Clone(); } }
        public PriorityOverride[] PriorityOverrides { get { return (PriorityOverride[])priorityOverrides.Clone(); } }
    }

    /// <summary>
    /// Injectable seam (C3): capture the other monitors' planning rows and the manual
    /// priority overrides for one prepare. Null (the pre-C3 constructors) keeps the run
    /// single-monitor — the redistribution plan comes out empty and cross-monitor
    /// choices never appear. Invoked only during Prepare, never during Commit.
    /// </summary>
    public delegate CapturedOverflowWorld OverflowWorldCapture(MonitorInfo target, string currentDesktop);

    /// <summary>
    /// Injectable seam (C3): apply ONE planned cross-monitor move (production: a guarded
    /// SetWindowPos through the placer's identity check). Invoked in the C2 plan's order
    /// during a Redistribute commit; the controller owns the cancellation/guard
    /// checkpoints around each call.
    /// </summary>
    public delegate WindowPlacementResult CrossMonitorMoveExecution(CrossMonitorMove move,
        MutationGuard guard, WindowSnapshot[] snapshots, CancellationToken cancellationToken);

    /// <summary>
    /// Composes the C2 cross-monitor snapshot from captured per-monitor data (pure:
    /// joins by handle, classifies through the local assignment plans, resolves
    /// priorities through C1 — no enumeration, clock or Win32 call). The tools'
    /// -WhatIf previews and the C3 Prepare flow both build their snapshot here so the
    /// classification rules can never drift per caller.
    /// </summary>
    public static class CrossMonitorSnapshotComposer
    {
        // @MX:ANCHOR: [AUTO] C2 snapshot composition — the only producer of CrossMonitorSnapshot from live-captured data.
        // @MX:REASON: organize-dryrun, organize-once -WhatIf and the C3 choice flow must classify windows identically; a second composer would drift.
        public static CrossMonitorSnapshot Compose(
            string currentDesktopId,
            MonitorInfo[] monitors,
            Zone[][] zonesPerMonitor,
            string[] labelsPerMonitor,
            AssignmentPlan[] localPlans,
            EnumeratedWindow[] allWindows,
            WindowFact[] facts,
            WindowSnapshot[] snapshots,
            PriorityOverride[] manualOverrides)
        {
            int monitorCount = MonitorRowCount(monitors, zonesPerMonitor, labelsPerMonitor, localPlans);
            List<MonitorLayoutSnapshot> layouts = new List<MonitorLayoutSnapshot>();
            List<AssignmentPlan> plans = new List<AssignmentPlan>();
            for (int i = 0; i < monitorCount; i++)
            {
                MonitorInfo monitor = monitors[i];
                Zone[] zones = zonesPerMonitor[i];
                if (monitor == null || monitor.StableKey == null
                    || string.IsNullOrEmpty(monitor.StableKey.CanonicalValue)
                    || zones == null || zones.Length == 0)
                {
                    continue;
                }
                layouts.Add(new MonitorLayoutSnapshot(monitor.StableKey,
                    labelsPerMonitor[i] == null ? string.Empty : labelsPerMonitor[i], zones));
                plans.Add(localPlans[i]);
            }

            List<CrossMonitorWindow> windows = new List<CrossMonitorWindow>();
            EnumeratedWindow[] enumerated = allWindows ?? new EnumeratedWindow[0];
            for (int z = 0; z < enumerated.Length; z++)
            {
                EnumeratedWindow row = enumerated[z];
                if (row == null || row.Monitor == null)
                {
                    continue;
                }
                WindowFact fact = FindFact(facts, row.Handle);
                if (fact == null)
                {
                    // Windows discovery excluded (other/unknown desktop, untrusted state)
                    // never enter the planner's world.
                    continue;
                }
                int index = IndexOfLayout(layouts, row.Monitor.StableKey);
                if (index < 0)
                {
                    continue;
                }
                WindowSnapshot snapshot = FindSnapshot(snapshots, row.Handle);
                windows.Add(BuildWindow(row, z, fact, snapshot, layouts[index], plans[index], manualOverrides));
            }
            return new CrossMonitorSnapshot(currentDesktopId, layouts.ToArray(), windows.ToArray());
        }

        private static int MonitorRowCount(MonitorInfo[] monitors, Zone[][] zonesPerMonitor,
            string[] labelsPerMonitor, AssignmentPlan[] localPlans)
        {
            int count = monitors == null ? 0 : monitors.Length;
            count = Min(count, zonesPerMonitor == null ? 0 : zonesPerMonitor.Length);
            count = Min(count, labelsPerMonitor == null ? 0 : labelsPerMonitor.Length);
            count = Min(count, localPlans == null ? 0 : localPlans.Length);
            return count;
        }

        private static int Min(int a, int b)
        {
            return a < b ? a : b;
        }

        private static CrossMonitorWindow BuildWindow(EnumeratedWindow row, int zOrderIndex,
            WindowFact fact, WindowSnapshot snapshot, MonitorLayoutSnapshot layout,
            AssignmentPlan plan, PriorityOverride[] manualOverrides)
        {
            PlannedMove move = null;
            bool hasMove = plan != null && plan.TryFindMove(fact.Id, out move);
            bool manager = plan != null && plan.ManagerPinned
                && string.Equals(plan.ManagerWindowId, fact.Id, StringComparison.Ordinal);
            // A2 classification: an exact-rect normal no-move is a stable occupant; a
            // stacked assignment is the overflow the planner considers.
            bool stable = hasMove && move != null
                && move.SkipReason == PlannedMoveSkipReason.None && !move.MoveRequired && !move.Stacked;
            bool overflow = hasMove && move != null && move.Stacked;
            int currentZoneId = hasMove && move != null ? move.ZoneId : -1;
            bool verified = row.DesktopStatus != null && row.DesktopStatus.Success && row.DesktopStatus.Value;

            // C1 alignment: the immovable rows carry their matching derived class next
            // to the booleans so resolution and immovability can never disagree.
            DerivedPriorityClass derivedClass = DerivedClass(snapshot);
            if (manager)
            {
                derivedClass = DerivedPriorityClass.Manager;
            }
            else if (fact.FullScreen)
            {
                derivedClass = DerivedPriorityClass.FullScreen;
            }
            else if (stable)
            {
                derivedClass = DerivedPriorityClass.StableOccupant;
            }
            PriorityInput input = new PriorityInput(fact.Id,
                ManualRank(manualOverrides, fact, snapshot), DeclaredRank(snapshot),
                derivedClass, manager, fact.FullScreen, stable,
                zOrderIndex, layout.MonitorKey.CanonicalValue, fact.Top, fact.Left);
            ResolvedPriority priority = PriorityResolver.Resolve(input);
            return new CrossMonitorWindow(fact.Id, row.Handle, row.Identity,
                layout.MonitorKey, verified, fact.MutationStateTrusted,
                fact.FullScreen, manager, stable, overflow, currentZoneId,
                fact.Left, fact.Top, fact.Width, fact.Height, priority);
        }

        /// <summary>The session-derived class; unidentified windows are Unidentified.</summary>
        private static DerivedPriorityClass DerivedClass(WindowSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Identified)
            {
                return DerivedPriorityClass.Unidentified;
            }
            foreach (TabSnapshot tab in snapshot.Tabs)
            {
                if (tab == null || tab.Session == null)
                {
                    continue;
                }
                if (tab.Session.Kind == SessionKind.WindowsNative)
                {
                    return DerivedPriorityClass.WindowsNative;
                }
                if (tab.Session.Kind == SessionKind.Remote)
                {
                    return DerivedPriorityClass.RemoteSession;
                }
                if (tab.Session.Kind == SessionKind.Local)
                {
                    return DerivedPriorityClass.LocalSession;
                }
            }
            return DerivedPriorityClass.Unidentified;
        }

        /// <summary>The first title-declared rank across the tabs (C1 grammar), or null.</summary>
        private static int? DeclaredRank(WindowSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }
            foreach (TabSnapshot tab in snapshot.Tabs)
            {
                if (tab == null)
                {
                    continue;
                }
                NormalizedTitle parsed = TitleNormalizer.Parse(tab.Title);
                if (parsed.DeclaredRank.HasValue)
                {
                    return parsed.DeclaredRank;
                }
            }
            return null;
        }

        /// <summary>
        /// The first matching manual override (canonical settings order): a Session row
        /// matches any tab session name, a RawTitle row matches the window name. UserLabel
        /// rows need the run-scoped label registry, which the tools do not carry — those
        /// rows match nothing here and C3's Prepare carries the labels.
        /// </summary>
        private static int? ManualRank(PriorityOverride[] overrides, WindowFact fact, WindowSnapshot snapshot)
        {
            foreach (PriorityOverride row in overrides ?? new PriorityOverride[0])
            {
                if (row == null || row.Selector == null || string.IsNullOrEmpty(row.Selector.Value))
                {
                    continue;
                }
                bool matches = false;
                if (row.Selector.Kind == ManagerSelectorKind.Session && snapshot != null)
                {
                    foreach (TabSnapshot tab in snapshot.Tabs)
                    {
                        if (tab != null && tab.Session != null
                            && string.Equals(tab.Session.Name, row.Selector.Value, StringComparison.Ordinal))
                        {
                            matches = true;
                            break;
                        }
                    }
                }
                else if (row.Selector.Kind == ManagerSelectorKind.RawTitle && fact != null
                    && string.Equals(fact.Name, row.Selector.Value, StringComparison.Ordinal))
                {
                    matches = true;
                }
                if (matches && row.Rank >= PriorityResolver.ManualRankMin && row.Rank <= PriorityResolver.ManualRankMax)
                {
                    return row.Rank;
                }
            }
            return null;
        }

        private static WindowFact FindFact(WindowFact[] facts, IntPtr handle)
        {
            foreach (WindowFact fact in facts ?? new WindowFact[0])
            {
                if (fact != null && fact.Handle == handle)
                {
                    return fact;
                }
            }
            return null;
        }

        private static WindowSnapshot FindSnapshot(WindowSnapshot[] snapshots, IntPtr handle)
        {
            foreach (WindowSnapshot snapshot in snapshots ?? new WindowSnapshot[0])
            {
                if (snapshot != null && snapshot.Handle == handle)
                {
                    return snapshot;
                }
            }
            return null;
        }

        private static int IndexOfLayout(List<MonitorLayoutSnapshot> layouts, MonitorKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.CanonicalValue))
            {
                return -1;
            }
            for (int i = 0; i < layouts.Count; i++)
            {
                if (string.Equals(layouts[i].MonitorKey.CanonicalValue, key.CanonicalValue,
                    StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// The window discovery output (REQ-PIPE-001 step 3): the placement facts and the
    /// identification snapshots for the same windows, joined by handle. Immutable; arrays
    /// return copies.
    /// </summary>
    public sealed class DiscoveredWindows
    {
        private readonly WindowFact[] facts;
        private readonly WindowSnapshot[] snapshots;

        public DiscoveredWindows(WindowFact[] facts, WindowSnapshot[] snapshots)
            : this(facts, snapshots, 0, 0, 0, 0)
        {
        }

        public DiscoveredWindows(WindowFact[] facts, WindowSnapshot[] snapshots, int enumeratedCount,
            int skippedOtherDesktopCount, int skippedUnknownDesktopCount, int skippedUnknownStateCount)
        {
            EnumeratedCount = enumeratedCount;
            SkippedOtherDesktopCount = skippedOtherDesktopCount;
            SkippedUnknownDesktopCount = skippedUnknownDesktopCount;
            SkippedUnknownStateCount = skippedUnknownStateCount;
            this.facts = facts == null ? new WindowFact[0] : (WindowFact[])facts.Clone();
            this.snapshots = snapshots == null ? new WindowSnapshot[0] : (WindowSnapshot[])snapshots.Clone();
        }

        public WindowFact[] Facts { get { return (WindowFact[])facts.Clone(); } }
        public int EnumeratedCount { get; private set; }
        public int SkippedOtherDesktopCount { get; private set; }
        public int SkippedUnknownDesktopCount { get; private set; }
        public int SkippedUnknownStateCount { get; private set; }
        public WindowSnapshot[] Snapshots { get { return (WindowSnapshot[])snapshots.Clone(); } }
    }

    /// <summary>
    /// One organize run's terminal outcome: per-merge outcomes in execution order, the
    /// count of helper-missing skips, the final (survivor) assignment plan, the placement
    /// results (empty when nothing needed a move), and whether the run completed (false =
    /// stopped at the layout gate with nothing mutated). Immutable; arrays return copies.
    /// </summary>
    public sealed class OrganizeRunResult
    {
        private readonly MergeOutcome[] outcomes;
        private readonly int helperMissingSkips;
        private readonly AssignmentPlan finalPlan;
        private readonly WindowPlacementResult[] placementResults;
        private readonly bool completed;
        private readonly LastOperationStatus status;

        public OrganizeRunResult(MergeOutcome[] outcomes, int helperMissingSkips,
            AssignmentPlan finalPlan, WindowPlacementResult[] placementResults, bool completed)
            : this(outcomes, helperMissingSkips, finalPlan, placementResults, completed, false)
        {
        }

        public OrganizeRunResult(MergeOutcome[] outcomes, int helperMissingSkips,
            AssignmentPlan finalPlan, WindowPlacementResult[] placementResults, bool completed, bool topologyAborted)
            : this(outcomes, helperMissingSkips, finalPlan, placementResults, completed, topologyAborted, null)
        {
        }

        public OrganizeRunResult(MergeOutcome[] outcomes, int helperMissingSkips,
            AssignmentPlan finalPlan, WindowPlacementResult[] placementResults, bool completed,
            bool topologyAborted, LastOperationStatus status)
        {
            TopologyAborted = topologyAborted;
            this.outcomes = outcomes == null ? new MergeOutcome[0] : (MergeOutcome[])outcomes.Clone();
            this.helperMissingSkips = helperMissingSkips;
            this.finalPlan = finalPlan;
            this.placementResults = placementResults == null
                ? new WindowPlacementResult[0] : (WindowPlacementResult[])placementResults.Clone();
            this.completed = completed;
            this.status = status;
        }

        public MergeOutcome[] Outcomes { get { return (MergeOutcome[])outcomes.Clone(); } }
        public int HelperMissingSkips { get { return helperMissingSkips; } }

        /// <summary>The survivor assignment plan; null when the run stopped at the layout gate.</summary>
        public AssignmentPlan FinalPlan { get { return finalPlan; } }
        public WindowPlacementResult[] PlacementResults { get { return (WindowPlacementResult[])placementResults.Clone(); } }

        /// <summary>False when the layout gate stopped the run before any window work (D-5: nothing was mutated).</summary>
        public bool Completed { get { return completed; } }
        public bool TopologyAborted { get; private set; }

        /// <summary>The structured per-category outcome (B3); null on legacy constructor paths.</summary>
        public LastOperationStatus Status { get { return status; } }

        public override string ToString()
        {
            if (status != null)
            {
                // B3: placed/failed come from the structured counts — never from the raw
                // placement-record count (a stacked or failed record is not a placement).
                return string.Format(CultureInfo.InvariantCulture,
                    "completed={0} outcomes={1} helper-skips={2} placed={3} failed={4}",
                    completed, outcomes.Length, helperMissingSkips, status.Placed, status.Failed);
            }
            return string.Format(CultureInfo.InvariantCulture, "completed={0} outcomes={1} helper-skips={2} placed={3}",
                completed, outcomes.Length, helperMissingSkips, placementResults.Length);
        }
    }

    /// <summary>
    /// The organize pipeline behind injected ports (REQ-PIPE-001..005, spec D-4/D-5),
    /// split since C3 into a pure Prepare phase and a guarded Commit phase: resolve the
    /// current desktop; select and resolve the layout — on Unsupported or Invalid, emit
    /// the matching notice and STOP (no mutation); discover the windows on that
    /// monitor+desktop; compute the initial assignment (SPEC-ZONE-005); plan merges from
    /// that assignment (SPEC-OVERFLOW-006); capture the multi-monitor world and the C2
    /// redistribution plan; capture the commit signature — all inside Prepare, which
    /// mutates NOTHING. Commit then validates the captured signature first, and applies
    /// exactly one policy: probe helper existence for the planned Remote merges
    /// (fail-open — REQ-PIPE-005) and emit HelperMissing for definitive absences;
    /// execute the surviving merges; emit the per-merge failure notices
    /// (MergeNotConfirmed; MergeConfirmedNotClosed); recompute the assignment for the
    /// survivors; apply placement only when a window actually needs a move — or, for
    /// Redistribute, apply the C2 moves first, then reacquire/recompute the affected
    /// monitors, then the remaining placement. A chosen manager that could not be pinned
    /// emits ManagerSkipped. All computation completes before the first mutation (the
    /// merge executor, the cross-monitor move executor and the placer are the only
    /// mutating ports). No WinForms types anywhere in this class (C2); Pester drives it
    /// with fakes.
    /// </summary>
    public sealed class OrganizeController
    {
        private readonly CurrentDesktopSource desktopSource;
        private readonly LayoutResolution layoutResolve;
        private readonly WindowDiscovery windows;
        private readonly AssignmentComputation assign;
        private readonly MergePlanning mergePlanning;
        private readonly HelperExistsProbe helperProbe;
        private readonly MergeExecution mergeExecutor;
        private readonly PlacementPass placer;
        private readonly NoticeSink notify;
        private readonly LabelRegistry labels;
        private readonly Action<string> trace;
        private readonly CommitGuardCreation createGuard;
        private readonly GuardedPlacementPass guardedPlacer;
        private readonly NoticeMessageSink messageSink;
        private readonly OverflowWorldCapture worldCapture;
        private readonly CrossMonitorMoveExecution crossMoveExecutor;

        public OrganizeController(CurrentDesktopSource desktopSource, LayoutResolution layoutResolve,
            WindowDiscovery windows, AssignmentComputation assign, MergePlanning mergePlanning,
            HelperExistsProbe helperProbe, MergeExecution mergeExecutor, PlacementPass placer,
            NoticeSink notify, LabelRegistry labels)
            : this(desktopSource, layoutResolve, windows, assign, mergePlanning, helperProbe,
                mergeExecutor, placer, notify, labels, null)
        {
        }

        /// <param name="trace">Optional diagnostic sink for fail-open probe drops (morning triage; advisory).</param>
        public OrganizeController(CurrentDesktopSource desktopSource, LayoutResolution layoutResolve,
            WindowDiscovery windows, AssignmentComputation assign, MergePlanning mergePlanning,
            HelperExistsProbe helperProbe, MergeExecution mergeExecutor, PlacementPass placer,
            NoticeSink notify, LabelRegistry labels, Action<string> trace)
            : this(desktopSource, layoutResolve, windows, assign, mergePlanning, helperProbe, mergeExecutor,
                placer, notify, labels, trace, null, null)
        {
        }

        public OrganizeController(CurrentDesktopSource desktopSource, LayoutResolution layoutResolve,
            WindowDiscovery windows, AssignmentComputation assign, MergePlanning mergePlanning,
            HelperExistsProbe helperProbe, MergeExecution mergeExecutor, PlacementPass placer,
            NoticeSink notify, LabelRegistry labels, Action<string> trace, CommitGuardCreation createGuard,
            GuardedPlacementPass guardedPlacer)
            : this(desktopSource, layoutResolve, windows, assign, mergePlanning, helperProbe,
                mergeExecutor, placer, notify, labels, trace, createGuard, guardedPlacer, null)
        {
        }

        /// <param name="messageSink">Optional B3 notice channel: when set, emissions carry
        /// level and balloon policy (NoticeMessage); when null, the legacy string sink
        /// receives the same resolved text — existing call sites keep working either way.</param>
        public OrganizeController(CurrentDesktopSource desktopSource, LayoutResolution layoutResolve,
            WindowDiscovery windows, AssignmentComputation assign, MergePlanning mergePlanning,
            HelperExistsProbe helperProbe, MergeExecution mergeExecutor, PlacementPass placer,
            NoticeSink notify, LabelRegistry labels, Action<string> trace, CommitGuardCreation createGuard,
            GuardedPlacementPass guardedPlacer, NoticeMessageSink messageSink)
        {
            this.createGuard = createGuard;
            this.guardedPlacer = guardedPlacer;
            if (desktopSource == null) throw new ArgumentNullException("desktopSource");
            if (layoutResolve == null) throw new ArgumentNullException("layoutResolve");
            if (windows == null) throw new ArgumentNullException("windows");
            if (assign == null) throw new ArgumentNullException("assign");
            if (mergePlanning == null) throw new ArgumentNullException("mergePlanning");
            if (helperProbe == null) throw new ArgumentNullException("helperProbe");
            if (mergeExecutor == null) throw new ArgumentNullException("mergeExecutor");
            if (placer == null) throw new ArgumentNullException("placer");
            if (notify == null) throw new ArgumentNullException("notify");
            if (labels == null) throw new ArgumentNullException("labels");

            this.desktopSource = desktopSource;
            this.layoutResolve = layoutResolve;
            this.windows = windows;
            this.assign = assign;
            this.mergePlanning = mergePlanning;
            this.helperProbe = helperProbe;
            this.mergeExecutor = mergeExecutor;
            this.placer = placer;
            this.notify = notify;
            this.labels = labels;
            this.trace = trace;
            this.messageSink = messageSink;
        }

        /// <summary>
        /// C3 form: adds the multi-monitor world capture and the cross-monitor move
        /// executor. Both may be null — the run then keeps its pre-C3 single-monitor
        /// behaviour (empty redistribution plan, no cross-monitor choices).
        /// </summary>
        public OrganizeController(CurrentDesktopSource desktopSource, LayoutResolution layoutResolve,
            WindowDiscovery windows, AssignmentComputation assign, MergePlanning mergePlanning,
            HelperExistsProbe helperProbe, MergeExecution mergeExecutor, PlacementPass placer,
            NoticeSink notify, LabelRegistry labels, Action<string> trace, CommitGuardCreation createGuard,
            GuardedPlacementPass guardedPlacer, NoticeMessageSink messageSink,
            OverflowWorldCapture worldCapture, CrossMonitorMoveExecution crossMoveExecutor)
            : this(desktopSource, layoutResolve, windows, assign, mergePlanning, helperProbe,
                mergeExecutor, placer, notify, labels, trace, createGuard, guardedPlacer, messageSink)
        {
            this.worldCapture = worldCapture;
            this.crossMoveExecutor = crossMoveExecutor;
        }

        /// <summary>
        /// The run-scoped label registry (REQ-PIPE-004): the tray menu labels through this
        /// instance; the discovery adapter consults it when composing the matcher input.
        /// </summary>
        public LabelRegistry Labels { get { return labels; } }

        /// <summary>Organizes one monitor (D-4). Deterministic under fixed ports (NFR-2); never mutates on a layout stop.</summary>
        // @MX:ANCHOR: [AUTO] the product's whole behaviour in one entry point (plan.md H).
        // @MX:REASON: every organize action (menu, hotkey, organize-once) funnels through Run; the ordering here is the no-half-states guarantee.
        public OrganizeRunResult Run(MonitorInfo monitor, string managerWindowName)
        {
            return Run(monitor, managerWindowName, false);
        }

        public OrganizeRunResult Run(MonitorInfo monitor, string managerWindowName, bool mergeEnabled)
        {
            return Run(monitor, managerWindowName, mergeEnabled, null, null);
        }

        /// <param name="monitorLabel">Display label for the notices and the status (B3);
        /// null derives a best-effort label from the monitor alone (the caller that knows
        /// the full monitor set should pass TrayMenuBuilder.DerivePhysicalLabel's result).</param>
        /// <param name="logPath">The configured log path carried on the status (B3).</param>
        public OrganizeRunResult Run(MonitorInfo monitor, string managerWindowName, bool mergeEnabled,
            string monitorLabel, string logPath)
        {
            return Run(monitor, managerWindowName, mergeEnabled, monitorLabel, logPath, CancellationToken.None);
        }

        /// <param name="cancellationToken">B4 cancellation checkpoints: before/after the
        /// monitor+layout reads, after discovery, before pure planning, between guarded
        /// merges, and immediately before the mutation phase. A checkpoint hit stops the
        /// run BEFORE the next mutation (an issued PostMessage/SetWindowPos always
        /// completes); the Aborted status carries the "cancelled" detail.</param>
        public OrganizeRunResult Run(MonitorInfo monitor, string managerWindowName, bool mergeEnabled,
            string monitorLabel, string logPath, CancellationToken cancellationToken)
        {
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                PreparedOrganizeRun prepared = PrepareCore(monitor, managerWindowName, mergeEnabled,
                    monitorLabel, logPath, null, startedUtc, cancellationToken);
                // Compatibility overload (C3): pure planning plus a HEADLESS commit —
                // never any UI. The legacy mergeEnabled flag selects Merge vs Stack so
                // the pinned headless sequences (AC-006..008, B5 organize-once) keep
                // their exact behaviour.
                return CommitCore(prepared, mergeEnabled ? OverflowPolicy.Merge : OverflowPolicy.Stack,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                return FailedRun(monitor, monitorLabel, logPath, startedUtc, ex);
            }
        }

        /// <summary>
        /// C3 public planning entry (pure — mutates nothing; see PrepareCore). The
        /// resolved monitor and run inputs ride the parameters: the tray-level
        /// OrganizeRequest identifies the originating intent and is carried on the
        /// prepared run, but monitor resolution stays with the shell (the coordinator's
        /// request type does not carry the resolved target).
        /// </summary>
        public PreparedOrganizeRun Prepare(MonitorInfo monitor, string managerWindowName, bool mergeEnabled,
            string monitorLabel, string logPath, OrganizeRequest request, CancellationToken cancellationToken)
        {
            return PrepareCore(monitor, managerWindowName, mergeEnabled, monitorLabel, logPath, request,
                DateTime.UtcNow, cancellationToken);
        }

        /// <summary>
        /// C3 mutation entry: first validates the captured commit signature, then
        /// applies exactly ONE policy (see CommitCore). Ask never reaches this method —
        /// the caller resolves it to another value or cancels.
        /// </summary>
        public OrganizeRunResult Commit(PreparedOrganizeRun prepared, OverflowPolicy policy,
            CancellationToken cancellationToken)
        {
            if (prepared == null) throw new ArgumentNullException("prepared");
            return CommitCore(prepared, policy, cancellationToken);
        }

        /// <summary>
        /// The C3 choice flow: worker Prepare, then the pre-mutation decision. No
        /// overflow commits Stack without any dialog; a saved Stack/Redistribute commits
        /// the saved policy without any dialog; a saved Merge commits Merge (CommitCore
        /// degrades to Stack with one warning while the gate is closed); saved Ask shows
        /// the choice seam only now that overflow is proven — Cancel performs zero
        /// mutation, a choice optionally persists through the callback, then the worker
        /// resumes with Commit(choice).
        /// </summary>
        public OrganizeRunResult RunWithChoice(MonitorInfo monitor, string managerWindowName,
            string monitorLabel, string logPath, OverflowPolicy savedPolicy, bool mergeReleaseGate,
            OverflowChoicePrompt choicePrompt, Action<OverflowPolicy> persistChoice,
            CancellationToken cancellationToken)
        {
            DateTime startedUtc = DateTime.UtcNow;
            try
            {
                PreparedOrganizeRun prepared = PrepareCore(monitor, managerWindowName, mergeReleaseGate,
                    monitorLabel, logPath, null, startedUtc, cancellationToken);
                TraceLine("overflow-flow:prepare");
                if (prepared.Plan == null)
                {
                    return prepared.TerminalResult;
                }
                if (!prepared.HasOverflow)
                {
                    return CommitCore(prepared, OverflowPolicy.Stack, cancellationToken);
                }
                if (savedPolicy == OverflowPolicy.Stack || savedPolicy == OverflowPolicy.Redistribute)
                {
                    return CommitCore(prepared, savedPolicy, cancellationToken);
                }
                if (savedPolicy == OverflowPolicy.Merge)
                {
                    return CommitCore(prepared, OverflowPolicy.Merge, cancellationToken);
                }
                // Saved Ask: the modal choice only after overflow is proven, and never
                // after cancellation.
                if (cancellationToken.IsCancellationRequested)
                {
                    return Cancelled(startedUtc, ResolveMonitorLabel(monitor, monitorLabel), logPath,
                        prepared.Discovered, new List<MergeOutcome>(), new WindowPlacementResult[0]);
                }
                TraceLine("overflow-flow:choice");
                OverflowChoiceResult choice = choicePrompt == null
                    ? null : choicePrompt(prepared, mergeReleaseGate);
                if (choice == null || choice.Choice == OverflowChoice.Cancel)
                {
                    return ChoiceCancelled(prepared, cancellationToken);
                }
                OverflowPolicy chosen = MapChoice(choice.Choice);
                if (choice.RememberChoice && persistChoice != null)
                {
                    persistChoice(chosen);
                }
                return CommitCore(prepared, chosen, cancellationToken);
            }
            catch (Exception ex)
            {
                return FailedRun(monitor, monitorLabel, logPath, startedUtc, ex);
            }
        }

        private static OverflowPolicy MapChoice(OverflowChoice choice)
        {
            if (choice == OverflowChoice.Merge)
            {
                return OverflowPolicy.Merge;
            }
            if (choice == OverflowChoice.Redistribute)
            {
                return OverflowPolicy.Redistribute;
            }
            return OverflowPolicy.Stack;
        }

        /// <summary>
        /// The C3 cancelled-choice shape: the user (or a token cancellation while the
        /// dialog was pending) aborted the choice — zero mutation, an Aborted status
        /// whose detail names why, the stacked count still visible in the status.
        /// </summary>
        private static OrganizeRunResult ChoiceCancelled(PreparedOrganizeRun prepared, CancellationToken token)
        {
            DiscoveredWindows discovered = prepared.Discovered;
            int skippedUnknownDesktop = discovered == null ? 0 : discovered.SkippedUnknownDesktopCount;
            int skippedUnknownState = discovered == null ? 0 : discovered.SkippedUnknownStateCount;
            int discoveredCount = (discovered == null ? 0 : discovered.Facts.Length)
                + skippedUnknownDesktop + skippedUnknownState;
            int stacked = 0;
            foreach (PlannedMove move in prepared.Plan.LocalAssignment.Moves)
            {
                if (move != null && move.Stacked)
                {
                    stacked++;
                }
            }
            LastOperationStatus status = new LastOperationStatus(prepared.StartedUtc, DateTime.UtcNow,
                prepared.MonitorLabel, OperationDisposition.Aborted, discoveredCount,
                skippedUnknownDesktop, skippedUnknownState, 0, 0, 0, stacked, 0, 0, 0, false,
                prepared.LogPath, token.IsCancellationRequested ? "cancelled" : "overflow-choice-cancelled");
            return new OrganizeRunResult(new MergeOutcome[0], 0, prepared.Plan.LocalAssignment,
                new WindowPlacementResult[0], false, false, status);
        }

        /// <summary>B3: an unexpected failure becomes a Failed status plus the Error-level
        /// notice; the exception detail rides the status, the user text stays generic.</summary>
        private OrganizeRunResult FailedRun(MonitorInfo monitor, string monitorLabel, string logPath,
            DateTime startedUtc, Exception ex)
        {
            Emit(NoticeKind.UnexpectedFailure, null);
            return new OrganizeRunResult(new MergeOutcome[0], 0, null, new WindowPlacementResult[0], false, false,
                new LastOperationStatus(startedUtc, DateTime.UtcNow, ResolveMonitorLabel(monitor, monitorLabel),
                    OperationDisposition.Failed, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, logPath, ex.Message));
        }

        /// <summary>
        /// The C3 pure planning phase (mutates NOTHING): desktop/layout acquisition with
        /// the pinned early stops, discovery, the initial assignment, merge PLANNING
        /// (never execution), the multi-monitor world capture, C1/C2 cross-monitor
        /// planning, and the commit-signature capture through the guard. A run stopped
        /// at a gate returns a prepared run whose Plan is null.
        /// </summary>
        private PreparedOrganizeRun PrepareCore(MonitorInfo monitor, string managerWindowName,
            bool mergeEnabled, string monitorLabel, string logPath, OrganizeRequest request,
            DateTime startedUtc, CancellationToken cancellationToken)
        {
            string label = ResolveMonitorLabel(monitor, monitorLabel);
            List<MergeOutcome> outcomes = new List<MergeOutcome>();
            WindowPlacementResult[] noPlacements = new WindowPlacementResult[0];

            string desktop = desktopSource();
            if (cancellationToken.IsCancellationRequested)
            {
                return new PreparedOrganizeRun(request, null, null, null,
                    Cancelled(startedUtc, label, logPath, null, outcomes, noPlacements),
                    monitor, desktop, null, managerWindowName, label, logPath, startedUtc,
                    null, mergeEnabled, null, null, null);
            }

            LayoutResult layout = layoutResolve(monitor, desktop);
            if (layout == null || layout.Kind != LayoutResultKind.Supported)
            {
                EmitLayoutNotice(layout);
                OrganizeRunResult stopped = new OrganizeRunResult(outcomes.ToArray(), 0, null, noPlacements, false, false,
                    BuildStatus(startedUtc, label, OperationDisposition.Aborted, null, null, outcomes.ToArray(),
                        noPlacements, false, logPath, null));
                return new PreparedOrganizeRun(request, null, null, stopped.Status, stopped,
                    monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                    null, mergeEnabled, null, null, null);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new PreparedOrganizeRun(request, null, null, null,
                    Cancelled(startedUtc, label, logPath, null, outcomes, noPlacements),
                    monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                    null, mergeEnabled, null, null, null);
            }
            Zone[] zones = layout.Zones;
            CommitGuard commitGuard = createGuard == null ? null : createGuard(monitor, desktop, layout);

            DiscoveredWindows discovered = windows(monitor, desktop);
            if (cancellationToken.IsCancellationRequested)
            {
                return new PreparedOrganizeRun(request, null, null, null,
                    Cancelled(startedUtc, label, logPath, discovered, outcomes, noPlacements),
                    monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                    commitGuard, mergeEnabled, null, null, null);
            }
            WindowFact[] facts = discovered == null ? new WindowFact[0] : discovered.Facts;
            WindowSnapshot[] snapshots = discovered == null ? new WindowSnapshot[0] : discovered.Snapshots;
            int acquisitionSkips = discovered == null
                ? 0 : discovered.SkippedUnknownDesktopCount + discovered.SkippedUnknownStateCount;
            if (facts.Length == 0)
            {
                // B3: zero windows is an information event, not a warning — the NoWindows
                // notice is the ONLY emission (never a manager warning).
                Emit(NoticeKind.NoWindows, label);
                OrganizeRunResult stopped = new OrganizeRunResult(outcomes.ToArray(), 0,
                    new AssignmentPlan(new PlannedMove[0], false, null, null), noPlacements, true, false,
                    BuildStatus(startedUtc, label, OperationDisposition.NothingToDo, discovered, null,
                        outcomes.ToArray(), noPlacements, false, logPath, null));
                return new PreparedOrganizeRun(request, null, discovered, stopped.Status, stopped,
                    monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                    commitGuard, mergeEnabled, null, facts, snapshots);
            }
            if (acquisitionSkips > 0)
            {
                Emit(NoticeKind.AcquisitionDegraded, acquisitionSkips.ToString(CultureInfo.InvariantCulture));
            }

            // The initial assignment PRECEDES merge planning (OVERFLOW-006 REQ-MRG-001 consumes an AssignmentPlan).
            if (cancellationToken.IsCancellationRequested)
            {
                return new PreparedOrganizeRun(request, null, discovered, null,
                    Cancelled(startedUtc, label, logPath, discovered, outcomes, noPlacements),
                    monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                    commitGuard, mergeEnabled, null, facts, snapshots);
            }
            AssignmentPlan initial = assign(zones, facts, managerWindowName);
            EmitManagerNotice(initial, managerWindowName, label);

            // @MX:NOTE: automatic merging is opt-in; disabled runs never invoke merge ports.
            MergePlan mergePlan = mergeEnabled ? mergePlanning(initial, snapshots)
                : new MergePlan(new PlannedMerge[0], new int[0]);

            // C3: the multi-monitor planning world (the other monitors plus the manual
            // priority overrides). A missing port keeps the run single-monitor (empty
            // redistribution plan); a capture failure degrades the same way — local
            // organizing still runs.
            CapturedMonitorRow[] worldRows = new CapturedMonitorRow[0];
            PriorityOverride[] overrides = new PriorityOverride[0];
            if (worldCapture != null)
            {
                try
                {
                    CapturedOverflowWorld world = worldCapture(monitor, desktop);
                    if (world != null)
                    {
                        worldRows = world.OtherMonitors;
                        overrides = world.PriorityOverrides;
                    }
                }
                catch (Exception ex)
                {
                    TraceLine("overflow world capture failed (redistribution disabled): " + ex.Message);
                }
            }

            // Compose the C2 snapshot over the target row (this run's own layout and
            // discovery) plus the captured rows; each row's local plan feeds the A2
            // stable/overflow classification. The rows always accumulate the global
            // fact/snapshot join the move executor's identity checks consume.
            CapturedMonitorRow[] rows = new CapturedMonitorRow[worldRows.Length + 1];
            rows[0] = new CapturedMonitorRow(monitor, zones, label, discovered);
            for (int i = 0; i < worldRows.Length; i++)
            {
                rows[i + 1] = worldRows[i];
            }
            List<WindowFact> allFacts = new List<WindowFact>();
            List<WindowSnapshot> allSnapshots = new List<WindowSnapshot>();
            List<EnumeratedWindow> allWindows = new List<EnumeratedWindow>();
            CrossMonitorPlan redistribution = new CrossMonitorPlan(new CrossMonitorMove[0], new string[0]);
            if (worldRows.Length > 0)
            {
                MonitorInfo[] rowMonitors = new MonitorInfo[rows.Length];
                Zone[][] rowZones = new Zone[rows.Length][];
                string[] rowLabels = new string[rows.Length];
                AssignmentPlan[] rowPlans = new AssignmentPlan[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    CapturedMonitorRow row = rows[i];
                    WindowFact[] rowFacts = row.Discovered == null ? new WindowFact[0] : row.Discovered.Facts;
                    WindowSnapshot[] rowSnapshots = row.Discovered == null
                        ? new WindowSnapshot[0] : row.Discovered.Snapshots;
                    rowMonitors[i] = row.Monitor;
                    rowZones[i] = row.Zones;
                    rowLabels[i] = row.Label;
                    rowPlans[i] = i == 0 ? initial : assign(row.Zones, rowFacts, managerWindowName);
                    foreach (WindowFact fact in rowFacts)
                    {
                        if (fact == null)
                        {
                            continue;
                        }
                        allFacts.Add(fact);
                        WindowSnapshot rowSnapshot = FindSnapshotByHandle(rowSnapshots, fact.Handle);
                        allSnapshots.Add(rowSnapshot);
                        // Discovery already established current-desktop membership: the
                        // synthesized row carries a verified desktop status plus the
                        // snapshot identity — the composer's two enumerated-row inputs.
                        allWindows.Add(new EnumeratedWindow(fact.Handle, row.Monitor,
                            DesktopFlagResult.Ok(true), rowSnapshot == null ? null : rowSnapshot.Identity));
                    }
                }
                CrossMonitorSnapshot composed = CrossMonitorSnapshotComposer.Compose(desktop, rowMonitors,
                    rowZones, rowLabels, rowPlans, allWindows.ToArray(), allFacts.ToArray(),
                    allSnapshots.ToArray(), overrides);
                redistribution = CrossMonitorPlanner.Plan(composed);
            }

            OrganizePlan plan = new OrganizePlan(initial, mergePlan, redistribution, null);
            LastOperationStatus preliminary = BuildStatus(startedUtc, label,
                acquisitionSkips > 0 ? OperationDisposition.Degraded : OperationDisposition.Completed,
                discovered, initial, outcomes.ToArray(), noPlacements, false, logPath, null);
            return new PreparedOrganizeRun(request, plan, discovered, preliminary, null,
                monitor, desktop, layout, managerWindowName, label, logPath, startedUtc,
                commitGuard, mergeEnabled, rows, allFacts.ToArray(), allSnapshots.ToArray());
        }

        /// <summary>
        /// The C3 mutation phase: FIRST validates the captured commit signature (the
        /// choice dialog may have remained open while the monitors or layout changed),
        /// then applies exactly ONE policy. Stack skips merge and redistribution and
        /// applies the final local assignment; Merge executes the planned merges (only
        /// while the gate captured at Prepare says enabled — otherwise it degrades to
        /// Stack with exactly one warning entry); Redistribute applies the C2 moves in
        /// plan order first, reacquires/recomputes the affected monitors' local
        /// assignments, then applies the remaining local placement.
        /// </summary>
        private OrganizeRunResult CommitCore(PreparedOrganizeRun prepared, OverflowPolicy policy,
            CancellationToken cancellationToken)
        {
            if (prepared.Plan == null)
            {
                // Prepare stopped at a gate; the stopped outcome replays unchanged.
                return prepared.TerminalResult == null
                    ? new OrganizeRunResult(new MergeOutcome[0], 0, null, new WindowPlacementResult[0], false, false, null)
                    : prepared.TerminalResult;
            }
            DateTime startedUtc = prepared.StartedUtc;
            string label = prepared.MonitorLabel;
            string logPath = prepared.LogPath;
            Zone[] zones = prepared.Zones;
            DiscoveredWindows discovered = prepared.Discovered;
            WindowFact[] facts = discovered == null ? new WindowFact[0] : discovered.Facts;
            WindowSnapshot[] snapshots = discovered == null ? new WindowSnapshot[0] : discovered.Snapshots;
            int acquisitionSkips = discovered == null
                ? 0 : discovered.SkippedUnknownDesktopCount + discovered.SkippedUnknownStateCount;
            List<MergeOutcome> outcomes = new List<MergeOutcome>();
            List<string> mergedSourceIds = new List<string>();
            List<WindowPlacementResult> placementResults = new List<WindowPlacementResult>();
            int helperMissingSkips = 0;
            AssignmentPlan initial = prepared.Plan.LocalAssignment;

            OverflowPolicy effective = policy;
            if (policy == OverflowPolicy.Merge && !prepared.MergeGateEnabled)
            {
                // The saved policy may predate the release gate; merge is never silently
                // enabled — degrade to Stack with exactly ONE warning entry.
                Emit(NoticeKind.MergePolicyDisabled, null);
                effective = OverflowPolicy.Stack;
            }

            MutationGuard mutationGuard = delegate
            {
                return createGuard == null
                    || (prepared.Guard != null && prepared.Guard.IsCurrent());
            };

            // C3 FIRST: the commit-signature validation — a stale signature aborts with
            // zero mutation (nothing below runs).
            if (createGuard != null && prepared.Guard != null && !prepared.Guard.IsCurrent())
            {
                return GuardAborted(startedUtc, label, logPath, discovered, initial, outcomes,
                    placementResults, helperMissingSkips);
            }

            if (effective == OverflowPolicy.Merge)
            {
                // REQ-PIPE-001/005: probe every planned Remote merge BETWEEN planning and
                // execution; a definitive absence skips that merge with HelperMissing, a
                // probe failure fails open.
                List<PlannedMerge> executable = new List<PlannedMerge>();
                foreach (PlannedMerge merge in prepared.Plan.MergePlan.Merges)
                {
                    if (merge == null)
                    {
                        continue;
                    }
                    if (IsRemote(merge.Session))
                    {
                        bool? helperExists = ProbeGuarded(merge);
                        if (helperExists.HasValue && !helperExists.Value)
                        {
                            Emit(NoticeKind.HelperMissing, SessionName(merge));
                            helperMissingSkips++;
                            continue;
                        }
                    }
                    executable.Add(merge);
                }

                foreach (PlannedMerge merge in executable)
                {
                    // B4 checkpoint BETWEEN guarded mutations: an issued merge completes;
                    // cancellation prevents the NEXT one.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled(startedUtc, label, logPath, discovered, outcomes, placementResults.ToArray());
                    }
                    if (!mutationGuard())
                    {
                        return GuardAborted(startedUtc, label, logPath, discovered, initial, outcomes,
                            placementResults, helperMissingSkips);
                    }
                    MergeOutcome outcome = mergeExecutor(merge);
                    if (outcome == null)
                    {
                        continue;
                    }
                    outcomes.Add(outcome);
                    EmitMergeNotice(outcome, merge);
                    if (outcome.Kind == MergeOutcomeKind.Merged && outcome.SourceWindowId != null)
                    {
                        mergedSourceIds.Add(outcome.SourceWindowId);
                    }
                }
            }

            AssignmentPlan finalPlan = initial;
            if (effective == OverflowPolicy.Redistribute)
            {
                // The C2 moves first, in plan (priority) order, each behind its own
                // cancellation/guard checkpoint.
                foreach (CrossMonitorMove move in prepared.Plan.RedistributionPlan.Moves)
                {
                    if (move == null)
                    {
                        continue;
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled(startedUtc, label, logPath, discovered, outcomes, placementResults.ToArray());
                    }
                    if (!mutationGuard())
                    {
                        return GuardAborted(startedUtc, label, logPath, discovered, initial, outcomes,
                            placementResults, helperMissingSkips);
                    }
                    WindowPlacementResult moved = crossMoveExecutor == null
                        ? new WindowPlacementResult(move.Handle, move.WindowId, true, false, "no cross-monitor executor")
                        : crossMoveExecutor(move, mutationGuard, prepared.AllSnapshots, cancellationToken);
                    placementResults.Add(moved);
                    if (moved != null && moved.Error == "topology-aborted")
                    {
                        return GuardAborted(startedUtc, label, logPath, discovered, initial, outcomes,
                            placementResults, helperMissingSkips);
                    }
                }
                // Reacquire/recompute the AFFECTED monitors (the target always, plus
                // every other monitor the moves touched), then the remaining placement.
                foreach (CapturedMonitorRow row in AffectedRows(prepared))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled(startedUtc, label, logPath, discovered, outcomes, placementResults.ToArray());
                    }
                    DiscoveredWindows rediscovered = windows(row.Monitor, prepared.Desktop);
                    WindowFact[] rowFacts = rediscovered == null ? new WindowFact[0] : rediscovered.Facts;
                    WindowFact[] rowSurvivors = FilterSurvivors(rowFacts, mergedSourceIds);
                    AssignmentPlan rowPlan = assign(row.Zones, rowSurvivors, prepared.ManagerWindowName);
                    if (IsTargetRow(prepared, row))
                    {
                        finalPlan = rowPlan;
                    }
                    OrganizeRunResult stopped = ApplyPlacement(rowPlan,
                        rediscovered == null ? snapshots : rediscovered.Snapshots, mutationGuard,
                        placementResults, cancellationToken, startedUtc, label, logPath, discovered,
                        outcomes, helperMissingSkips);
                    if (stopped != null)
                    {
                        return stopped;
                    }
                }
            }
            else
            {
                // Survivor reassignment: failed-merge windows simply remain and are
                // stacked by the plan (no merges ran for Stack, so the survivors are
                // the discovered facts — the pinned double-assign behaviour).
                WindowFact[] survivors = FilterSurvivors(facts, mergedSourceIds);
                finalPlan = assign(zones, survivors, prepared.ManagerWindowName);
                OrganizeRunResult stopped = ApplyPlacement(finalPlan, snapshots, mutationGuard,
                    placementResults, cancellationToken, startedUtc, label, logPath, discovered,
                    outcomes, helperMissingSkips);
                if (stopped != null)
                {
                    return stopped;
                }
            }

            OperationDisposition disposition = acquisitionSkips > 0
                ? OperationDisposition.Degraded : OperationDisposition.Completed;
            return new OrganizeRunResult(outcomes.ToArray(), helperMissingSkips, finalPlan,
                placementResults.ToArray(), true, false,
                BuildStatus(startedUtc, label, disposition, discovered, finalPlan, outcomes.ToArray(),
                    placementResults.ToArray(), false, logPath, null));
        }

        /// <summary>
        /// One guarded placement pass (REQ-PIPE-002 idempotency: an already-organized
        /// screen plans no moves, so the mutating placer is not called at all). Returns
        /// the stopped result on cancellation / a guard or placer topology abort, null
        /// when the pass completed.
        /// </summary>
        private OrganizeRunResult ApplyPlacement(AssignmentPlan plan, WindowSnapshot[] identitySnapshots,
            MutationGuard mutationGuard, List<WindowPlacementResult> placementResults,
            CancellationToken cancellationToken, DateTime startedUtc, string label, string logPath,
            DiscoveredWindows discovered, List<MergeOutcome> outcomes, int helperMissingSkips)
        {
            if (!HasRequiredMove(plan))
            {
                return null;
            }
            // B4 checkpoint immediately before the mutation phase.
            if (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(startedUtc, label, logPath, discovered, outcomes, placementResults.ToArray());
            }
            if (!mutationGuard())
            {
                return GuardAborted(startedUtc, label, logPath, discovered, plan, outcomes,
                    placementResults, helperMissingSkips);
            }
            WindowPlacementResult[] results = guardedPlacer == null
                ? placer(plan) : guardedPlacer(plan, mutationGuard, identitySnapshots);
            // A PS-typed fake placer returning @() marshals as null — every consumption
            // defends with the empty-array fallback (legacy contract).
            results = results ?? new WindowPlacementResult[0];
            // B3: every placement failure is logged separately BEFORE the aggregate
            // notice (the trace port is the production log; never a balloon).
            foreach (WindowPlacementResult placement in results ?? new WindowPlacementResult[0])
            {
                if (placement != null && !placement.Skipped && !placement.Success)
                {
                    TraceLine("placement failed: " + placement);
                }
            }
            placementResults.AddRange(results);
            foreach (WindowPlacementResult placement in results ?? new WindowPlacementResult[0])
            {
                if (placement != null && placement.Error == "topology-aborted")
                {
                    return GuardAborted(startedUtc, label, logPath, discovered, plan, outcomes,
                        placementResults, helperMissingSkips);
                }
            }
            int placedCount;
            int placementFailedCount;
            CountPlacements(results, out placedCount, out placementFailedCount);
            if (placementFailedCount > 0)
            {
                Emit(NoticeKind.PlacementFailed, placementFailedCount.ToString(CultureInfo.InvariantCulture));
            }
            return null;
        }

        /// <summary>The A4 guard stop: the TopologyChanged notice plus the aborted
        /// result carrying whatever completed before the guard failed.</summary>
        private OrganizeRunResult GuardAborted(DateTime startedUtc, string label, string logPath,
            DiscoveredWindows discovered, AssignmentPlan plan, List<MergeOutcome> outcomes,
            List<WindowPlacementResult> placementResults, int helperMissingSkips)
        {
            Emit(NoticeKind.TopologyChanged, null);
            return new OrganizeRunResult(outcomes.ToArray(), helperMissingSkips, plan,
                placementResults.ToArray(), false, true,
                BuildStatus(startedUtc, label, OperationDisposition.Aborted, discovered, plan,
                    outcomes.ToArray(), placementResults.ToArray(), true, logPath, null));
        }

        /// <summary>
        /// The monitors a Redistribute commit must reacquire (C3): the target row plus
        /// every other captured row whose stable key appears as a move source or
        /// destination, in captured row order.
        /// </summary>
        private static CapturedMonitorRow[] AffectedRows(PreparedOrganizeRun prepared)
        {
            List<CapturedMonitorRow> affected = new List<CapturedMonitorRow>();
            foreach (CapturedMonitorRow row in prepared.Rows)
            {
                if (row == null || row.Monitor == null || row.Monitor.StableKey == null)
                {
                    continue;
                }
                if (IsTargetRow(prepared, row))
                {
                    affected.Add(row);
                    continue;
                }
                foreach (CrossMonitorMove move in prepared.Plan.RedistributionPlan.Moves)
                {
                    if (move == null)
                    {
                        continue;
                    }
                    if (string.Equals(move.SourceMonitorKey, row.Monitor.StableKey.CanonicalValue, StringComparison.Ordinal)
                        || string.Equals(move.DestinationMonitorKey, row.Monitor.StableKey.CanonicalValue, StringComparison.Ordinal))
                    {
                        affected.Add(row);
                        break;
                    }
                }
            }
            return affected.ToArray();
        }

        private static bool IsTargetRow(PreparedOrganizeRun prepared, CapturedMonitorRow row)
        {
            return prepared.Monitor != null && prepared.Monitor.StableKey != null
                && row.Monitor != null && row.Monitor.StableKey != null
                && row.Monitor.StableKey.EqualsKey(prepared.Monitor.StableKey);
        }

        private static WindowSnapshot FindSnapshotByHandle(WindowSnapshot[] snapshots, IntPtr handle)
        {
            foreach (WindowSnapshot snapshot in snapshots ?? new WindowSnapshot[0])
            {
                if (snapshot != null && snapshot.Handle == handle)
                {
                    return snapshot;
                }
            }
            return null;
        }

        /// <summary>The B4 cancelled-run shape: stopped at a checkpoint, nothing further
        /// mutated, an Aborted status whose detail names the cancellation.</summary>
        private static OrganizeRunResult Cancelled(DateTime startedUtc, string label, string logPath,
            DiscoveredWindows discovered, List<MergeOutcome> outcomes, WindowPlacementResult[] placementResults)
        {
            MergeOutcome[] completed = outcomes == null ? new MergeOutcome[0] : outcomes.ToArray();
            return new OrganizeRunResult(completed, 0, null,
                placementResults == null ? new WindowPlacementResult[0] : placementResults, false, false,
                BuildStatus(startedUtc, label, OperationDisposition.Aborted, discovered, null, completed,
                    placementResults, false, logPath, "cancelled"));
        }

        private static bool IsRemote(SessionRecord session)
        {
            return session != null && session.Kind == SessionKind.Remote;
        }

        /// <summary>The merge's session name; the source window id when no session rides the plan.</summary>
        private static string SessionName(PlannedMerge merge)
        {
            if (merge.Session != null && !string.IsNullOrEmpty(merge.Session.Name))
            {
                return merge.Session.Name;
            }
            return merge.SourceWindowId;
        }

        /// <summary>REQ-PIPE-005 fail-open: a probe that throws counts as a failed probe, not an absent helper.</summary>
        private bool? ProbeGuarded(PlannedMerge merge)
        {
            try
            {
                return helperProbe(merge);
            }
            catch (Exception ex)
            {
                TraceLine("helper probe failed (fail-open, merge proceeds): " + ex.Message);
                return null;
            }
        }

        /// <summary>Unsupported/Invalid stop the run; the notice carries the layout type or the invalid detail (REQ-PIPE-001).</summary>
        private void EmitLayoutNotice(LayoutResult layout)
        {
            if (layout == null)
            {
                Emit(NoticeKind.UnsupportedLayout, string.Empty);
                return;
            }
            if (layout.Kind == LayoutResultKind.Unsupported)
            {
                if (layout.Reason == LayoutReason.NoAppliedLayout)
                {
                    Emit(NoticeKind.NoAppliedLayout, null);
                    return;
                }
                Emit(NoticeKind.UnsupportedLayout, layout.LayoutType);
                return;
            }
            Emit(NoticeKind.InvalidLayout, layout.Detail);
        }

        /// <summary>ManagerSkipped fires only when a choice existed but could not be honored ("no choice" skipped nothing).</summary>
        private void EmitManagerNotice(AssignmentPlan plan, string managerWindowName, string monitorLabel)
        {
            if (plan == null || string.IsNullOrEmpty(managerWindowName))
            {
                return;
            }
            if (!plan.ManagerPinned)
            {
                Emit(NoticeKind.ManagerSkipped, managerWindowName, monitorLabel);
            }
        }

        private void EmitMergeNotice(MergeOutcome outcome, PlannedMerge merge)
        {
            if (outcome.Kind == MergeOutcomeKind.SafetyCheckFailed)
            {
                Emit(NoticeKind.MergeSafetyAborted, SessionName(merge));
                return;
            }
            if (outcome.Kind == MergeOutcomeKind.ConfirmTimeout
                || outcome.Kind == MergeOutcomeKind.LaunchFailed)
            {
                Emit(NoticeKind.MergeNotConfirmed, SessionName(merge));
            }
            else if (outcome.Kind == MergeOutcomeKind.ConfirmedNotClosed)
            {
                Emit(NoticeKind.MergeConfirmedNotClosed, SessionName(merge));
            }
        }

        /// <summary>B3 emission: the message sink carries level and balloon policy; the
        /// legacy string sink receives the identical resolved text when no message sink is set.</summary>
        private void Emit(NoticeKind kind, string arg)
        {
            EmitMessage(NoticeTable.Get(kind, arg));
        }

        private void Emit(NoticeKind kind, string arg, string monitorLabel)
        {
            EmitMessage(NoticeTable.Get(kind, arg, monitorLabel));
        }

        private void EmitMessage(NoticeMessage message)
        {
            NoticeMessageSink sink = messageSink;
            if (sink != null)
            {
                sink(message);
                return;
            }
            notify(message.Text);
        }

        /// <summary>The display label for the status and the {monitor} notices (B3): the
        /// caller-supplied label wins; otherwise a best-effort derivation from the single
        /// monitor (the full-set derivation lives with callers that know all monitors).</summary>
        private static string ResolveMonitorLabel(MonitorInfo monitor, string monitorLabel)
        {
            if (monitorLabel != null)
            {
                return monitorLabel;
            }
            if (monitor == null)
            {
                return string.Empty;
            }
            return TrayMenuBuilder.DerivePhysicalLabel(monitor, new MonitorInfo[] { monitor });
        }

        /// <summary>Builds the structured status (B3) from the run's terminal state: the
        /// discovery counters, the final plan's skip/unchanged/stacked moves, the merge
        /// outcomes and the placement results. Count rules pinned in the design: each
        /// window counted exactly once per category; Failed = PlacementFailed + MergeFailed.</summary>
        private static LastOperationStatus BuildStatus(DateTime startedUtc, string monitorLabel,
            OperationDisposition disposition, DiscoveredWindows discovered, AssignmentPlan plan,
            MergeOutcome[] outcomes, WindowPlacementResult[] placements, bool topologyChanged,
            string logPath, string detail)
        {
            int skippedUnknownDesktop = discovered == null ? 0 : discovered.SkippedUnknownDesktopCount;
            int skippedUnknownState = discovered == null ? 0 : discovered.SkippedUnknownStateCount;
            int discoveredCount = (discovered == null ? 0 : discovered.Facts.Length)
                + skippedUnknownDesktop + skippedUnknownState;
            int skippedFullScreen = 0;
            int unchanged = 0;
            int stacked = 0;
            if (plan != null)
            {
                foreach (PlannedMove move in plan.Moves)
                {
                    if (move == null)
                    {
                        continue;
                    }
                    if (move.SkipReason == PlannedMoveSkipReason.FullScreen)
                    {
                        skippedFullScreen++;
                    }
                    else if (!move.MoveRequired && move.SkipReason == PlannedMoveSkipReason.None)
                    {
                        unchanged++;
                    }
                    if (move.Stacked)
                    {
                        stacked++;
                    }
                }
            }
            int merged = 0;
            int mergeFailed = 0;
            if (outcomes != null)
            {
                foreach (MergeOutcome outcome in outcomes)
                {
                    if (outcome == null)
                    {
                        continue;
                    }
                    if (outcome.Kind == MergeOutcomeKind.Merged)
                    {
                        merged++;
                    }
                    else if (outcome.Kind == MergeOutcomeKind.ConfirmTimeout
                        || outcome.Kind == MergeOutcomeKind.ConfirmedNotClosed
                        || outcome.Kind == MergeOutcomeKind.LaunchFailed
                        || outcome.Kind == MergeOutcomeKind.SafetyCheckFailed)
                    {
                        mergeFailed++;
                    }
                }
            }
            int placed;
            int placementFailed;
            CountPlacements(placements, out placed, out placementFailed);
            return new LastOperationStatus(startedUtc, DateTime.UtcNow, monitorLabel, disposition,
                discoveredCount, skippedUnknownDesktop, skippedUnknownState, skippedFullScreen,
                placed, unchanged, stacked, merged, mergeFailed, placementFailed,
                topologyChanged, logPath, detail);
        }

        /// <summary>Placed counts placement results with Skipped=false and Success=true;
        /// failures count Skipped=false and Success=false (skipped/rejected records count nowhere).</summary>
        private static void CountPlacements(WindowPlacementResult[] placements, out int placed, out int failed)
        {
            placed = 0;
            failed = 0;
            if (placements == null)
            {
                return;
            }
            foreach (WindowPlacementResult placement in placements)
            {
                if (placement == null || placement.Skipped)
                {
                    continue;
                }
                if (placement.Success)
                {
                    placed++;
                }
                else
                {
                    failed++;
                }
            }
        }

        private static WindowFact[] FilterSurvivors(WindowFact[] facts, List<string> mergedSourceIds)
        {
            if (mergedSourceIds.Count == 0)
            {
                return facts;
            }
            List<WindowFact> survivors = new List<WindowFact>();
            foreach (WindowFact fact in facts)
            {
                if (fact == null)
                {
                    continue;
                }
                bool merged = false;
                foreach (string id in mergedSourceIds)
                {
                    if (string.Equals(fact.Id, id, StringComparison.Ordinal))
                    {
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    survivors.Add(fact);
                }
            }
            return survivors.ToArray();
        }

        private static bool HasRequiredMove(AssignmentPlan plan)
        {
            if (plan == null)
            {
                return false;
            }
            foreach (PlannedMove move in plan.Moves)
            {
                if (move != null && move.MoveRequired)
                {
                    return true;
                }
            }
            return false;
        }

        private void TraceLine(string message)
        {
            Action<string> sink = trace;
            if (sink != null)
            {
                sink(message);
            }
        }
    }
}
