using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Assignment;
using TerminalOrganizer.Core.Monitors;
using TerminalOrganizer.Core.Overflow;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.App
{
    public delegate SessionRecord[] SessionRecordsRead(int[] processIds);

    /// <summary>Shared, injectable discovery boundary; rejected desktops never reach deeper acquisition.</summary>
    public static class DiscoveryPolicy
    {
        public static DiscoveredWindows Acquire(EnumeratedWindow[] windows, MonitorInfo monitor,
            Func<IntPtr, WindowState> readState, TabTitleRead readTabs, SessionRecordsRead readSessions,
            Func<WindowSnapshot[], WindowSnapshot[]> overlay)
        {
            List<AcquiredWindow> acquired = new List<AcquiredWindow>();
            Dictionary<long, WindowState> states = new Dictionary<long, WindowState>();
            HashSet<int> pids = new HashSet<int>();
            int enumerated = 0, other = 0, unknown = 0, invalid = 0;
            foreach (EnumeratedWindow window in windows ?? new EnumeratedWindow[0])
            {
                if (window == null || window.Monitor == null
                    || (monitor != null && !window.Monitor.StableKey.EqualsKey(monitor.StableKey))) continue;
                enumerated++;
                if (window.DesktopStatus == null || !window.DesktopStatus.Success) { unknown++; continue; }
                if (!window.DesktopStatus.Value) { other++; continue; }
                WindowState state;
                try { state = readState(window.Handle); }
                catch (Exception ex) { state = WindowState.Invalid(WindowStateReadStatus.Failed, ex.Message); }
                if (state == null || !state.IsValidForMutation)
                {
                    invalid++;
                    acquired.Add(new AcquiredWindow(window.Identity, TabTitleResult.Failure(
                        window.Identity == null ? null : window.Identity.RawWindowTitle, "state untrusted"), window.Monitor, window.DesktopStatus));
                    continue;
                }
                TabTitleResult tabs;
                try { tabs = readTabs(window.Handle); }
                catch (Exception ex) { tabs = TabTitleResult.Failure(window.Identity == null ? null : window.Identity.RawWindowTitle, ex.Message); }
                acquired.Add(new AcquiredWindow(window.Identity, tabs, window.Monitor, window.DesktopStatus));
                states[window.Handle.ToInt64()] = state;
                if (window.Identity != null && window.Identity.ProcessId > 0) pids.Add(window.Identity.ProcessId);
            }
            int[] processIds = new int[pids.Count];
            pids.CopyTo(processIds);
            Array.Sort(processIds);
            SessionRecord[] sessions = processIds.Length == 0 ? new SessionRecord[0] : readSessions(processIds);
            WindowSnapshot[] snapshots = WindowSnapshotBuilder.Compose(acquired.ToArray(), sessions);
            if (overlay != null) snapshots = overlay(snapshots);
            List<WindowFact> facts = new List<WindowFact>();
            foreach (WindowSnapshot snapshot in snapshots)
            {
                WindowState state;
                if (!states.TryGetValue(snapshot.Handle.ToInt64(), out state)) continue;
                string name = snapshot.Identity == null ? null : snapshot.Identity.RawWindowTitle;
                if (string.IsNullOrEmpty(name) && snapshot.Tabs.Length > 0) name = snapshot.Tabs[0].Title;
                facts.Add(new WindowFact("w" + snapshot.Handle, snapshot.Handle, name, snapshot.Identified,
                    state.Left, state.Top, state.Width, state.Height, state.Maximized, state.Minimized, state.FullScreen, state.IsValidForMutation));
            }
            return new DiscoveredWindows(facts.ToArray(), snapshots, enumerated, other, unknown, invalid);
        }
    }
}
