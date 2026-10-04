using System;

namespace TerminalOrganizer.Core.Assignment
{
    /// <summary>
    /// One window's assignment inputs (REQ-ASG-001..005): its id, handle and window name,
    /// the identification flag (an unidentified window never acts as manager, spec A2), the
    /// current rect as left/top/width/height (plan.md B.5: plain facts, never a
    /// WindowSnapshot), and the state flags acquired by WindowStateReader (REQ-PLC-002).
    /// Immutable.
    /// </summary>
    public sealed class WindowFact
    {
        private readonly string id;
        private readonly IntPtr handle;
        private readonly string name;
        private readonly bool identified;
        private readonly int left;
        private readonly int top;
        private readonly int width;
        private readonly int height;
        private readonly bool maximized;
        private readonly bool minimized;
        private readonly bool fullScreen;

        public WindowFact(string id, IntPtr handle, string name, bool identified,
            int left, int top, int width, int height,
            bool maximized, bool minimized, bool fullScreen)
            : this(id, handle, name, identified, left, top, width, height, maximized, minimized, fullScreen, true)
        {
        }

        public WindowFact(string id, IntPtr handle, string name, bool identified,
            int left, int top, int width, int height,
            bool maximized, bool minimized, bool fullScreen, bool mutationStateTrusted)
        {
            MutationStateTrusted = mutationStateTrusted;
            this.id = id;
            this.handle = handle;
            this.name = name;
            this.identified = identified;
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
            this.maximized = maximized;
            this.minimized = minimized;
            this.fullScreen = fullScreen;
        }

        public string Id { get { return id; } }
        public bool MutationStateTrusted { get; private set; }
        public bool MayMove { get { return MutationStateTrusted && !FullScreen; } }
        public IntPtr Handle { get { return handle; } }
        public string Name { get { return name; } }
        /// <summary>
        /// True when the window has an identity name (SPEC-RULES-009): a rule-derived name or a launcher
        /// session name, not only session evidence. Merge eligibility is a separate flag on the snapshot.
        /// </summary>
        public bool Identified { get { return identified; } }
        public int Left { get { return left; } }
        public int Top { get { return top; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public bool Maximized { get { return maximized; } }
        public bool Minimized { get { return minimized; } }
        public bool FullScreen { get { return fullScreen; } }

        /// <summary>REQ-ASG-005: only a normal state allows a no-move plan; state outranks rect equality.</summary>
        public bool StateIsNormal
        {
            get { return MutationStateTrusted && !fullScreen && !maximized && !minimized; }
        }

        public override string ToString()
        {
            return string.Format("{0}|{1}|{2},{3} {4}x{5}|normal={6}", id, name, left, top, width, height, StateIsNormal);
        }
    }

    /// <summary>
    /// One window's planned placement (REQ-ASG-001..005): the target zone id and rect, and
    /// the move flags — MoveRequired false marks the idempotent no-move (D-F: exact-rect
    /// equality in the normal state); RestoreFirst marks a maximized or minimized window
    /// that must be restored before its move (D-C); Stacked marks an overflow window (D-E);
    /// FullScreenSkipped marks a full-screen window left untouched. Immutable.
    /// </summary>
    public enum PlannedMoveSkipReason { None, FullScreen, UntrustedState }

    public sealed class PlannedMove
    {
        public const int NoZoneId = -1;
        public bool OccupiesZone { get { return SkipReason == PlannedMoveSkipReason.None; } }
        public PlannedMoveSkipReason SkipReason { get; private set; }
        private readonly string windowId;
        private readonly IntPtr handle;
        private readonly int zoneId;
        private readonly int targetLeft;
        private readonly int targetTop;
        private readonly int targetWidth;
        private readonly int targetHeight;
        private readonly bool moveRequired;
        private readonly bool restoreFirst;
        private readonly bool stacked;
        private readonly bool fullScreenSkipped;

        public PlannedMove(string windowId, IntPtr handle, int zoneId,
            int targetLeft, int targetTop, int targetWidth, int targetHeight,
            bool moveRequired, bool restoreFirst, bool stacked, bool fullScreenSkipped)
            : this(windowId, handle, zoneId, targetLeft, targetTop, targetWidth, targetHeight,
                moveRequired, restoreFirst, stacked, fullScreenSkipped ? PlannedMoveSkipReason.FullScreen : PlannedMoveSkipReason.None)
        {
        }

        public PlannedMove(string windowId, IntPtr handle, int zoneId,
            int targetLeft, int targetTop, int targetWidth, int targetHeight,
            bool moveRequired, bool restoreFirst, bool stacked, PlannedMoveSkipReason skipReason)
        {
            SkipReason = skipReason;
            this.windowId = windowId;
            this.handle = handle;
            this.zoneId = skipReason == PlannedMoveSkipReason.None ? zoneId : NoZoneId;
            this.targetLeft = targetLeft;
            this.targetTop = targetTop;
            this.targetWidth = targetWidth;
            this.targetHeight = targetHeight;
            this.moveRequired = skipReason == PlannedMoveSkipReason.None && moveRequired;
            this.restoreFirst = skipReason == PlannedMoveSkipReason.None && restoreFirst;
            this.stacked = skipReason == PlannedMoveSkipReason.None && stacked;
            this.fullScreenSkipped = skipReason == PlannedMoveSkipReason.FullScreen;
        }

        public string WindowId { get { return windowId; } }
        public IntPtr Handle { get { return handle; } }
        public int ZoneId { get { return zoneId; } }
        public int TargetLeft { get { return targetLeft; } }
        public int TargetTop { get { return targetTop; } }
        public int TargetWidth { get { return targetWidth; } }
        public int TargetHeight { get { return targetHeight; } }

        /// <summary>False for an idempotent no-move or a skipped full-screen window.</summary>
        public bool MoveRequired { get { return moveRequired; } }

        /// <summary>REQ-PLC-001 input: the placer restores via ShowWindow(SW_RESTORE) before moving.</summary>
        public bool RestoreFirst { get { return restoreFirst; } }

        /// <summary>REQ-ASG-003: an overflow window assigned to the last zone of fill order.</summary>
        public bool Stacked { get { return stacked; } }

        /// <summary>REQ-ASG-005: a full-screen exclusive window planned as no-move, untouched.</summary>
        public bool FullScreenSkipped { get { return fullScreenSkipped; } }

        public override string ToString()
        {
            return string.Format("{0}->z{1} @{2},{3} {4}x{5} move={6} restore={7} stacked={8} fullscreen-skipped={9}",
                windowId, zoneId, targetLeft, targetTop, targetWidth, targetHeight,
                moveRequired, restoreFirst, stacked, fullScreenSkipped);
        }
    }

    /// <summary>
    /// The whole assignment plan (REQ-ASG-001..005): one PlannedMove per input window in
    /// fill order (the manager first, then the non-manager windows by position), plus the
    /// manager outcome — pinned with its window id, or skipped with the reason string
    /// (REQ-ASG-004). Immutable; Moves returns a copy.
    /// </summary>
    public sealed class AssignmentPlan
    {
        private readonly PlannedMove[] moves;
        private readonly bool managerPinned;
        private readonly string managerWindowId;
        private readonly string managerSkippedReason;

        public AssignmentPlan(PlannedMove[] moves, bool managerPinned, string managerWindowId, string managerSkippedReason)
        {
            this.moves = moves == null ? new PlannedMove[0] : (PlannedMove[])moves.Clone();
            this.managerPinned = managerPinned;
            this.managerWindowId = managerWindowId;
            this.managerSkippedReason = managerSkippedReason;
        }

        public PlannedMove[] Moves
        {
            get { return (PlannedMove[])moves.Clone(); }
        }

        /// <summary>True when the manager window was found, identified and pinned to the first zone.</summary>
        public bool ManagerPinned { get { return managerPinned; } }

        /// <summary>The pinned manager window's id; null when not pinned.</summary>
        public string ManagerWindowId { get { return managerWindowId; } }

        /// <summary>The REQ-ASG-004 skip reason; null when pinned.</summary>
        public string ManagerSkippedReason { get { return managerSkippedReason; } }

        /// <summary>
        /// One window's planned move by id (C2): the cross-monitor composer reads the
        /// A2 stable/overflow classification (no-move vs stacked) from the local plan.
        /// </summary>
        public bool TryFindMove(string windowId, out PlannedMove move)
        {
            move = null;
            if (string.IsNullOrEmpty(windowId))
            {
                return false;
            }
            foreach (PlannedMove candidate in moves)
            {
                if (candidate != null && string.Equals(candidate.WindowId, windowId, StringComparison.Ordinal))
                {
                    move = candidate;
                    return true;
                }
            }
            return false;
        }

        public override string ToString()
        {
            return string.Format("moves={0} manager-pinned={1} manager-skipped={2}",
                moves.Length, managerPinned, managerSkippedReason == null ? "-" : managerSkippedReason);
        }
    }
}
