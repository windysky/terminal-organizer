using System;
using TerminalOrganizer.Core.Rules;

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
        private readonly string ruleReference;
        private readonly int? defaultRank;

        /// <summary>
        /// Today's constructor: the declared rank is the launcher-grammar digit (so its provenance is the
        /// preset), the default rank is 500, and no rule reference accompanies a derived rank.
        /// </summary>
        public PriorityInput(string windowId, int? manualRank, int? declaredRank,
            DerivedPriorityClass derivedClass, bool manager, bool fullScreen, bool stableOccupant,
            int zOrderIndex, string monitorKey, int top, int left)
            : this(windowId, manualRank, declaredRank, declaredRank.HasValue ? LauncherPrefixPreset.Reference : null, null,
                derivedClass, manager, fullScreen, stableOccupant, zOrderIndex, monitorKey, top, left)
        {
        }

        /// <summary>
        /// The rule-aware constructor (SPEC-RULES-008): the rule rank takes the declared slot; the rule
        /// reference names the rank-supplying rule, or the matched rule when none supplied a rank (null
        /// when no rule matched); the default rank applies to an unidentified window (0..999, else 500).
        /// </summary>
        public PriorityInput(string windowId, int? manualRank, int? ruleRank, string ruleReference, int? defaultRank,
            DerivedPriorityClass derivedClass, bool manager, bool fullScreen, bool stableOccupant,
            int zOrderIndex, string monitorKey, int top, int left)
        {
            this.ruleReference = ruleReference;
            this.defaultRank = defaultRank;
            this.windowId = windowId;
            this.manualRank = manualRank;
            this.declaredRank = ruleRank;
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

        /// <summary>The rank a title rule or the launcher preset supplied (0..999 from a rule, a single digit from the grammar).</summary>
        public int? DeclaredRank { get { return declaredRank; } }

        /// <summary>The reference of the rank-supplying rule, or of the matched rule when none supplied a rank; null when no rule matched.</summary>
        public string RuleReference { get { return ruleReference; } }

        /// <summary>The default rank for an unidentified window; null means 500 (a value outside 0..999 also means 500).</summary>
        public int? DefaultRank { get { return defaultRank; } }

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
        private readonly string provenance;

        public ResolvedPriority(string windowId, bool immovable, string immovableReason,
            PrioritySource source, int rank, int zOrderIndex, string monitorKey,
            int top, int left, string reason)
            : this(windowId, immovable, immovableReason, source, rank, zOrderIndex, monitorKey, top, left, reason, reason)
        {
        }

        public ResolvedPriority(string windowId, bool immovable, string immovableReason,
            PrioritySource source, int rank, int zOrderIndex, string monitorKey,
            int top, int left, string reason, string provenance)
        {
            this.provenance = provenance;
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

        /// <summary>
        /// Where the resolved rank came from: "manual", "preset launcher-prefix", "rule n: text"
        /// ("rule n: marker m"), "session Local|Remote|WindowsNative" or "default", followed by
        /// " (matched ref)" when a rule matched without supplying the rank. Never truncated.
        /// </summary>
        public string Provenance { get { return provenance; } }

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
                    input.Top, input.Left, "manual", "manual");
            }
            if (input.DeclaredRank.HasValue)
            {
                return new ResolvedPriority(input.WindowId, false, null, PrioritySource.Declared,
                    input.DeclaredRank.Value, input.ZOrderIndex, input.MonitorKey,
                    input.Top, input.Left, "declared", HasReference(input) ? input.RuleReference : "declared");
            }
            // @MX:NOTE: source mapping (SPEC-RULES-008 A2) — a rule or preset rank is Declared; a session-class
            // or default rank is Derived, so move reasons keep reading manual / declared / derived.
            bool unidentified = input.DerivedClass == DerivedPriorityClass.Unidentified;
            int rank = unidentified ? ResolveDefaultRank(input.DefaultRank) : DerivedRank(input.DerivedClass);
            string basis = unidentified ? "default" : "session " + SessionName(input.DerivedClass);
            string provenance = HasReference(input) ? basis + " (matched " + input.RuleReference + ")" : basis;
            return new ResolvedPriority(input.WindowId, false, null, PrioritySource.Derived,
                rank, input.ZOrderIndex, input.MonitorKey,
                input.Top, input.Left, "derived:" + input.DerivedClass, provenance);
        }

        /// <summary>A null or empty rule reference means no rule matched.</summary>
        private static bool HasReference(PriorityInput input)
        {
            return !string.IsNullOrEmpty(input.RuleReference);
        }

        /// <summary>The default rank: the given value within 0..999, else 500.</summary>
        private static int ResolveDefaultRank(int? defaultRank)
        {
            if (defaultRank.HasValue && defaultRank.Value >= ManualRankMin && defaultRank.Value <= ManualRankMax)
            {
                return defaultRank.Value;
            }
            return 500;
        }

        private static string SessionName(DerivedPriorityClass derivedClass)
        {
            switch (derivedClass)
            {
                case DerivedPriorityClass.WindowsNative:
                    return "WindowsNative";
                case DerivedPriorityClass.LocalSession:
                    return "Local";
                case DerivedPriorityClass.RemoteSession:
                    return "Remote";
                default:
                    return derivedClass.ToString();
            }
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
