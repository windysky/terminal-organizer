using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Monitors;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// The outcome of WindowScope.Filter (B5): the windows included by the shared
    /// production scope rule plus one counter per exclusion reason. Immutable; the
    /// included array returns a copy.
    /// </summary>
    public sealed class WindowScopeResult
    {
        private readonly EnumeratedWindow[] included;
        private readonly int skippedOtherMonitor;
        private readonly int skippedOtherDesktop;
        private readonly int skippedUnknownDesktop;
        private readonly int skippedUnattributed;

        public WindowScopeResult(EnumeratedWindow[] included, int skippedOtherMonitor,
            int skippedOtherDesktop, int skippedUnknownDesktop, int skippedUnattributed)
        {
            this.included = included == null ? new EnumeratedWindow[0] : (EnumeratedWindow[])included.Clone();
            this.skippedOtherMonitor = skippedOtherMonitor;
            this.skippedOtherDesktop = skippedOtherDesktop;
            this.skippedUnknownDesktop = skippedUnknownDesktop;
            this.skippedUnattributed = skippedUnattributed;
        }

        public EnumeratedWindow[] Included
        {
            get { return (EnumeratedWindow[])included.Clone(); }
        }

        public int SkippedOtherMonitor { get { return skippedOtherMonitor; } }
        public int SkippedOtherDesktop { get { return skippedOtherDesktop; } }
        public int SkippedUnknownDesktop { get { return skippedUnknownDesktop; } }

        /// <summary>Windows with no monitor attribution in the given set (excluded from Included).</summary>
        public int SkippedUnattributed { get { return skippedUnattributed; } }
    }

    /// <summary>
    /// The single production scope rule (A3/A4 parity; B5): stable monitor-key match,
    /// current desktop only, unknown desktop excluded, unattributed excluded. The
    /// allMonitors flag removes ONLY the monitor filter — the desktop safety filter
    /// always applies. A null monitorKey also removes the monitor filter (the
    /// all-monitors menu path). Production discovery (Composition.Discover) and the
    /// dump-windows / organize-dryrun / organize-once tools all call this function;
    /// the rule is never re-implemented in PowerShell.
    /// </summary>
    // @MX:ANCHOR: [AUTO] the one shared window-scope rule consumed by production discovery and three tools.
    // @MX:REASON: fan_in >= 4 (TrayContext.Discover + dump-windows + organize-dryrun + organize-once); duplicating it in PowerShell caused the B5 tool-scope drift.
    public static class WindowScope
    {
        public static WindowScopeResult Filter(EnumeratedWindow[] windows, MonitorKey monitorKey, bool allMonitors)
        {
            List<EnumeratedWindow> included = new List<EnumeratedWindow>();
            int otherMonitor = 0;
            int otherDesktop = 0;
            int unknownDesktop = 0;
            int unattributed = 0;
            bool byMonitor = !allMonitors && monitorKey != null;
            foreach (EnumeratedWindow window in windows ?? new EnumeratedWindow[0])
            {
                if (window == null)
                {
                    continue;
                }
                if (window.Monitor == null)
                {
                    unattributed++;
                    continue;
                }
                if (byMonitor && !window.Monitor.StableKey.EqualsKey(monitorKey))
                {
                    otherMonitor++;
                    continue;
                }
                if (window.DesktopStatus == null || !window.DesktopStatus.Success)
                {
                    unknownDesktop++;
                    continue;
                }
                if (!window.DesktopStatus.Value)
                {
                    otherDesktop++;
                    continue;
                }
                included.Add(window);
            }
            return new WindowScopeResult(included.ToArray(), otherMonitor, otherDesktop, unknownDesktop, unattributed);
        }
    }
}
