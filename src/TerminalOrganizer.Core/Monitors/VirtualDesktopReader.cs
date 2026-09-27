using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TerminalOrganizer.Core.Monitors
{
    public interface IWindowDesktopSession : IDisposable
    {
        DesktopIdResult GetWindowDesktopId(IntPtr window);
        DesktopFlagResult IsWindowOnCurrentVirtualDesktop(IntPtr window);
    }

    /// <summary>One pass shares and disposes one desktop COM session.</summary>
    public sealed class WindowDesktopSessionSource : IDesktopSource, IDisposable
    {
        private readonly IWindowDesktopSession session;
        private bool disposed;
        public WindowDesktopSessionSource(IWindowDesktopSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            this.session = session;
        }
        public DesktopReadResult TryReadWin10SessionValue() { return DesktopReadResult.Absent(); }
        public DesktopReadResult TryReadWin11Value() { return DesktopReadResult.Absent(); }
        public DesktopIdResult GetWindowDesktopId(IntPtr window)
        {
            return disposed ? DesktopIdResult.Failure("desktop session disposed") : session.GetWindowDesktopId(window);
        }
        public DesktopFlagResult IsWindowOnCurrentVirtualDesktop(IntPtr window)
        {
            return disposed ? DesktopFlagResult.Failure("desktop session disposed") : session.IsWindowOnCurrentVirtualDesktop(window);
        }
        public void Dispose() { if (!disposed) { disposed = true; session.Dispose(); } }
    }
    /// <summary>
    /// The virtual-desktop acquisition wrapper (REQ-VDT-003): the two registry reads of
    /// REQ-VDT-001 steps 1-2 and the documented IVirtualDesktopManager COM interface. A
    /// failed COM activation or registry read returns a failure result, never an exception.
    /// The undocumented IVirtualDesktopManagerInternal is deliberately not declared (tech.md).
    /// </summary>
    // @MX:WARN: real-screen behaviour, morning-checklist surface (dump-monitors.ps1); not an acceptance claim.
    public sealed class VirtualDesktopReader : IDesktopSource
    {
        private const string ValueName = "CurrentVirtualDesktop";
        private const string Win10SessionKeyFormat =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\SessionInfo\\{0}\\VirtualDesktops";
        private const string Win11KeyPath =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VirtualDesktops";

        public DesktopReadResult TryReadWin10SessionValue()
        {
            using (Process process = Process.GetCurrentProcess())
                return ReadDesktopValue(string.Format(Win10SessionKeyFormat, process.SessionId));
        }

        public DesktopReadResult TryReadWin11Value()
        {
            return ReadDesktopValue(Win11KeyPath);
        }

        public DesktopIdResult GetWindowDesktopId(IntPtr window)
        {
            using (IWindowDesktopSession session = OpenSession()) return session.GetWindowDesktopId(window);
        }

        public DesktopFlagResult IsWindowOnCurrentVirtualDesktop(IntPtr window)
        {
            using (IWindowDesktopSession session = OpenSession()) return session.IsWindowOnCurrentVirtualDesktop(window);
        }

        public IWindowDesktopSession OpenSession() { return new DesktopSession(); }

        public DesktopIdResult TryGetCurrentDesktopIdFromForegroundWindow()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return DesktopIdResult.Failure("no foreground window");
                using (IWindowDesktopSession session = OpenSession())
                {
                    DesktopFlagResult flag = session.IsWindowOnCurrentVirtualDesktop(window);
                    if (flag == null || !flag.Success || !flag.Value) return DesktopIdResult.Failure("foreground desktop is unverified");
                    return session.GetWindowDesktopId(window);
                }
            }
            catch (Exception ex) { return DesktopIdResult.Failure(ex.Message); }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private sealed class DesktopSession : IWindowDesktopSession
        {
            private IVirtualDesktopManager manager;
            private string error;
            public DesktopSession()
            {
                try { manager = CreateManager(); }
                catch (Exception ex) { error = ex.Message; }
            }
            public DesktopIdResult GetWindowDesktopId(IntPtr window)
            {
                try
                {
                    if (manager == null) return DesktopIdResult.Failure(error ?? "desktop manager unavailable");
                    return DesktopIdResult.Ok(manager.GetWindowDesktopId(window));
                }
                catch (Exception ex) { return DesktopIdResult.Failure(ex.Message); }
            }
            public DesktopFlagResult IsWindowOnCurrentVirtualDesktop(IntPtr window)
            {
                try
                {
                    if (manager == null) return DesktopFlagResult.Failure(error ?? "desktop manager unavailable");
                    bool current;
                    manager.IsWindowOnCurrentVirtualDesktop(window, out current);
                    return DesktopFlagResult.Ok(current);
                }
                catch (Exception ex) { return DesktopFlagResult.Failure(ex.Message); }
            }
            public void Dispose()
            {
                IVirtualDesktopManager owned = manager;
                manager = null;
                if (owned != null && Marshal.IsComObject(owned)) Marshal.FinalReleaseComObject(owned);
            }
        }

        /// <summary>
        /// Reads one CurrentVirtualDesktop value. Both value kinds are normalized (spec 0.1.2,
        /// audit N2): REG_SZ text passes through; a 16-byte REG_BINARY GUID becomes its braced
        /// text. An absent key or value yields Absent; an unexpected kind or a failed read yields
        /// Failed — never an exception.
        /// </summary>
        private static DesktopReadResult ReadDesktopValue(string keyPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    if (key == null)
                    {
                        return DesktopReadResult.Absent();
                    }
                    object raw = key.GetValue(ValueName);
                    if (raw == null)
                    {
                        return DesktopReadResult.Absent();
                    }
                    string text = raw as string;
                    if (text != null)
                    {
                        return DesktopReadResult.Present(text);
                    }
                    byte[] bytes = raw as byte[];
                    if (bytes != null && bytes.Length == 16)
                    {
                        return DesktopReadResult.Present(new Guid(bytes).ToString("B"));
                    }
                    return DesktopReadResult.Failed();
                }
            }
            catch
            {
                return DesktopReadResult.Failed();
            }
        }

        private static IVirtualDesktopManager CreateManager()
        {
            Type type = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"));
            if (type == null)
            {
                return null;
            }
            return Activator.CreateInstance(type) as IVirtualDesktopManager;
        }

        /// <summary>The documented IVirtualDesktopManager COM interface, declared by hand (tech.md).</summary>
        [ComImport]
        [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IVirtualDesktopManager
        {
            void IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out bool onCurrentDesktop);

            Guid GetWindowDesktopId(IntPtr topLevelWindow);
        }
    }
}
