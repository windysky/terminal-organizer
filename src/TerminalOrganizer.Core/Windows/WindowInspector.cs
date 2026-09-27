using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TerminalOrganizer.Core.Windows
{
    public sealed class WindowIdentity
    {
        public WindowIdentity(IntPtr handle, int processId, long processStartTimeUtcTicks, string className, string rawWindowTitle)
        {
            Handle = handle; ProcessId = processId; ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
            ClassName = className; RawWindowTitle = rawWindowTitle;
        }
        public IntPtr Handle { get; private set; }
        public int ProcessId { get; private set; }
        public long ProcessStartTimeUtcTicks { get; private set; }
        public string ClassName { get; private set; }
        public string RawWindowTitle { get; private set; }

        // @MX:ANCHOR: fail-closed identity comparison shared by live mutation gates.
        // @MX:REASON: HWND alone can be reused; process and raw title must also remain unchanged.
        public bool EqualsForMutation(WindowIdentity other)
        {
            return other != null && Handle != IntPtr.Zero && ProcessId > 0 && ProcessStartTimeUtcTicks > 0
                && string.Equals(ClassName, Win32WindowEnumerator.TerminalWindowClass, StringComparison.Ordinal)
                && RawWindowTitle != null && Handle == other.Handle && ProcessId == other.ProcessId
                && ProcessStartTimeUtcTicks == other.ProcessStartTimeUtcTicks
                && string.Equals(ClassName, other.ClassName, StringComparison.Ordinal)
                && string.Equals(RawWindowTitle, other.RawWindowTitle, StringComparison.Ordinal);
        }
        public string StableDiagnosticKey
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}", Handle.ToInt64(), ProcessId, ProcessStartTimeUtcTicks, ClassName); }
        }
    }

    public sealed class WindowInspection
    {
        public WindowInspection(WindowIdentity identity, TabTitleResult tabs) { Identity = identity; Tabs = tabs; }
        public WindowIdentity Identity { get; private set; }
        public TabTitleResult Tabs { get; private set; }
    }

    public delegate WindowInspection WindowInspectionRead(IntPtr handle);

    public sealed class WindowInspector
    {
        private readonly UiaTabTitleReader tabReader;
        public WindowInspector() : this(new UiaTabTitleReader()) { }
        public WindowInspector(UiaTabTitleReader tabReader)
        {
            if (tabReader == null) throw new ArgumentNullException("tabReader");
            this.tabReader = tabReader;
        }
        public WindowInspection Inspect(IntPtr handle)
        {
            WindowIdentity before = ReadIdentity(handle);
            TabTitleResult tabs = tabReader.ReadTabs(handle);
            WindowIdentity after = ReadIdentity(handle);
            if (!before.EqualsForMutation(after))
                tabs = TabTitleResult.Failure(after.RawWindowTitle, "window identity changed during inspection");
            return new WindowInspection(after, tabs);
        }
        public static WindowIdentity ReadIdentity(IntPtr handle)
        {
            int pid = 0; long ticks = 0; string className = null; string title = null;
            try
            {
                uint nativePid;
                NativeMethods.GetWindowThreadProcessId(handle, out nativePid);
                pid = checked((int)nativePid);
                StringBuilder name = new StringBuilder(256);
                if (NativeMethods.GetClassName(handle, name, name.Capacity) > 0) className = name.ToString();
                title = UiaTabTitleReader.GetWindowTextTitle(handle);
                using (Process process = Process.GetProcessById(pid)) ticks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch { ticks = 0; }
            return new WindowIdentity(handle, pid, ticks, className, title);
        }
        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
            [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
            public static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
        }
    }
}
