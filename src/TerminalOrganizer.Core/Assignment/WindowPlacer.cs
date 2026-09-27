using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TerminalOrganizer.Core.Assignment
{
    public delegate bool MutationGuard();
    /// <summary>
    /// The outcome of applying one planned move (REQ-PLC-001): the window, whether it was
    /// skipped (no-move or full-screen), whether the Win32 calls succeeded, and the
    /// per-window failure detail. Immutable.
    /// </summary>
    public sealed class WindowPlacementResult
    {
        private readonly IntPtr handle;
        private readonly string windowId;
        private readonly bool skipped;
        private readonly bool success;
        private readonly string error;

        public WindowPlacementResult(IntPtr handle, string windowId, bool skipped, bool success, string error)
        {
            this.handle = handle;
            this.windowId = windowId;
            this.skipped = skipped;
            this.success = success;
            this.error = error;
        }

        public IntPtr Handle { get { return handle; } }
        public string WindowId { get { return windowId; } }

        /// <summary>True when no Win32 call was attempted (no-move / full-screen skip).</summary>
        public bool Skipped { get { return skipped; } }

        /// <summary>True when the move (and any restore) was issued without a reported failure.</summary>
        public bool Success { get { return success; } }

        /// <summary>The per-window failure detail; null on success or skip.</summary>
        public string Error { get { return error; } }

        public override string ToString()
        {
            return string.Format("{0}|{1}|skipped={2}|success={3}|error={4}",
                windowId, handle, skipped, success, error == null ? "-" : error);
        }
    }

    /// <summary>
    /// Applies an assignment plan to the live desktop (REQ-PLC-001): each restore-first
    /// window via ShowWindow(SW_RESTORE) — Windows itself may briefly activate on restore
    /// (spec D-C) — then SetWindowPos to the target rect with SWP_NOACTIVATE |
    /// SWP_NOZORDER so the move never steals focus or changes Z order. No-move windows are
    /// skipped. A Win32 failure on one window never stops the others and is reported per
    /// window; Apply never throws. Real-screen behaviour is the morning checklist
    /// (tools/organize-dryrun.ps1 live mode prints the plan; this wrapper is what the
    /// tray app will call, SPEC-TRAY-007).
    /// </summary>
    // @MX:WARN: [AUTO] real-screen surface, morning checklist; not an acceptance claim.
    public sealed class WindowPlacer
    {
        private const int SwRestore = 9;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private readonly Func<PlannedMove, bool> moveWindow;
        private readonly Action<IntPtr> restoreWindow;

        public WindowPlacer() : this(NativeMove, delegate(IntPtr handle) { NativeMethods.ShowWindow(handle, SwRestore); }) { }
        public WindowPlacer(Func<PlannedMove, bool> moveWindow, Action<IntPtr> restoreWindow)
        {
            if (moveWindow == null || restoreWindow == null) throw new ArgumentNullException("native mutation port");
            this.moveWindow = moveWindow; this.restoreWindow = restoreWindow;
        }

        /// <summary>
        /// Applies every move in the plan in order; returns one WindowPlacementResult per
        /// window, never throws (REQ-PLC-001). A null plan yields an empty result list.
        /// </summary>
        public WindowPlacementResult[] Apply(AssignmentPlan plan)
        {
            return Apply(plan, delegate { return true; });
        }

        public WindowPlacementResult[] Apply(AssignmentPlan plan, MutationGuard mutationGuard)
        {
            return Apply(plan, mutationGuard, delegate(IntPtr handle) { return true; });
        }

        public WindowPlacementResult[] Apply(AssignmentPlan plan, MutationGuard mutationGuard, Func<IntPtr, bool> identityGuard)
        {
            return Apply(plan, mutationGuard, identityGuard, System.Threading.CancellationToken.None);
        }

        /// <summary>
        /// The B4 cancellation-aware pass: once a SetWindowPos has been issued that
        /// individual operation completes, but a cancelled token prevents the NEXT
        /// move — every remaining move is reported rejected with the "cancelled"
        /// detail (distinct from a topology abort).
        /// </summary>
        public WindowPlacementResult[] Apply(AssignmentPlan plan, MutationGuard mutationGuard,
            Func<IntPtr, bool> identityGuard, System.Threading.CancellationToken cancellationToken)
        {
            bool aborted = false;
            MutationGuard latchedGuard = delegate
            {
                if (aborted) return false;
                try { aborted = mutationGuard == null || !mutationGuard(); }
                catch { aborted = true; }
                return !aborted;
            };
            List<WindowPlacementResult> results = new List<WindowPlacementResult>();
            if (plan == null)
            {
                return results.ToArray();
            }
            bool cancelled = false;
            foreach (PlannedMove move in plan.Moves)
            {
                if (move == null)
                {
                    continue;
                }
                if (!move.MoveRequired)
                {
                    results.Add(new WindowPlacementResult(move.Handle, move.WindowId, true, true, null));
                    continue;
                }
                cancelled = cancelled || cancellationToken.IsCancellationRequested;
                if (cancelled)
                {
                    results.Add(Rejected(move, "cancelled"));
                    continue;
                }
                results.Add(ApplyOne(move, latchedGuard, identityGuard));
            }
            return results.ToArray();
        }

        /// <summary>
        /// One window: restore (OS may activate) then move with no focus steal. A failure is
        /// reported, never thrown; ShowWindow's return value is the previous visibility, not
        /// an error, so only a failed SetWindowPos marks the result failed.
        /// </summary>
        private WindowPlacementResult ApplyOne(PlannedMove move, MutationGuard guard, Func<IntPtr, bool> identityGuard)
        {
            bool success = true;
            string error = null;
            try
            {
                if (move.RestoreFirst)
                {
                    if (guard == null || !guard()) return Rejected(move, "topology-aborted");
                    if (identityGuard == null || !identityGuard(move.Handle)) return Rejected(move, "window identity changed");
                    restoreWindow(move.Handle);
                }
                if (guard == null || !guard()) return Rejected(move, "topology-aborted");
                if (identityGuard == null || !identityGuard(move.Handle)) return Rejected(move, "window identity changed");
                bool moved = moveWindow(move);
                if (!moved)
                {
                    success = false;
                    error = string.Format("SetWindowPos failed for handle {0}: Win32 error {1}", move.Handle, Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
            }
            return new WindowPlacementResult(move.Handle, move.WindowId, false, success, error);
        }

        private static WindowPlacementResult Rejected(PlannedMove move, string error)
        {
            return new WindowPlacementResult(move.Handle, move.WindowId, true, false, error);
        }

        private static bool NativeMove(PlannedMove move)
        {
            return NativeMethods.SetWindowPos(move.Handle, IntPtr.Zero, move.TargetLeft, move.TargetTop,
                move.TargetWidth, move.TargetHeight, SwpNoActivate | SwpNoZOrder);
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; no assembly reference needed).</summary>
        // @MX:WARN: [AUTO] real-screen P/Invoke surface (plan.md H): morning checklist; never exercised by the suite with live windows.
        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr window, int command);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
                int x, int y, int cx, int cy, uint flags);
        }
    }
}
