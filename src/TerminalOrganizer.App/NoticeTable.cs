using System;
using System.Globalization;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The user-facing notice kinds (REQ-NOTI-001; B3 levels and balloon policy): one per
    /// table row — the layout stops, the merge outcomes, the acquisition/placement
    /// degradations (B3 additions NoWindows / PlacementFailed / AcquisitionDegraded /
    /// UnexpectedFailure), the hotkey conflict, and the manager skip.
    /// </summary>
    public enum NoticeKind
    {
        TopologyChanged,
        MergeSafetyAborted,
        /// <summary>The applied layout type is not supported; nothing was changed.</summary>
        UnsupportedLayout,

        /// <summary>The layout data is invalid; the detail names why.</summary>
        InvalidLayout,

        /// <summary>No FancyZones layout is applied to this monitor.</summary>
        NoAppliedLayout,

        /// <summary>A merge aborted without confirming its tab; the window was left open.</summary>
        MergeNotConfirmed,

        /// <summary>The tab was created but the old window could not be closed.</summary>
        MergeConfirmedNotClosed,

        /// <summary>The attach helper for a planned Remote merge is absent; the merge was skipped.</summary>
        HelperMissing,

        /// <summary>The global hotkey registration failed; the menu path still works.</summary>
        HotkeyConflict,

        /// <summary>The persisted manager choice could not be honored; organized without a manager zone.</summary>
        ManagerSkipped,

        /// <summary>No Windows Terminal windows were discovered on the target monitor (B3).</summary>
        NoWindows,

        /// <summary>One or more windows could not be moved; the log carries the per-window detail (B3).</summary>
        PlacementFailed,

        /// <summary>Windows were skipped because their desktop or state could not be verified (B3).</summary>
        AcquisitionDegraded,

        /// <summary>An unexpected exception stopped the organize run (B3).</summary>
        UnexpectedFailure,

        /// <summary>The saved overflow policy is Merge while merge is not enabled; the run degraded to stacking (C3).</summary>
        MergePolicyDisabled
    }

    /// <summary>The severity of one notice (B3): drives the balloon icon and the balloon policy.</summary>
    public enum NoticeLevel
    {
        Information,
        Warning,
        Error
    }

    /// <summary>
    /// One user-facing notice resolved from the table (B3): the kind, the severity level,
    /// whether a balloon is warranted, and the final text. The log ALWAYS receives the
    /// text; the balloon receives the identical text only when ShowBalloon is set.
    /// Immutable.
    /// </summary>
    public sealed class NoticeMessage
    {
        private readonly NoticeKind kind;
        private readonly NoticeLevel level;
        private readonly bool showBalloon;
        private readonly string text;

        public NoticeMessage(NoticeKind kind, NoticeLevel level, bool showBalloon, string text)
        {
            this.kind = kind;
            this.level = level;
            this.showBalloon = showBalloon;
            this.text = text;
        }

        public NoticeKind Kind { get { return kind; } }
        public NoticeLevel Level { get { return level; } }
        public bool ShowBalloon { get { return showBalloon; } }
        public string Text { get { return text; } }
    }

    /// <summary>The message-typed notice channel (B3): carries the level and the balloon policy alongside the text.</summary>
    public delegate void NoticeMessageSink(NoticeMessage message);

    /// <summary>
    /// The pure notice table (REQ-NOTI-001; B3 level/balloon matrix). The copy, the level
    /// and the balloon policy per kind are pinned in the B3 design table — the log and the
    /// balloon receive the SAME resolved text, so their texts can never drift apart.
    /// Routine successful outcomes are menu/log-only (no balloon); Information kinds never
    /// balloon. Pure: no WinForms types, no ambient state, never throws.
    /// </summary>
    // @MX:NOTE: [AUTO] user-facing strings + levels/balloon flags pinned in the B3 design notice table - edit there first, then here.
    // @MX:ANCHOR: [AUTO] the single notice resolution surface every emission path consults.
    // @MX:REASON: controller, lifecycle and shell all resolve notices through Get/Text; the copy and balloon policy are pinned product behavior.
    public static class NoticeTable
    {
        /// <summary>
        /// The text for one failure kind; arg is the kind's primary placeholder subject
        /// (session name, count, layout type) and may be null for the no-placeholder kinds.
        /// Compatibility wrapper over Get(kind, arg).Text.
        /// </summary>
        public static string Text(NoticeKind kind, string arg)
        {
            return Get(kind, arg).Text;
        }

        /// <summary>Compatibility wrapper over Get(kind, arg, monitorLabel).Text.</summary>
        public static string Text(NoticeKind kind, string arg, string monitorLabel)
        {
            return Get(kind, arg, monitorLabel).Text;
        }

        /// <summary>Resolves one kind with no monitor label (single-placeholder kinds).</summary>
        public static NoticeMessage Get(NoticeKind kind, string arg)
        {
            return Get(kind, arg, null);
        }

        /// <summary>
        /// Resolves one kind. arg is the primary placeholder subject; monitorLabel fills
        /// the {monitor} placeholder (ManagerSkipped pairs it with the {name} arg) and is
        /// ignored by the single-placeholder kinds.
        /// </summary>
        public static NoticeMessage Get(NoticeKind kind, string arg, string monitorLabel)
        {
            switch (kind)
            {
                case NoticeKind.NoWindows:
                    return new NoticeMessage(kind, NoticeLevel.Information, false,
                        string.Format(CultureInfo.InvariantCulture,
                            "No Windows Terminal windows on {0} — nothing to organize.", arg ?? string.Empty));

                case NoticeKind.ManagerSkipped:
                    return new NoticeMessage(kind, NoticeLevel.Information, false,
                        string.Format(CultureInfo.InvariantCulture,
                            "Manager “{0}” is not open on {1}. Other windows were organized.",
                            arg ?? string.Empty, monitorLabel ?? string.Empty));

                case NoticeKind.MergeNotConfirmed:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "Merge for “{0}” was not confirmed. The original window remains open.", arg ?? string.Empty));

                case NoticeKind.MergeSafetyAborted:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "Merge for “{0}” could not be verified safely. The original window remains open.", arg ?? string.Empty));

                case NoticeKind.MergeConfirmedNotClosed:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "The tab for '{0}' was created but its old window could not be closed; close it by hand.", arg ?? string.Empty));

                case NoticeKind.HelperMissing:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "Attach helper not found for '{0}'; the merge was skipped.", arg ?? string.Empty));

                case NoticeKind.PlacementFailed:
                    return new NoticeMessage(kind, NoticeLevel.Error, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0} windows could not be moved. Open the log for details.", arg ?? string.Empty));

                case NoticeKind.AcquisitionDegraded:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0} windows were skipped because their desktop or state could not be verified.", arg ?? string.Empty));

                case NoticeKind.TopologyChanged:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        "Monitor layout changed during organize. No remaining moves were applied.");

                case NoticeKind.HotkeyConflict:
                    return new NoticeMessage(kind, NoticeLevel.Error, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "Hotkey '{0}' is already taken; use the tray menu.", arg ?? string.Empty));

                case NoticeKind.UnsupportedLayout:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "FancyZones layout '{0}' is not supported on this monitor; nothing was changed.", arg ?? string.Empty));

                case NoticeKind.InvalidLayout:
                    return new NoticeMessage(kind, NoticeLevel.Error, true,
                        string.Format(CultureInfo.InvariantCulture,
                            "The FancyZones layout data on this monitor is invalid ({0}); nothing was changed.", arg ?? string.Empty));

                case NoticeKind.NoAppliedLayout:
                    return new NoticeMessage(kind, NoticeLevel.Information, false,
                        "No FancyZones layout is applied to this monitor; nothing was changed.");

                case NoticeKind.UnexpectedFailure:
                    return new NoticeMessage(kind, NoticeLevel.Error, true,
                        "Organize failed. Open the log for details.");

                case NoticeKind.MergePolicyDisabled:
                    return new NoticeMessage(kind, NoticeLevel.Warning, true,
                        "Overflow policy is Merge, but merge is not enabled; windows were stacked instead.");

                default:
                    return new NoticeMessage(kind, NoticeLevel.Information, false, kind.ToString());
            }
        }
    }
}
