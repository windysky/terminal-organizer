using System;
using System.Collections.Generic;
using System.Globalization;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Overflow;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The one formatter of a window's priority row (SPEC-RULES-009 REQ-UI-001, REQ-UI-002):
    /// "&lt;display&gt; · rank &lt;N&gt; · &lt;provenance&gt;", with " (as &lt;identity&gt;)" after the display when the
    /// identity name differs from the first tab title. The tray menu rows and both organize tools print this
    /// text, so they can never drift apart. The rank and provenance come from the same resolution
    /// redistribution uses (the movable path); the stored provenance is never cut, only the rendered row is.
    /// </summary>
    // @MX:ANCHOR: menu rows and both tools must print identical text.
    // @MX:REASON: a second formatter would let the menu and the tool lines disagree about where a rank came from.
    public static class RuleProvenance
    {
        /// <summary>A provenance longer than this is cut when rendered (to <see cref="CutLength"/> characters plus an ellipsis).</summary>
        public const int RenderLimit = 80;

        /// <summary>How many provenance characters survive a cut.</summary>
        public const int CutLength = 79;

        private const string Separator = " · ";

        /// <summary>The prefix of a tool line: two spaces and "window: ".</summary>
        public const string LinePrefix = "  window: ";

        /// <summary>The row text of every window, in input order, resolved on the movable path with the given overrides and default rank.</summary>
        public static string[] RowTexts(WindowSnapshot[] snapshots, PriorityOverride[] manualOverrides, int defaultRank)
        {
            List<string> rows = new List<string>();
            if (snapshots != null)
            {
                foreach (WindowSnapshot snapshot in snapshots)
                {
                    rows.Add(RowText(snapshot, manualOverrides, defaultRank));
                }
            }
            return rows.ToArray();
        }

        /// <summary>The tool lines (the row text behind the line prefix) of every window, in input order.</summary>
        public static string[] LineTexts(WindowSnapshot[] snapshots, PriorityOverride[] manualOverrides, int defaultRank)
        {
            string[] rows = RowTexts(snapshots, manualOverrides, defaultRank);
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = LinePrefix + rows[i];
            }
            return rows;
        }

        /// <summary>One window's row text; empty for a null snapshot. Never throws.</summary>
        public static string RowText(WindowSnapshot snapshot, PriorityOverride[] manualOverrides, int defaultRank)
        {
            if (snapshot == null)
            {
                return string.Empty;
            }
            try
            {
                ResolvedPriority priority = CrossMonitorSnapshotComposer.ResolveMovable(snapshot, manualOverrides, defaultRank);
                string display = TrayMenuBuilder.DisplayWindowCandidate(snapshot);
                TabSnapshot[] tabs = snapshot.Tabs;
                string firstTitle = tabs.Length > 0 && tabs[0] != null ? tabs[0].Title : null;
                string identity = snapshot.IdentityName;
                if (!string.IsNullOrEmpty(identity) && !string.Equals(identity, firstTitle, StringComparison.Ordinal))
                {
                    display = display + " (as " + identity + ")";
                }
                return display + Separator + "rank " + priority.Rank.ToString(CultureInfo.InvariantCulture)
                    + Separator + Bound(Provenance(snapshot, priority));
            }
            catch (Exception)
            {
                // NFR-3: a formatting failure never reaches the menu or a tool.
                return TrayMenuBuilder.DisplayWindowCandidate(snapshot);
            }
        }

        /// <summary>
        /// True when every tab of the window is bound to an open launcher session that carries a command line.
        /// A run-scoped label's synthetic session (no command line) does not count.
        /// </summary>
        public static bool HasCanonicalSessionEvidence(WindowSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            TabSnapshot[] tabs = snapshot.Tabs;
            if (tabs.Length == 0)
            {
                return false;
            }
            foreach (TabSnapshot tab in tabs)
            {
                if (tab == null || tab.Session == null || string.IsNullOrEmpty(tab.Session.CommandLine))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The stored provenance, with a labeled window's session-class step rendered as "label".</summary>
        private static string Provenance(WindowSnapshot snapshot, ResolvedPriority priority)
        {
            string provenance = priority.Provenance ?? string.Empty;
            if (priority.Source == PrioritySource.Derived && ManagerResolver.DeriveUserLabel(snapshot) != null
                && provenance.StartsWith("session Local", StringComparison.Ordinal))
            {
                return "label" + provenance.Substring("session Local".Length);
            }
            return provenance;
        }

        /// <summary>REQ-UI-001 render bound: above 80 characters, the first 79 plus an ellipsis.</summary>
        private static string Bound(string provenance)
        {
            if (provenance.Length <= RenderLimit)
            {
                return provenance;
            }
            return provenance.Substring(0, CutLength) + "…";
        }
    }

    /// <summary>
    /// The process-lifetime de-duplicating diagnostic sink (SPEC-RULES-009 REQ-LOG-001). A rule or settings
    /// diagnostic is written once per distinct key (position + reason + definition text) however often the
    /// settings are re-read; a changed definition is a new key and is written again. Thread-safe; the
    /// write goes through the supplied sink (production: LogSink.Append) and never throws.
    /// </summary>
    public static class RuleDiagnosticLog
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Writes the line through the sink unless this key was already written in this process.</summary>
        public static void Report(Action<string> sink, string line, string key)
        {
            if (sink == null || string.IsNullOrEmpty(line))
            {
                return;
            }
            string identity = key ?? line;
            lock (Gate)
            {
                if (!Seen.Add(identity))
                {
                    return;
                }
            }
            try
            {
                sink(line);
            }
            catch (Exception)
            {
                // Logging must never break a settings read.
            }
        }
    }
}
