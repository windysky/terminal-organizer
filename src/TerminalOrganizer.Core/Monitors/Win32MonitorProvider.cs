using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using TerminalOrganizer.Core.Geometry;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// Enumerates attached monitors through Win32 (REQ-ID-003): EnumDisplayDevices for the
    /// interface path, EnumDisplayMonitors + GetMonitorInfo for the rects, GetDpiForMonitor
    /// with a GetDeviceCaps fallback for the DPI, and the EDID registry read for the serial.
    /// The EDID read is an injectable seam (plan.md J.0); the production default reads
    /// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\{id}\{instance}\Device Parameters\EDID.
    /// Environment failures never throw — a monitor's failed acquisition degrades to empty
    /// or zero fields on that monitor.
    /// </summary>
    // @MX:WARN: real-screen behaviour, morning-checklist surface (dump-monitors.ps1); not an acceptance claim.
    public sealed class Win32MonitorProvider : IMonitorProvider
    {
        private const string EdidValueName = "EDID";
        private const string EnumDisplayRoot = "SYSTEM\\CurrentControlSet\\Enum\\DISPLAY";
        private const string DeviceParametersSegment = "Device Parameters";

        private readonly Func<string, string, byte[]> edidSupplier;

        /// <summary>Production constructor: EDID bytes come from the real HKLM key.</summary>
        public Win32MonitorProvider()
            : this(new Func<string, string, byte[]>(ReadEdidFromRegistry))
        {
        }

        /// <summary>Injectable seam (plan.md J.0): a supplier of EDID bytes for a monitor id + instance.</summary>
        public Win32MonitorProvider(Func<string, string, byte[]> edidSupplier)
        {
            if (edidSupplier == null)
            {
                throw new ArgumentNullException("edidSupplier");
            }
            this.edidSupplier = edidSupplier;
        }

        /// <summary>The production EDID read: the EDID bytes, or null when the key or value is missing.</summary>
        public static byte[] ReadEdidFromRegistry(string monitorId, string instance)
        {
            if (string.IsNullOrEmpty(monitorId) || string.IsNullOrEmpty(instance))
            {
                return null;
            }
            try
            {
                string path = string.Format("{0}\\{1}\\{2}\\{3}", EnumDisplayRoot, monitorId, instance, DeviceParametersSegment);
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    return key.GetValue(EdidValueName) as byte[];
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>NFR-3: memory is bounded by the attached-monitor count.</summary>
        public MonitorInfo[] GetMonitors()
        {
            List<MonitorInfo> monitors = new List<MonitorInfo>();
            try
            {
                // Two-level enumeration: level 0 walks the display adapters, level 1 walks the
                // monitor devices of each attached adapter and yields the \\?\DISPLAY#...#{guid}
                // interface path as DeviceID (EDD_GET_DEVICE_INTERFACE_NAME).
                NativeMethods.DisplayDevice adapter = new NativeMethods.DisplayDevice();
                uint adapterIndex = 0;
                int number = 0;
                while (true)
                {
                    adapter.Cb = NativeMethods.SizeOfDisplayDevice;
                    if (!NativeMethods.EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
                    {
                        break;
                    }
                    adapterIndex++;
                    if ((adapter.StateFlags & NativeMethods.DisplayDeviceAttachedToDesktop) == 0)
                    {
                        continue;
                    }
                    AddActiveMonitors(adapter.DeviceName, monitors, ref number);
                }
            }
            catch
            {
                // Enumeration failure degrades to the monitors collected so far.
            }
            return monitors.ToArray();
        }

        private void AddActiveMonitors(string adapterName, List<MonitorInfo> monitors, ref int number)
        {
            NativeMethods.DisplayDevice monitor = new NativeMethods.DisplayDevice();
            uint monitorIndex = 0;
            while (true)
            {
                monitor.Cb = NativeMethods.SizeOfDisplayDevice;
                if (!NativeMethods.EnumDisplayDevices(adapterName, monitorIndex, ref monitor, NativeMethods.GetDeviceInterfaceName))
                {
                    break;
                }
                monitorIndex++;
                if ((monitor.StateFlags & NativeMethods.DisplayDeviceActive) == 0)
                {
                    continue;
                }
                number++;
                monitors.Add(BuildMonitorInfo(adapterName, monitor.DeviceID, number));
            }
        }

        private MonitorInfo BuildMonitorInfo(string deviceName, string interfacePath, int number)
        {
            string monitorId = string.Empty;
            string instance = string.Empty;
            string serial = string.Empty;
            try
            {
                InterfacePathParts parts = InterfacePathParser.Parse(interfacePath);
                monitorId = parts.MonitorId;
                instance = parts.Instance;
                serial = EdidParser.ParseSerial(edidSupplier(monitorId, instance));
            }
            catch
            {
                // Identity or EDID failure degrades to the empty fields already set.
            }

            int monitorLeft = 0;
            int monitorTop = 0;
            int monitorWidth = 0;
            int monitorHeight = 0;
            int workLeft = 0;
            int workTop = 0;
            int workWidth = 0;
            int workHeight = 0;
            int dpi = 0;
            bool primary = false;
            try
            {
                IntPtr monitorHandle = NativeMethods.FindMonitorByName(deviceName);
                if (monitorHandle != IntPtr.Zero)
                {
                    NativeMethods.MonitorInfoEx info = new NativeMethods.MonitorInfoEx();
                    info.Cb = NativeMethods.SizeOfMonitorInfoEx;
                    if (NativeMethods.GetMonitorInfo(monitorHandle, ref info))
                    {
                        primary = (info.Flags & 1) != 0;
                        monitorLeft = info.Monitor.Left;
                        monitorTop = info.Monitor.Top;
                        monitorWidth = info.Monitor.Right - info.Monitor.Left;
                        monitorHeight = info.Monitor.Bottom - info.Monitor.Top;
                        workLeft = info.Work.Left;
                        workTop = info.Work.Top;
                        workWidth = info.Work.Right - info.Work.Left;
                        workHeight = info.Work.Bottom - info.Work.Top;
                        dpi = GetDpi(monitorHandle);
                    }
                }
            }
            catch
            {
                // Rect or DPI failure degrades to the zero fields already set.
            }
            WorkArea workArea = new WorkArea(workLeft, workTop, workWidth, workHeight, dpi);
            return new MonitorInfo(deviceName, interfacePath, monitorId, instance, serial, number, primary,
                monitorLeft, monitorTop, monitorWidth, monitorHeight, workArea);
        }

        private static int GetDpi(IntPtr monitorHandle)
        {
            uint dpiX;
            uint dpiY;
            if (NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.EffectiveDpi, out dpiX, out dpiY) == 0)
            {
                return (int)dpiX;
            }
            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return 0;
            }
            try
            {
                return NativeMethods.GetDeviceCaps(dc, NativeMethods.LogicalPixelsX);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; no assembly reference needed).</summary>
        private static class NativeMethods
        {
            public const int DisplayDeviceAttachedToDesktop = 0x00000001;
            public const int DisplayDeviceActive = 0x00000001;

            /// <summary>EDD_GET_DEVICE_INTERFACE_NAME: DeviceID becomes the device interface path.</summary>
            public const uint GetDeviceInterfaceName = 0x00000001;

            public const int EffectiveDpi = 0;
            public const int LogicalPixelsX = 88;

            public static readonly int SizeOfDisplayDevice = Marshal.SizeOf(typeof(DisplayDevice));
            public static readonly int SizeOfMonitorInfoEx = Marshal.SizeOf(typeof(MonitorInfoEx));

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct DisplayDevice
            {
                public int Cb;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string DeviceName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceString;
                public int StateFlags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceID;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
                public string DeviceKey;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct MonitorInfoEx
            {
                public int Cb;
                public Rect Monitor;
                public Rect Work;
                public int Flags;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string DeviceName;
            }

            public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr lParam);

            [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
            public static extern bool EnumDisplayDevices(string device, uint deviceNumber, ref DisplayDevice deviceStructure, uint flags);

            [DllImport("user32.dll")]
            public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc proc, IntPtr lParam);

            [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
            public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

            [DllImport("shcore.dll")]
            public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

            [DllImport("user32.dll")]
            public static extern IntPtr GetDC(IntPtr window);

            [DllImport("user32.dll")]
            public static extern int ReleaseDC(IntPtr window, IntPtr dc);

            [DllImport("gdi32.dll")]
            public static extern int GetDeviceCaps(IntPtr dc, int index);

            public static IntPtr FindMonitorByName(string deviceName)
            {
                MonitorFinder finder = new MonitorFinder(deviceName);
                MonitorEnumProc proc = new MonitorEnumProc(finder.Callback);
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
                return finder.Result;
            }

            private sealed class MonitorFinder
            {
                private readonly string deviceName;
                private IntPtr result;

                public MonitorFinder(string deviceName)
                {
                    this.deviceName = deviceName;
                }

                public IntPtr Result
                {
                    get { return result; }
                }

                public bool Callback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr lParam)
                {
                    MonitorInfoEx info = new MonitorInfoEx();
                    info.Cb = SizeOfMonitorInfoEx;
                    if (GetMonitorInfo(monitor, ref info)
                        && string.Equals(info.DeviceName, deviceName, StringComparison.Ordinal))
                    {
                        result = monitor;
                        return false;
                    }
                    return true;
                }
            }
        }
    }
}
