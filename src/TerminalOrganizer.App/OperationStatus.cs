using System;
using System.Globalization;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The terminal outcome class of one organize run (B3): the completion ladder
    /// (Completed / NothingToDo — zero windows / Degraded — acquisition skips), the early
    /// stops (Aborted — the layout gate or the topology guard stopped the run with nothing
    /// mutated), Cancelled (reserved for the coordinator's cancellation), and Failed (an
    /// unexpected exception stopped the run; the UnexpectedFailure notice carries the text).
    /// </summary>
    public enum OperationDisposition
    {
        Completed,
        NothingToDo,
        Degraded,
        Aborted,
        Cancelled,
        Failed
    }

    /// <summary>
    /// One organize run's structured outcome (B3): timing, the monitor label, the
    /// disposition, the exact per-category window counts, the derived Failed total, and
    /// the pinned MenuSummary shown in the tray menu. Count rules (each window counted
    /// exactly once per category; stacked and placed are independent dimensions — a
    /// stacked window that moves is counted in both): Discovered = current-desktop
    /// windows before state exclusion; SkippedUnknownDesktop / SkippedUnknownState come
    /// from DiscoveredWindows; SkippedFullScreen / Unchanged / Stacked come from the
    /// final plan moves; Placed / PlacementFailed come from the placement results
    /// (Skipped=false only); Merged / MergeFailed come from the merge outcomes.
    /// Immutable.
    /// </summary>
    // @MX:ANCHOR: [AUTO] the B3 status surface behind the menu summary and the aggregate notices.
    // @MX:REASON: every organize completion funnels through this class; the count categories and MenuSummary order are pinned product behavior.
    public sealed class LastOperationStatus
    {
        private readonly DateTime startedUtc;
        private readonly DateTime completedUtc;
        private readonly string monitorLabel;
        private readonly OperationDisposition disposition;
        private readonly int discovered;
        private readonly int skippedUnknownDesktop;
        private readonly int skippedUnknownState;
        private readonly int skippedFullScreen;
        private readonly int placed;
        private readonly int unchanged;
        private readonly int stacked;
        private readonly int merged;
        private readonly int mergeFailed;
        private readonly int placementFailed;
        private readonly bool topologyChanged;
        private readonly string logPath;
        private readonly string detail;
        private readonly bool neverRun;

        public LastOperationStatus(
            DateTime startedUtc,
            DateTime completedUtc,
            string monitorLabel,
            OperationDisposition disposition,
            int discovered,
            int skippedUnknownDesktop,
            int skippedUnknownState,
            int skippedFullScreen,
            int placed,
            int unchanged,
            int stacked,
            int merged,
            int mergeFailed,
            int placementFailed,
            bool topologyChanged,
            string logPath,
            string detail)
            : this(startedUtc, completedUtc, monitorLabel, disposition, discovered, skippedUnknownDesktop,
                skippedUnknownState, skippedFullScreen, placed, unchanged, stacked, merged, mergeFailed,
                placementFailed, topologyChanged, logPath, detail, false)
        {
        }

        private LastOperationStatus(
            DateTime startedUtc,
            DateTime completedUtc,
            string monitorLabel,
            OperationDisposition disposition,
            int discovered,
            int skippedUnknownDesktop,
            int skippedUnknownState,
            int skippedFullScreen,
            int placed,
            int unchanged,
            int stacked,
            int merged,
            int mergeFailed,
            int placementFailed,
            bool topologyChanged,
            string logPath,
            string detail,
            bool neverRun)
        {
            this.startedUtc = startedUtc;
            this.completedUtc = completedUtc;
            this.monitorLabel = monitorLabel;
            this.disposition = disposition;
            this.discovered = discovered;
            this.skippedUnknownDesktop = skippedUnknownDesktop;
            this.skippedUnknownState = skippedUnknownState;
            this.skippedFullScreen = skippedFullScreen;
            this.placed = placed;
            this.unchanged = unchanged;
            this.stacked = stacked;
            this.merged = merged;
            this.mergeFailed = mergeFailed;
            this.placementFailed = placementFailed;
            this.topologyChanged = topologyChanged;
            this.logPath = logPath;
            this.detail = detail;
            this.neverRun = neverRun;
        }

        public DateTime StartedUtc { get { return startedUtc; } }
        public DateTime CompletedUtc { get { return completedUtc; } }
        public string MonitorLabel { get { return monitorLabel; } }
        public OperationDisposition Disposition { get { return disposition; } }
        public int Discovered { get { return discovered; } }
        public int SkippedUnknownDesktop { get { return skippedUnknownDesktop; } }
        public int SkippedUnknownState { get { return skippedUnknownState; } }
        public int SkippedFullScreen { get { return skippedFullScreen; } }
        public int Placed { get { return placed; } }
        public int Unchanged { get { return unchanged; } }
        public int Stacked { get { return stacked; } }
        public int Merged { get { return merged; } }
        public int MergeFailed { get { return mergeFailed; } }
        public int PlacementFailed { get { return placementFailed; } }

        /// <summary>PlacementFailed + MergeFailed — derived, never double-counted (the categories are disjoint).</summary>
        public int Failed { get { return placementFailed + mergeFailed; } }

        public long ElapsedMilliseconds
        {
            get { return (long)(completedUtc - startedUtc).TotalMilliseconds; }
        }

        public bool TopologyChanged { get { return topologyChanged; } }
        public string LogPath { get { return logPath; } }
        public string Detail { get { return detail; } }

        /// <summary>
        /// The pinned menu row (B3): "Last result: {placed} placed, {unchanged} unchanged,
        /// {stacked} stacked, {skipped} skipped, {merged} merged, {failed} failed" — where
        /// {skipped} is the union of the three skip buckets (unknown desktop, unknown
        /// state, full screen).
        /// </summary>
        public string MenuSummary
        {
            get
            {
                if (neverRun)
                {
                    return "Last result: Not run yet";
                }
                return string.Format(CultureInfo.InvariantCulture,
                    "Last result: {0} placed, {1} unchanged, {2} stacked, {3} skipped, {4} merged, {5} failed",
                    placed, unchanged, stacked,
                    skippedUnknownDesktop + skippedUnknownState + skippedFullScreen,
                    merged, Failed);
            }
        }

        /// <summary>The pre-first-run status: zero counts and the pinned not-run summary ("Last result: Not run yet").</summary>
        public static LastOperationStatus NeverRun(string logPath)
        {
            return new LastOperationStatus(DateTime.MinValue, DateTime.MinValue, null,
                OperationDisposition.NothingToDo, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, logPath, null, true);
        }
    }
}
