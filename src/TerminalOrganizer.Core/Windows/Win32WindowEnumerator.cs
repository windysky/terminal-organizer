using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TerminalOrganizer.Core.Monitors;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// One enumerated WT window (REQ-ACQ-001): the top-level handle, its monitor
    /// attribution (null when no monitor in the given set contains the window), and the
    /// current-desktop status result from the desktop reader.
    /// </summary>
    public sealed class EnumeratedWindow
    {
        private readonly IntPtr handle;
        private readonly MonitorInfo monitor;
        private readonly DesktopFlagResult desktopStatus;

        public EnumeratedWindow(IntPtr handle, MonitorInfo monitor, DesktopFlagResult desktopStatus)
            : this(handle, monitor, desktopStatus, new WindowIdentity(handle, 0, 0, null, null))
        {
        }

        public EnumeratedWindow(IntPtr handle, MonitorInfo monitor, DesktopFlagResult desktopStatus, WindowIdentity identity)
        {
            this.Identity = identity;
            this.handle = handle;
            this.monitor = monitor;
            this.desktopStatus = desktopStatus;
        }

        public IntPtr Handle { get { return handle; } }
        public WindowIdentity Identity { get; private set; }

        /// <summary>The attributed MonitorInfo from the given monitor set; null when unattributed.</summary>
        public MonitorInfo Monitor { get { return monitor; } }

        /// <summary>The current-desktop status (MON-003 IDesktopSource); a failure result marks a degraded read.</summary>
        public DesktopFlagResult DesktopStatus { get { return desktopStatus; } }
    }

    /// <summary>
    /// Enumerates the visible top-level Windows Terminal windows (REQ-ACQ-001) in ZOrder
    /// (topmost first, the EnumWindows order — the final placement order is decided later
    /// by SPEC-ZONE-005). A window counts only when its class is
    /// CASCADIA_HOSTING_WINDOW_CLASS, it is visible, and its pid belongs to the given
    /// WindowsTerminal.exe pid set (all WT windows share one process, so a window's own
    /// process tells nothing). Dependencies arrive as parameters: the pid set, the desktop
    /// reader (MON-003 IDesktopSource), and the monitor set (MON-003 MonitorInfo) for
    /// attribution by the HMONITOR device name. A failed
    /// acquisition of one window degrades to that window being skipped; nothing throws.
    /// </summary>
    // @MX:WARN: real-screen behaviour, morning-checklist surface (tools/dump-windows.ps1); not an acceptance claim.
    public sealed class Win32WindowEnumerator
    {
        private readonly WindowMonitorDeviceName monitorResolver;
        public Win32WindowEnumerator() : this(new Win32WindowMonitorResolver().GetDeviceName) { }
        public Win32WindowEnumerator(WindowMonitorDeviceName resolver)
        {
            if (resolver == null) throw new ArgumentNullException("resolver");
            monitorResolver = resolver;
        }
        public static MonitorInfo ResolveMonitorByDeviceName(string deviceName, MonitorInfo[] monitors)
        {
            if (string.IsNullOrEmpty(deviceName)) return null;
            MonitorInfo result = null;
            foreach (MonitorInfo monitor in monitors ?? new MonitorInfo[0])
                if (monitor != null && string.Equals(deviceName, monitor.DeviceName, StringComparison.Ordinal))
                {
                    if (result != null) return null;
                    result = monitor;
                }
            return result;
        }
        /// <summary>The Windows Terminal top-level window class (tech.md probe fact).</summary>
        public const string TerminalWindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

        /// <summary>Enumerates the matching windows in ZOrder. A null desktop reader degrades to a failure status per window.</summary>
        public EnumeratedWindow[] Enumerate(int[] terminalPids, IDesktopSource desktopReader, MonitorInfo[] monitors)
        {
            List<EnumeratedWindow> windows = new List<EnumeratedWindow>();
            HashSet<int> pids = BuildPidSet(terminalPids);
            WindowCollector collector = new WindowCollector(pids, desktopReader, monitors, monitorResolver);
            try
            {
                NativeMethods.EnumWindows(new NativeMethods.EnumWindowsProc(collector.Callback), IntPtr.Zero);
            }
            catch
            {
                // Enumeration failure degrades to the windows collected so far.
            }
            return collector.ToArray();
        }

        private static HashSet<int> BuildPidSet(int[] terminalPids)
        {
            HashSet<int> pids = new HashSet<int>();
            if (terminalPids == null)
            {
                return pids;
            }
            foreach (int pid in terminalPids)
            {
                pids.Add(pid);
            }
            return pids;
        }

        /// <summary>Accumulates the matching windows; an error in one window's acquisition skips it (REQ-ACQ-001).</summary>
        private sealed class WindowCollector
        {
            private readonly HashSet<int> pids;
            private readonly IDesktopSource desktopReader;
            private readonly MonitorInfo[] monitors;
            private readonly List<EnumeratedWindow> windows = new List<EnumeratedWindow>();

            private readonly WindowMonitorDeviceName resolver;
            public WindowCollector(HashSet<int> pids, IDesktopSource desktopReader, MonitorInfo[] monitors, WindowMonitorDeviceName resolver)
            {
                this.resolver = resolver;
                this.pids = pids;
                this.desktopReader = desktopReader;
                this.monitors = monitors;
            }

            public EnumeratedWindow[] ToArray()
            {
                return windows.ToArray();
            }

            public bool Callback(IntPtr handle, IntPtr lParam)
            {
                try
                {
                    if (!NativeMethods.IsWindowVisible(handle)
                        || !IsTerminalClass(handle)
                        || !BelongsToTerminalPids(handle))
                    {
                        return true;
                    }
                    DesktopFlagResult desktop = desktopReader == null
                        ? DesktopFlagResult.Failure("no desktop reader supplied")
                        : desktopReader.IsWindowOnCurrentVirtualDesktop(handle);
                    MonitorInfo monitor = ResolveWindowMonitor(handle);
                    windows.Add(new EnumeratedWindow(handle, monitor, desktop, WindowInspector.ReadIdentity(handle)));
                }
                catch
                {
                    // A failed acquisition skips this window; enumeration continues.
                }
                return true;
            }

            private static bool IsTerminalClass(IntPtr handle)
            {
                StringBuilder name = new StringBuilder(256);
                int length = NativeMethods.GetClassName(handle, name, name.Capacity);
                if (length <= 0)
                {
                    return false;
                }
                return string.Equals(name.ToString(), TerminalWindowClass, StringComparison.Ordinal);
            }

            private bool BelongsToTerminalPids(IntPtr handle)
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(handle, out pid);
                return pids.Contains((int)pid);
            }

            /// <summary>Unknown or ambiguous HMONITOR device names fail closed.</summary>
            private MonitorInfo ResolveWindowMonitor(IntPtr handle)
            {
                return ResolveMonitorByDeviceName(resolver(handle), monitors);
            }
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; no assembly reference needed).</summary>
        private static class NativeMethods
        {
            public delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr window);

            [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
            public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr window, out Rect rect);

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }
        }
    }

    public delegate string WindowMonitorDeviceName(IntPtr window);

    public sealed class Win32WindowMonitorResolver
    {
        public string GetDeviceName(IntPtr window)
        {
            IntPtr monitor = MonitorFromWindow(window, 2);
            MonitorInfoEx info = new MonitorInfoEx();
            info.Size = Marshal.SizeOf(typeof(MonitorInfoEx));
            return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.Device : null;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfoEx
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
    }
}
