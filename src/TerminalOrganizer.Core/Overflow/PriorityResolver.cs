using System;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// The derived classification of one window (C1). Manager, FullScreen and StableOccupant
    /// are the immovable classes; the other four carry pinned derived ranks.
    /// </summary>
    public enum DerivedPriorityClass
    {
        Manager,
        FullScreen,
        StableOccupant,
        WindowsNative,
        LocalSession,
        RemoteSession,
        Unidentified
    }

    /// <summary>Which rule produced a resolved rank (C1).</summary>
    public enum PrioritySource
    {
        Manual,
        Declared,
        Derived
    }

    /// <summary>
    /// One window's captured priority inputs (C1): pure data — every field was captured or
    /// persisted before resolution; no clock, process recency or enumeration order enters.
    /// </summary>
    public sealed class PriorityInput
    {
        private readonly string windowId;
        private readonly int? manualRank;
        private readonly int? declaredRank;
        private readonly DerivedPriorityClass derivedClass;
        private readonly bool manager;
        private readonly bool fullScreen;
        private readonly bool stableOccupant;
        private readonly int zOrderIndex;
        private readonly string monitorKey;
        private readonly int top;
        private readonly int left;

        public PriorityInput(string windowId, int? manualRank, int? declaredRank,
            DerivedPriorityClass derivedClass, bool manager, bool fullScreen, bool stableOccupant,
            int zOrderIndex, string monitorKey, int top, int left)
        {
            this.windowId = windowId;
            this.manualRank = manualRank;
            this.declaredRank = declaredRank;
            this.derivedClass = derivedClass;
            this.manager = manager;
            this.fullScreen = fullScreen;
            this.stableOccupant = stableOccupant;
            this.zOrderIndex = zOrderIndex;
            this.monitorKey = monitorKey;
            this.top = top;
            this.left = left;
        }

        public string WindowId { get { return windowId; } }

        /// <summary>The persisted manual rank; valid only within 0..999.</summary>
        public int? ManualRank { get { return manualRank; } }

        /// <summary>The single-digit rank declared in the window-title prefix grammar.</summary>
        public int? DeclaredRank { get { return declaredRank; } }

        public DerivedPriorityClass DerivedClass { get { return derivedClass; } }
        public bool Manager { get { return manager; } }
        public bool FullScreen { get { return fullScreen; } }
        public bool StableOccupant { get { return stableOccupant; } }

        /// <summary>The captured EnumWindows index; 0 is topmost, larger is older/lower.</summary>
        public int ZOrderIndex { get { return zOrderIndex; } }

        public string MonitorKey { get { return monitorKey; } }
        public int Top { get { return top; } }
        public int Left { get { return left; } }
    }

    /// <summary>
    /// One window's resolved priority (C1): immovability (with its reason), the winning
    /// source and rank, and the captured tie-break keys carried through for redistribution.
    /// Immutable.
    /// </summary>
    public sealed class ResolvedPriority
    {
        private readonly string windowId;
        private readonly bool immovable;
        private readonly string immovableReason;
        private readonly PrioritySource source;
        private readonly int rank;
        private readonly int zOrderIndex;
        private readonly string monitorKey;
        private readonly int top;
        private readonly int left;
        private readonly string reason;

        public ResolvedPriority(string windowId, bool immovable, string immovableReason,
            PrioritySource source, int rank, int zOrderIndex, string monitorKey,
            int top, int left, string reason)
        {
            this.windowId = windowId;
            this.immovable = immovable;
            this.immovableReason = immovableReason;
            this.source = source;
            this.rank = rank;
            this.zOrderIndex = zOrderIndex;
            this.monitorKey = monitorKey;
            this.top = top;
            this.left = left;
            this.reason = reason;
        }

        public string WindowId { get { return windowId; } }
        public bool Immovable { get { return immovable; } }

        /// <summary>Why the window is immovable ("manager", "full-screen", "stable occupant"); null when movable.</summary>
        public string ImmovableReason { get { return immovableReason; } }

        public PrioritySource Source { get { return source; } }

        /// <summary>Lower numeric rank means higher priority.</summary>
        public int Rank { get { return rank; } }

        public int ZOrderIndex { get { return zOrderIndex; } }
        public string MonitorKey { get { return monitorKey; } }
        public int Top { get { return top; } }
        public int Left { get { return left; } }

        /// <summary>Short deterministic explanation of the winning rule.</summary>
        public string Reason { get { return reason; } }

        public override string ToString()
        {
            return string.Format("{0}|{1}|rank={2}|z={3}|{4}", windowId, source, rank, zOrderIndex, monitorKey);
        }
    }

    /// <summary>
    /// Resolves deterministic overflow priorities from captured data only (C1): manager,
    /// full-screen and stable occupants are immovable; then a valid manual rank (0..999,
    /// out-of-range values ignored); then the declared title rank; then the pinned derived
    /// table. CompareForRedistribution orders lowest priority FIRST, so an overloaded
    /// monitor sheds its lowest-priority windows first.
    /// </summary>
    public static class PriorityResolver
    {
        /// <summary>The manual rank bounds; persisted values outside are ignored by callers.</summary>
        public const int ManualRankMin = 0;
        public const int ManualRankMax = 999;

        // @MX:ANCHOR: [AUTO] C1 priority entry — consumed by the C2 planner, the redistribution sorter and status diagnostics.
        // @MX:REASON: every overflow decision path resolves through this call; a divergent copy would rank windows differently per consumer.
        public static ResolvedPriority Resolve(PriorityInput input)
        {
            if (input == null)
            {
                return null;
            }
            if (input.Manager || input.DerivedClass == DerivedPriorityClass.Manager)
            {
                return Immovable(input, "manager");
            }
            if (input.FullScreen || input.DerivedClass == DerivedPriorityClass.FullScreen)
            {
                return Immovable(input, "full-screen");
            }
            if (input.StableOccupant || input.DerivedClass == DerivedPriorityClass.StableOccupant)
            {
                return Immovable(input, "stable occupant");
            }
            if (input.ManualRank.HasValue
                && input.ManualRank.Value >= ManualRankMin && input.ManualRank.Value <= ManualRankMax)
            {
                return new ResolvedPriority(input.WindowId, false, null, PrioritySource.Manual,
                    input.ManualRank.Value, input.ZOrderIndex, input.MonitorKey,
                    input.Top, input.Left, "manual");
            }
            if (input.DeclaredRank.HasValue)
            {
                return new ResolvedPriority(input.WindowId, false, null, PrioritySource.Declared,
                    input.DeclaredRank.Value, input.ZOrderIndex, input.MonitorKey,
                    input.Top, input.Left, "declared");
            }
            return new ResolvedPriority(input.WindowId, false, null, PrioritySource.Derived,
                DerivedRank(input.DerivedClass), input.ZOrderIndex, input.MonitorKey,
                input.Top, input.Left, "derived:" + input.DerivedClass);
        }

        /// <summary>
        /// Orders two resolved windows lowest-priority-first for redistribution: movable
        /// before immovable, then rank descending, then captured Z-order index descending
        /// (EnumWindows index 0 is topmost; a larger captured index is older/lower), then
        /// monitor key ordinal, top, left, and finally window ID ordinal. Every input is
        /// captured data — no clock, process recency or dictionary order participates.
        /// </summary>
        // @MX:NOTE: [AUTO] C1 redistribution order — the C2 overflow planner sorts candidate movers with this comparator.
        public static int CompareForRedistribution(ResolvedPriority x, ResolvedPriority y)
        {
            if (x == null && y == null)
            {
                return 0;
            }
            if (x == null)
            {
                return -1;
            }
            if (y == null)
            {
                return 1;
            }
            if (x.Immovable != y.Immovable)
            {
                return x.Immovable ? 1 : -1;
            }
            if (x.Rank != y.Rank)
            {
                return y.Rank.CompareTo(x.Rank);
            }
            if (x.ZOrderIndex != y.ZOrderIndex)
            {
                return y.ZOrderIndex.CompareTo(x.ZOrderIndex);
            }
            int monitor = string.CompareOrdinal(x.MonitorKey, y.MonitorKey);
            if (monitor != 0)
            {
                return monitor;
            }
            if (x.Top != y.Top)
            {
                return x.Top.CompareTo(y.Top);
            }
            if (x.Left != y.Left)
            {
                return x.Left.CompareTo(y.Left);
            }
            return string.CompareOrdinal(x.WindowId, y.WindowId);
        }

        private static ResolvedPriority Immovable(PriorityInput input, string reason)
        {
            return new ResolvedPriority(input.WindowId, true, reason, PrioritySource.Derived,
                DerivedRank(input.DerivedClass), input.ZOrderIndex, input.MonitorKey,
                input.Top, input.Left, reason);
        }

        /// <summary>The pinned derived table: WindowsNative 100, LocalSession 300, RemoteSession 400, Unidentified 500.</summary>
        private static int DerivedRank(DerivedPriorityClass derivedClass)
        {
            switch (derivedClass)
            {
                case DerivedPriorityClass.WindowsNative:
                    return 100;
                case DerivedPriorityClass.LocalSession:
                    return 300;
                case DerivedPriorityClass.RemoteSession:
                    return 400;
                case DerivedPriorityClass.Unidentified:
                    return 500;
                default:
                    return 0;
            }
        }
    }
}
