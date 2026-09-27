using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// The result of one full overflow run (REQ-EXE-002): one outcome per planned merge in
    /// plan order, the recomputed assignment plan for the surviving windows, the results of
    /// the single placement pass, and a run-level error string when the run degraded instead
    /// of throwing. Immutable; arrays return copies.
    /// </summary>
    public sealed class OverflowRunResult
    {
        private readonly MergeOutcome[] outcomes;
        private readonly AssignmentPlan recomputedPlan;
        private readonly WindowPlacementResult[] placementResults;
        private readonly string error;

        public OverflowRunResult(MergeOutcome[] outcomes, AssignmentPlan recomputedPlan,
            WindowPlacementResult[] placementResults, string error)
        {
            this.outcomes = outcomes == null ? new MergeOutcome[0] : (MergeOutcome[])outcomes.Clone();
            this.recomputedPlan = recomputedPlan;
            this.placementResults = placementResults == null ? new WindowPlacementResult[0] : (WindowPlacementResult[])placementResults.Clone();
            this.error = error;
        }

        public MergeOutcome[] Outcomes
        {
            get { return (MergeOutcome[])outcomes.Clone(); }
        }

        /// <summary>The recomputed plan the single placement pass consumed; null on a degraded run.</summary>
        public AssignmentPlan RecomputedPlan { get { return recomputedPlan; } }
        public WindowPlacementResult[] PlacementResults
        {
            get { return (WindowPlacementResult[])placementResults.Clone(); }
        }

        /// <summary>The run-level failure detail when the run degraded; null on a normal run.</summary>
        public string Error { get { return error; } }

        public override string ToString()
        {
            return string.Format("outcomes={0} placement={1} error={2}", outcomes.Length, placementResults.Length, error == null ? "-" : error);
        }
    }

    /// <summary>
    /// Runs the overflow cycle (REQ-EXE-002, spec D-5): plan the merges, execute them STRICTLY
    /// sequentially (one in flight; focus-dependent targeting forbids concurrency), then
    /// recompute the assignment for the surviving windows — windows of failed merges simply
    /// remain and are stacked by that plan — and issue exactly ONE placement pass over the
    /// survivors. The merge executor, the assignment recomputation and the placement pass are
    /// injectable seams (production defaults: MergeExecutor, SPEC-ZONE-005 ZoneAssigner and
    /// WindowPlacer). The run never throws outward; every failure degrades to a result.
    /// </summary>
    // @MX:WARN: orchestrates live window mutation (plan.md H) — merging closes windows; morning checklist via tools/merge-test.ps1, never auto-run overnight.
    public sealed class OverflowRunner
    {
        private readonly MergeExecution mergeExecutor;
        private readonly AssignmentComputation assigner;
        private readonly PlacementPass placer;

        /// <summary>Production wiring: the real executor, ZoneAssigner and WindowPlacer.</summary>
        public OverflowRunner()
        {
            this.mergeExecutor = new MergeExecution(new MergeExecutor().RunMerge);
            this.assigner = new AssignmentComputation(AssignDefault);
            this.placer = new PlacementPass(new WindowPlacer().Apply);
        }

        public OverflowRunner(MergeExecution mergeExecutor, AssignmentComputation assigner, PlacementPass placer)
        {
            if (mergeExecutor == null)
            {
                throw new ArgumentNullException("mergeExecutor");
            }
            if (assigner == null)
            {
                throw new ArgumentNullException("assigner");
            }
            if (placer == null)
            {
                throw new ArgumentNullException("placer");
            }
            this.mergeExecutor = mergeExecutor;
            this.assigner = assigner;
            this.placer = placer;
        }

        public OverflowRunResult Run(Zone[] zones, WindowFact[] windows, string managerWindowName,
            AssignmentPlan assignmentPlan, WindowSnapshot[] snapshots)
        {
            try
            {
                MergePlan plan = MergePlanner.Plan(assignmentPlan, snapshots);

                List<MergeOutcome> outcomes = new List<MergeOutcome>();
                List<string> mergedSourceIds = new List<string>();
                // D-5: strictly sequential — the loop finishes one merge (to its terminal
                // outcome) before the next starts; there is no batch entry point anywhere.
                foreach (PlannedMerge merge in plan.Merges)
                {
                    if (merge == null)
                    {
                        continue;
                    }
                    MergeOutcome outcome = ExecuteGuarded(merge);
                    outcomes.Add(outcome);
                    if (outcome.Kind == MergeOutcomeKind.Merged && outcome.SourceWindowId != null)
                    {
                        mergedSourceIds.Add(outcome.SourceWindowId);
                    }
                }

                WindowFact[] survivors = CollectSurvivors(windows, mergedSourceIds);
                AssignmentPlan recomputed = assigner(zones, survivors, managerWindowName);
                WindowPlacementResult[] placement = placer(recomputed);
                return new OverflowRunResult(outcomes.ToArray(), recomputed, placement, null);
            }
            catch (Exception ex)
            {
                return new OverflowRunResult(new MergeOutcome[0], null, new WindowPlacementResult[0], ex.Message);
            }
        }

        /// <summary>The executor seam never throws outward either (REQ-EXE-001); a throwing
        /// fake degrades to a LaunchFailed outcome so later merges still run.</summary>
        private MergeOutcome ExecuteGuarded(PlannedMerge merge)
        {
            try
            {
                return mergeExecutor(merge);
            }
            catch (Exception ex)
            {
                return MergeOutcome.LaunchFailed(merge.SourceWindowId, ex.Message);
            }
        }

        /// <summary>Survivors: every input window except the successfully merged sources
        /// (their windows closed); failed-merge windows remain and stack (REQ-EXE-002).</summary>
        private static WindowFact[] CollectSurvivors(WindowFact[] windows, List<string> mergedSourceIds)
        {
            List<WindowFact> survivors = new List<WindowFact>();
            if (windows == null)
            {
                return survivors.ToArray();
            }
            foreach (WindowFact window in windows)
            {
                if (window == null)
                {
                    continue;
                }
                if (mergedSourceIds.Contains(window.Id))
                {
                    continue;
                }
                survivors.Add(window);
            }
            return survivors.ToArray();
        }

        private static AssignmentPlan AssignDefault(Zone[] zones, WindowFact[] windows, string managerWindowName)
        {
            return ZoneAssigner.Assign(zones, windows, managerWindowName);
        }
    }

    /// <summary>
    /// Applies one merge step on the live desktop (REQ-EXE-001, spec D-1/D-3/D-4): in MRU mode
    /// SetForegroundWindow(target) then Process.Start on the injectable launcher image
    /// (production default wt.exe) WITHOUT a shell; polls the target's tab titles through the
    /// SPEC-WIN-004 UIA reader at the poll interval within the confirmation budget; on the
    /// close step posts WM_CLOSE to the source — PostMessage only, never a hard process kill
    /// (plan.md G.1). The
    /// pure MergeSequencer owns every decision; this wrapper only performs actions and feeds
    /// observations (time included — the sequencer has no timers). Every failure surfaces as a
    /// step result / terminal outcome, never an exception.
    /// </summary>
    public sealed class MergeExecutor
    {
        /// <summary>Production launcher image (REQ-EXE-001: injectable parameter, default wt.exe).</summary>
        public const string DefaultLauncherImage = "wt.exe";

        /// <summary>WM_CLOSE (0x0010) — the only close mechanism (D-4; hard kills are forbidden).</summary>
        private const int WmClose = 0x0010;

        private readonly string launcherImage;
        private readonly MergeTargetingMode mode;
        private readonly string targetWindowTitle;
        private readonly WindowInspectionRead inspect;
        private readonly Func<IntPtr, bool> postClose;
        private readonly Func<ProcessStartInfo, Process> launchProcess;
        private readonly int pollIntervalMs;
        private readonly int budgetMs;

        /// <summary>Production constructor: wt.exe, MRU mode, the WIN-004 UIA reader, pinned defaults.</summary>
        public MergeExecutor()
            : this(DefaultLauncherImage, MergeTargetingMode.Mru, null, null,
            MergeSequencer.DefaultPollIntervalMs, MergeSequencer.DefaultConfirmationBudgetMs)
        {
        }

        public MergeExecutor(string launcherImage, MergeTargetingMode mode, string targetWindowTitle,
            TabTitleRead tabTitleReader, int pollIntervalMs, int budgetMs)
            : this(launcherImage, mode, targetWindowTitle,
                tabTitleReader == null ? new WindowInspectionRead(new WindowInspector().Inspect)
                    : delegate(IntPtr handle) { return new WindowInspection(WindowInspector.ReadIdentity(handle), tabTitleReader(handle)); },
                new Func<IntPtr, bool>(PostClose), pollIntervalMs, budgetMs)
        {
        }

        public MergeExecutor(WindowInspectionRead inspect, Func<IntPtr, bool> postClose)
            : this(DefaultLauncherImage, MergeTargetingMode.Mru, null, inspect, postClose,
                MergeSequencer.DefaultPollIntervalMs, MergeSequencer.DefaultConfirmationBudgetMs)
        {
        }

        /// <summary>B5 test seam: injectable launch. The production default really starts the
        /// launcher; a fake starts nothing, so attach-only rows stay deterministic.</summary>
        public MergeExecutor(WindowInspectionRead inspect, Func<IntPtr, bool> postClose,
            Func<ProcessStartInfo, Process> launchProcess, int pollIntervalMs, int budgetMs)
            : this(DefaultLauncherImage, MergeTargetingMode.Mru, null, inspect, postClose,
            launchProcess, pollIntervalMs, budgetMs)
        {
        }

        public MergeExecutor(string launcherImage, MergeTargetingMode mode, string targetWindowTitle,
            WindowInspectionRead inspect, Func<IntPtr, bool> postClose, int pollIntervalMs, int budgetMs)
            : this(launcherImage, mode, targetWindowTitle, inspect, postClose, null, pollIntervalMs, budgetMs)
        {
        }

        public MergeExecutor(string launcherImage, MergeTargetingMode mode, string targetWindowTitle,
            WindowInspectionRead inspect, Func<IntPtr, bool> postClose, Func<ProcessStartInfo, Process> launchProcess,
            int pollIntervalMs, int budgetMs)
        {
            if (inspect == null) throw new ArgumentNullException("inspect");
            if (postClose == null) throw new ArgumentNullException("postClose");
            if (string.IsNullOrEmpty(launcherImage))
            {
                throw new ArgumentNullException("launcherImage");
            }
            if (mode == MergeTargetingMode.Titled && string.IsNullOrEmpty(targetWindowTitle))
            {
                throw new ArgumentException("titled mode requires a window-title prefix", "targetWindowTitle");
            }
            if (pollIntervalMs < 250)
            {
                throw new ArgumentException("poll interval must be at least 250 ms (NFR-3)", "pollIntervalMs");
            }
            if (budgetMs < 1)
            {
                throw new ArgumentException("confirmation budget must be positive", "budgetMs");
            }
            this.launcherImage = launcherImage;
            this.mode = mode;
            this.targetWindowTitle = targetWindowTitle;
            this.inspect = inspect;
            this.postClose = postClose;
            this.launchProcess = launchProcess ?? new Func<ProcessStartInfo, Process>(StartLauncher);
            this.pollIntervalMs = pollIntervalMs;
            this.budgetMs = budgetMs;
        }

        /// <summary>Drives the sequencer loop for one merge to its terminal outcome; never throws.</summary>
        public MergeOutcome RunMerge(PlannedMerge merge)
        {
            try
            {
                if (merge == null)
                {
                    return MergeOutcome.LaunchFailed(null, "no merge given");
                }
                MergeStep step = MergeSequencer.Start(merge, pollIntervalMs, budgetMs);
                Stopwatch watch = Stopwatch.StartNew();
                while (!step.IsTerminal)
                {
                    switch (step.PendingAction)
                    {
                        case MergeAction.CaptureBaseline:
                            WindowInspection target = Inspect(merge.TargetHandle);
                            step = MergeSequencer.Next(step, MergeObservation.Baseline(
                                target != null && target.Tabs != null && target.Tabs.Trusted
                                ? new TargetTabBaseline(target.Identity, target.Tabs.Evidence) : null));
                            break;
                        case MergeAction.Launch:
                            watch.Restart();
                            step = MergeSequencer.Next(step, LaunchTab(merge));
                            break;
                        case MergeAction.Poll:
                            // NFR-3: wait the poll interval, then observe; no busy-wait.
                            Thread.Sleep(pollIntervalMs);
                            step = MergeSequencer.Next(step, ObserveTabs(merge, watch));
                            break;
                        case MergeAction.VerifyAndClose:
                            step = MergeSequencer.Next(step, VerifyAndClose(merge, step.Baseline));
                            break;
                        case MergeAction.ObserveClose:
                            step = MergeSequencer.Next(step, ObserveClose(merge));
                            break;
                        default:
                            return MergeOutcome.LaunchFailed(merge.SourceWindowId, "sequencer produced no pending action");
                    }
                }
                return step.Outcome ?? MergeOutcome.LaunchFailed(merge.SourceWindowId, "terminal step without an outcome");
            }
            catch (Exception ex)
            {
                return MergeOutcome.LaunchFailed(merge == null ? null : merge.SourceWindowId, ex.Message);
            }
        }

        /// <summary>
        /// D-1 launch: MRU mode brings the target to the foreground first so wt -w 0 (the most
        /// recently used window) addresses it; titled mode addresses wt -w &lt;title&gt;. The
        /// SetForegroundWindow foreground-lock can silently fail from a background process
        /// (plan G) — the title confirmation gates every close, so the launch proceeds anyway.
        /// Process.Start runs the injectable image without a shell. Failures return a
        /// failed-launch observation, never an exception.
        /// </summary>
        // @MX:WARN: [AUTO] real-screen surface — SetForegroundWindow + Process.Start; morning checklist, never auto-run overnight.
        private MergeObservation LaunchTab(PlannedMerge merge)
        {
            try
            {
                WindowInspection target = Inspect(merge.TargetHandle);
                if (target == null || !merge.TargetIdentity.EqualsForMutation(target.Identity)
                    || target.Tabs == null || !target.Tabs.Trusted)
                    return MergeObservation.Launched(false, "target identity or tab trust changed before launch");
                if (mode == MergeTargetingMode.Mru)
                {
                    NativeMethods.SetForegroundWindow(merge.TargetHandle);
                }
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = launcherImage;
                info.Arguments = BuildArguments(merge);
                info.UseShellExecute = false;
                Process launched = launchProcess(info);
                if (launched != null)
                {
                    // Dispose releases the Process object's handles only; it never kills the launched process.
                    launched.Dispose();
                }
                return MergeObservation.Launched(true, null);
            }
            catch (Exception ex)
            {
                return MergeObservation.Launched(false, ex.Message);
            }
        }

        /// <summary>The full wt argument line: window selector per mode + the built new-tab arguments.</summary>
        private string BuildArguments(PlannedMerge merge)
        {
            string newTab = MergeCommandBuilder.Build(merge.CommandLine);
            if (mode == MergeTargetingMode.Mru)
            {
                return "-w 0 " + newTab;
            }
            return "-w " + targetWindowTitle + " " + newTab;
        }

        /// <summary>D-3 poll observation: the target's tab titles plus the remaining budget.</summary>
        private MergeObservation ObserveTabs(PlannedMerge merge, Stopwatch watch)
        {
            WindowInspection read = Inspect(merge.TargetHandle);
            int remaining = budgetMs - (int)watch.ElapsedMilliseconds;
            return MergeObservation.Tabs(read == null ? null : read.Identity, read == null ? null : read.Tabs, remaining);
        }

        /// <summary>Re-inspect both windows and post WM_CLOSE only in this same guarded call.</summary>
        // @MX:WARN: the sole close gate; incomplete, changed or ambiguous evidence leaves the source open.
        // @MX:REASON: WM_CLOSE can close every tab in a window; all evidence is re-read before posting it.
        public MergeObservation VerifyAndClose(PlannedMerge merge, TargetTabBaseline baseline)
        {
            try
            {
                if (!MergeEvidence.ValidPlan(merge)) return MergeObservation.CloseGate(false, "invalid merge identity");
                WindowInspection source = Inspect(merge.SourceHandle);
                WindowInspection target = Inspect(merge.TargetHandle);
                SourceCloseEvidence evidence = MergeEvidence.EvaluateClose(merge, baseline, source, target);
                if (!evidence.MayClose) return MergeObservation.CloseGate(false, evidence.Detail);
                bool posted = postClose(merge.SourceHandle);
                return MergeObservation.CloseGate(posted, posted ? null : "WM_CLOSE could not be posted; inspect both windows");
            }
            catch (Exception ex) { return MergeObservation.CloseGate(false, ex.Message); }
        }

        private static bool PostClose(IntPtr handle)
        {
            return NativeMethods.PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        private static Process StartLauncher(ProcessStartInfo info)
        {
            return Process.Start(info);
        }

        /// <summary>
        /// B5 attach-only drill: captures the target baseline, launches the tab, and confirms
        /// the NEW target delta — then stops. The source verification and close steps are
        /// never entered (a zero-HWND source is not faked through the close state machine),
        /// and the close seam is never called. Baseline, launch and delta reuse the same
        /// seams and evidence rules (MergeEvidence.HasNewMatchingTab) as a full merge; only
        /// the walk is cut short. Never throws.
        /// </summary>
        public MergeOutcome RunAttachOnly(PlannedMerge merge)
        {
            try
            {
                if (merge == null)
                {
                    return MergeOutcome.LaunchFailed(null, "no merge given");
                }
                if (merge.TargetIdentity == null || !merge.TargetIdentity.EqualsForMutation(merge.TargetIdentity))
                {
                    return MergeOutcome.SafetyCheckFailed(merge.SourceWindowId, "target identity is not trusted");
                }
                if (merge.Session == null || string.IsNullOrEmpty(merge.Session.Name))
                {
                    return MergeOutcome.LaunchFailed(merge.SourceWindowId, "no session name to confirm");
                }
                WindowInspection before = Inspect(merge.TargetHandle);
                if (before == null || !merge.TargetIdentity.EqualsForMutation(before.Identity)
                    || before.Tabs == null || !before.Tabs.Trusted)
                {
                    return MergeOutcome.SafetyCheckFailed(merge.SourceWindowId, "target baseline is untrusted");
                }
                TargetTabBaseline baseline = new TargetTabBaseline(before.Identity, before.Tabs.Evidence);
                MergeObservation launched = LaunchTab(merge);
                if (!launched.LaunchSuccess)
                {
                    return MergeOutcome.LaunchFailed(merge.SourceWindowId, launched.LaunchError);
                }
                Stopwatch watch = Stopwatch.StartNew();
                while (true)
                {
                    // NFR-3: wait the poll interval, then observe; no busy-wait.
                    Thread.Sleep(pollIntervalMs);
                    WindowInspection read = Inspect(merge.TargetHandle);
                    if (read == null || !merge.TargetIdentity.EqualsForMutation(read.Identity))
                    {
                        return MergeOutcome.SafetyCheckFailed(merge.SourceWindowId, "target identity changed");
                    }
                    if (read.Tabs != null && read.Tabs.Trusted
                        && MergeEvidence.HasNewMatchingTab(baseline, read.Tabs, merge.Session.Name))
                    {
                        return MergeOutcome.Attached(merge.SourceWindowId);
                    }
                    if (budgetMs - (int)watch.ElapsedMilliseconds <= 0)
                    {
                        return MergeOutcome.ConfirmTimeout(merge.SourceWindowId);
                    }
                }
            }
            catch (Exception ex)
            {
                return MergeOutcome.LaunchFailed(merge == null ? null : merge.SourceWindowId, ex.Message);
            }
        }

        /// <summary>
        /// After the guarded WM_CLOSE, poll IsWindow within the close budget; a window that
        /// never vanishes reports close-observed-false (confirmed-not-closed), leaving it open.
        /// </summary>
        // @MX:NOTE: close observation performs no further mutation.
        private MergeObservation ObserveClose(PlannedMerge merge)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < budgetMs)
            {
                if (!NativeMethods.IsWindow(merge.SourceHandle))
                {
                    return MergeObservation.Close(true);
                }
                Thread.Sleep(pollIntervalMs);
            }
            return MergeObservation.Close(false);
        }

        private WindowInspection Inspect(IntPtr window)
        {
            try
            {
                return inspect(window);
            }
            catch (Exception ex)
            {
                return new WindowInspection(null, TabTitleResult.Failure(null, ex.Message));
            }
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; user32 needs no assembly reference).</summary>
        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr window);

            [DllImport("user32.dll")]
            public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool IsWindow(IntPtr window);
        }
    }
}
