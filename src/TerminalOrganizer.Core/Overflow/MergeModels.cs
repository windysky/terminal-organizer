using System;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// How the executor addresses the merge target window (spec D-1): MRU-foreground is the
    /// default (SetForegroundWindow(target) then wt -w 0); titled is the parked fallback for
    /// when the launcher adopts named windows.
    /// </summary>
    public enum MergeTargetingMode
    {
        /// <summary>SetForegroundWindow(target), then wt -w 0 (most recently used window).</summary>
        Mru,

        /// <summary>wt -w &lt;window-title-prefix&gt; (parked; needs the terminal-auto-launcher go-ahead).</summary>
        Titled
    }

    /// <summary>
    /// One planned merge (REQ-MRG-001): the source window (id and handle), the target window
    /// (the zone's stack base; the manager is never a target, D-2), and the source's session
    /// record with the FULL original command line copied verbatim (launcher duplicate-detection
    /// contract). Immutable.
    /// </summary>
    public sealed class PlannedMerge
    {
        private readonly string sourceWindowId;
        private readonly IntPtr sourceHandle;
        private readonly string targetWindowId;
        private readonly IntPtr targetHandle;
        private readonly SessionRecord session;
        private readonly string commandLine;

        public PlannedMerge(string sourceWindowId, IntPtr sourceHandle,
            string targetWindowId, IntPtr targetHandle, SessionRecord session)
            : this(sourceWindowId, new WindowIdentity(sourceHandle, 0, 0, null, null),
                targetWindowId, new WindowIdentity(targetHandle, 0, 0, null, null), session, null)
        {
        }

        public PlannedMerge(string sourceWindowId, WindowIdentity sourceIdentity,
            string targetWindowId, WindowIdentity targetIdentity, SessionRecord session, string sourceTabTitle)
        {
            this.SourceIdentity = sourceIdentity;
            this.TargetIdentity = targetIdentity;
            this.SourceTabTitle = sourceTabTitle;
            this.sourceWindowId = sourceWindowId;
            this.sourceHandle = sourceIdentity == null ? IntPtr.Zero : sourceIdentity.Handle;
            this.targetWindowId = targetWindowId;
            this.targetHandle = targetIdentity == null ? IntPtr.Zero : targetIdentity.Handle;
            this.session = session;
            this.commandLine = session == null ? null : session.CommandLine;
        }

        public string SourceWindowId { get { return sourceWindowId; } }
        public WindowIdentity SourceIdentity { get; private set; }
        public WindowIdentity TargetIdentity { get; private set; }
        public string SourceTabTitle { get; private set; }
        public IntPtr SourceHandle { get { return sourceHandle; } }
        public string TargetWindowId { get { return targetWindowId; } }
        public IntPtr TargetHandle { get { return targetHandle; } }

        /// <summary>The source's session record; carries the session name used for title confirmation.</summary>
        public SessionRecord Session { get { return session; } }

        /// <summary>The source's full command line, verbatim from the session record (REQ-MRG-001).</summary>
        public string CommandLine { get { return commandLine; } }

        public override string ToString()
        {
            return string.Format("{0}->{1}|{2}", sourceWindowId, targetWindowId, session == null ? "-" : session.Name);
        }
    }

    /// <summary>
    /// The whole merge plan (REQ-MRG-001/002): the merges in stable plan order, plus the zone
    /// ids marked stack-only (a zone whose stack base is not identified plans no merges —
    /// stacking only, never merging into an unidentified window). Immutable; arrays return copies.
    /// </summary>
    public sealed class MergePlan
    {
        private readonly PlannedMerge[] merges;
        private readonly int[] stackOnlyZoneIds;

        public MergePlan(PlannedMerge[] merges, int[] stackOnlyZoneIds)
        {
            this.merges = merges == null ? new PlannedMerge[0] : (PlannedMerge[])merges.Clone();
            this.stackOnlyZoneIds = stackOnlyZoneIds == null ? new int[0] : (int[])stackOnlyZoneIds.Clone();
        }

        public PlannedMerge[] Merges
        {
            get { return (PlannedMerge[])merges.Clone(); }
        }

        /// <summary>Zones with stacked windows but no merge target (unidentified base), in plan order.</summary>
        public int[] StackOnlyZoneIds
        {
            get { return (int[])stackOnlyZoneIds.Clone(); }
        }

        public override string ToString()
        {
            return string.Format("merges={0} stack-only-zones={1}", merges.Length, string.Join(",", stackOnlyZoneIds));
        }
    }

    /// <summary>The terminal outcome kinds of one merge (REQ-SEQ-002): success, confirm-timeout
    /// abort, close-gate failure, or launch failure. Every abort leaves the source window open.</summary>
    public enum MergeOutcomeKind
    {
        /// <summary>Tab confirmed and source closed (D-3/D-4 both passed).</summary>
        Merged,

        /// <summary>Budget exhausted without a title match; source stays open.</summary>
        ConfirmTimeout,

        /// <summary>Confirmed but not closed (close-gate failure or the close never landed); the tab exists, manual cleanup note.</summary>
        ConfirmedNotClosed,

        /// <summary>The launch itself failed; source stays open.</summary>
        LaunchFailed,
        SafetyCheckFailed,
        Disabled,

        /// <summary>Attach-only drill (B5): the tab launched and the target delta was confirmed;
        /// the source verification and close steps were never entered.</summary>
        Attached
    }

    /// <summary>One merge's terminal outcome (REQ-SEQ-002): the source window id, the kind, and
    /// the reason detail. Immutable.</summary>
    public sealed class MergeOutcome
    {
        private readonly string sourceWindowId;
        private readonly MergeOutcomeKind kind;
        private readonly string detail;

        private MergeOutcome(string sourceWindowId, MergeOutcomeKind kind, string detail)
        {
            this.sourceWindowId = sourceWindowId;
            this.kind = kind;
            this.detail = detail;
        }

        public string SourceWindowId { get { return sourceWindowId; } }
        public MergeOutcomeKind Kind { get { return kind; } }
        public string Detail { get { return detail; } }

        public static MergeOutcome Merged(string sourceWindowId)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.Merged, null);
        }

        public static MergeOutcome ConfirmTimeout(string sourceWindowId)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.ConfirmTimeout, "confirm-timeout");
        }

        public static MergeOutcome ConfirmedNotClosed(string sourceWindowId)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.ConfirmedNotClosed, "confirmed-not-closed");
        }

        public static MergeOutcome LaunchFailed(string sourceWindowId, string detail)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.LaunchFailed, detail);
        }

        public static MergeOutcome SafetyCheckFailed(string sourceWindowId, string detail)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.SafetyCheckFailed, detail);
        }

        public static MergeOutcome Disabled(string sourceWindowId)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.Disabled, "merge disabled");
        }

        /// <summary>B5 attach-only drill: target delta confirmed and no source path was ever entered.</summary>
        public static MergeOutcome Attached(string sourceWindowId)
        {
            return new MergeOutcome(sourceWindowId, MergeOutcomeKind.Attached, "attach-only; source path never entered");
        }

        public override string ToString()
        {
            return string.Format("{0}|{1}|{2}", sourceWindowId, kind, detail == null ? "-" : detail);
        }
    }

    /// <summary>The phases of one merge's walk (REQ-SEQ-001). Merged and Aborted are terminal.</summary>
    public enum MergePhase
    {
        CapturingBaseline,
        AwaitingClose,
        /// <summary>Fresh step; the executor must launch the tab first.</summary>
        AwaitingLaunch,

        /// <summary>Launched; polling the target's tab titles for the session-name match.</summary>
        Polling,

        /// <summary>New target tab confirmed; re-inspect both identities, source session and target delta.</summary>
        ConfirmingSource,

        /// <summary>Close gate passed; WM_CLOSE has not yet been observed to take effect.</summary>
        Closing,

        /// <summary>Terminal: merged.</summary>
        Merged,

        /// <summary>Terminal: aborted (source left open).</summary>
        Aborted
    }

    /// <summary>What the executor should do next (REQ-SEQ-001); None on a terminal step.</summary>
    public enum MergeAction
    {
        CaptureBaseline,
        VerifyAndClose,
        ObserveClose,
        /// <summary>Start wt with the built new-tab arguments (MRU foreground first).</summary>
        Launch,

        /// <summary>Wait the poll interval, then read the target's tab titles.</summary>
        Poll,

        /// <summary>Terminal step; read the outcome.</summary>
        None
    }

    /// <summary>The kinds of observation feeding the sequencer (REQ-SEQ-001).</summary>
    public enum MergeObservationKind
    {
        Baseline,
        CloseGate,
        /// <summary>The launch attempt finished (success or failure).</summary>
        Launched,

        /// <summary>A tab-title read of the target with the remaining confirmation budget.</summary>
        Tabs,

        /// <summary>Whether the source window has closed after WM_CLOSE.</summary>
        Close
    }

    /// <summary>
    /// One observation (REQ-SEQ-001): the sequencer is pure, so time travels in as an input —
    /// the Tabs observation carries the remaining confirmation budget; the executor owns every
    /// clock. Only the fields named by the kind are meaningful. Immutable; Titles returns a copy.
    /// </summary>
    public sealed class MergeObservation
    {
        private readonly MergeObservationKind kind;
        private readonly bool launchSuccess;
        private readonly string launchError;
        private readonly string[] titles;
        private readonly int budgetRemainingMs;
        private readonly int sourceTabCount;
        private readonly bool sourceClosed;

        private MergeObservation(MergeObservationKind kind, bool launchSuccess, string launchError,
            string[] titles, int budgetRemainingMs, int sourceTabCount, bool sourceClosed)
        {
            this.kind = kind;
            this.launchSuccess = launchSuccess;
            this.launchError = launchError;
            this.titles = titles == null ? new string[0] : (string[])titles.Clone();
            this.budgetRemainingMs = budgetRemainingMs;
            this.sourceTabCount = sourceTabCount;
            this.sourceClosed = sourceClosed;
        }

        public MergeObservationKind Kind { get { return kind; } }
        public bool LaunchSuccess { get { return launchSuccess; } }
        public string LaunchError { get { return launchError; } }
        public string[] Titles { get { return (string[])titles.Clone(); } }

        /// <summary>Remaining confirmation budget in milliseconds at the moment of the observation.</summary>
        public int BudgetRemainingMs { get { return budgetRemainingMs; } }
        public int SourceTabCount { get { return sourceTabCount; } }
        public bool SourceClosed { get { return sourceClosed; } }
        public TargetTabBaseline TargetBaseline { get; private set; }
        public WindowIdentity TargetIdentity { get; private set; }
        public TabTitleResult TabResult { get; private set; }
        public bool ClosePosted { get; private set; }
        public string Detail { get; private set; }

        public static MergeObservation Baseline(TargetTabBaseline baseline)
        {
            MergeObservation result = new MergeObservation(MergeObservationKind.Baseline, false, null, null, 0, 0, false);
            result.TargetBaseline = baseline;
            return result;
        }

        public static MergeObservation Launched(bool success, string error)
        {
            return new MergeObservation(MergeObservationKind.Launched, success, error, null, 0, 0, false);
        }

        public static MergeObservation Tabs(WindowIdentity targetIdentity, TabTitleResult tabs, int budgetRemainingMs)
        {
            MergeObservation result = new MergeObservation(MergeObservationKind.Tabs, false, null, tabs == null ? null : tabs.Titles, budgetRemainingMs, 0, false);
            result.TargetIdentity = targetIdentity;
            result.TabResult = tabs;
            return result;
        }

        public static MergeObservation CloseGate(bool closePosted, string detail)
        {
            MergeObservation result = new MergeObservation(MergeObservationKind.CloseGate, false, null, null, 0, 0, false);
            result.ClosePosted = closePosted;
            result.Detail = detail;
            return result;
        }

        public static MergeObservation Close(bool closed)
        {
            return new MergeObservation(MergeObservationKind.Close, false, null, null, 0, 0, closed);
        }
    }

    /// <summary>
    /// The immutable state of one merge's walk (REQ-SEQ-001): the planned merge, the phase,
    /// the next pending action, the pinned poll/budget parameters (D-3; no timers live here —
    /// time is an observation input), the poll count so far, and the terminal outcome once
    /// reached. The sequencer produces new steps; it never mutates one.
    /// </summary>
    public sealed class MergeStep
    {
        private readonly PlannedMerge merge;
        private readonly MergePhase phase;
        private readonly MergeAction pendingAction;
        private readonly int pollIntervalMs;
        private readonly int budgetMs;
        private readonly int pollCount;
        private readonly MergeOutcome outcome;

        public MergeStep(PlannedMerge merge, MergePhase phase, MergeAction pendingAction,
            int pollIntervalMs, int budgetMs, int pollCount, MergeOutcome outcome)
            : this(merge, phase, pendingAction, pollIntervalMs, budgetMs, pollCount, outcome, null)
        {
        }

        public MergeStep(PlannedMerge merge, MergePhase phase, MergeAction pendingAction,
            int pollIntervalMs, int budgetMs, int pollCount, MergeOutcome outcome, TargetTabBaseline baseline)
        {
            this.Baseline = baseline;
            this.merge = merge;
            this.phase = phase;
            this.pendingAction = pendingAction;
            this.pollIntervalMs = pollIntervalMs;
            this.budgetMs = budgetMs;
            this.pollCount = pollCount;
            this.outcome = outcome;
        }

        public PlannedMerge Merge { get { return merge; } }
        public TargetTabBaseline Baseline { get; private set; }
        public MergePhase Phase { get { return phase; } }

        /// <summary>What the executor should do now; None once terminal.</summary>
        public MergeAction PendingAction { get { return pendingAction; } }
        public int PollIntervalMs { get { return pollIntervalMs; } }
        public int BudgetMs { get { return budgetMs; } }
        public int PollCount { get { return pollCount; } }

        /// <summary>The terminal outcome; null until the step reaches Merged or Aborted.</summary>
        public MergeOutcome Outcome { get { return outcome; } }

        public bool IsTerminal
        {
            get { return phase == MergePhase.Merged || phase == MergePhase.Aborted; }
        }

        public override string ToString()
        {
            return string.Format("{0}|{1}|next={2}|polls={3}", merge, phase, pendingAction, pollCount);
        }
    }

    /// <summary>Injectable seam: reads one window's ordered tab titles (production default: the
    /// SPEC-WIN-004 UIA reader).</summary>
    public delegate TabTitleResult TabTitleRead(IntPtr window);

    /// <summary>Injectable seam: executes one planned merge to a terminal outcome (REQ-EXE-001).</summary>
    public delegate MergeOutcome MergeExecution(PlannedMerge merge);

    /// <summary>Injectable seam: recomputes the assignment plan (production default: SPEC-ZONE-005 ZoneAssigner).</summary>
    public delegate AssignmentPlan AssignmentComputation(Zone[] zones, WindowFact[] windows, string managerWindowName);

    /// <summary>Injectable seam: applies one assignment plan (production default: SPEC-ZONE-005 WindowPlacer).</summary>
    public delegate WindowPlacementResult[] PlacementPass(AssignmentPlan plan);
}
