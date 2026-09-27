using System;
using TerminalOrganizer.Core.Geometry;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// The four FancyZones key fields a live monitor contributes (REQ-SEL-001 query side):
    /// parsed monitor id and instance, the 1-based enumeration number, and the EDID serial.
    /// </summary>
    public sealed class MonitorIdentity
    {
        private readonly string monitorId;
        private readonly string instance;
        private readonly int number;
        private readonly string serial;

        public MonitorIdentity(string monitorId, string instance, int number, string serial)
        {
            this.monitorId = monitorId;
            this.instance = instance;
            this.number = number;
            this.serial = serial;
        }

        public string MonitorId { get { return monitorId; } }
        public string Instance { get { return instance; } }
        public int Number { get { return number; } }
        public string Serial { get { return serial; } }

        public override string ToString()
        {
            return string.Format("{0}|{1}|{2}|{3}", monitorId, instance, number, serial);
        }
    }

    /// <summary>
    /// One attached monitor as the provider reports it (REQ-ID-003): interface path, parsed
    /// id/instance, EDID serial, 1-based monitor number, the full monitor rect, and the work
    /// area rect with its DPI. Acquisition failures degrade to empty or zero fields, never
    /// to exceptions or a missing entry.
    /// </summary>
    public sealed class MonitorInfo
    {
        private readonly string interfacePath;
        private readonly string monitorId;
        private readonly string instance;
        private readonly string serial;
        private readonly int number;
        private readonly int monitorLeft;
        private readonly int monitorTop;
        private readonly int monitorWidth;
        private readonly int monitorHeight;
        private readonly WorkArea workArea;

        public MonitorInfo(string interfacePath, string monitorId, string instance, string serial, int number,
            int monitorLeft, int monitorTop, int monitorWidth, int monitorHeight, WorkArea workArea)
            : this(null, interfacePath, monitorId, instance, serial, number, false,
                monitorLeft, monitorTop, monitorWidth, monitorHeight, workArea)
        {
        }

        public MonitorInfo(string deviceName, string interfacePath, string monitorId, string instance, string serial, int number,
            bool primary, int monitorLeft, int monitorTop, int monitorWidth, int monitorHeight, WorkArea workArea)
        {
            DeviceName = deviceName;
            Primary = primary;
            this.interfacePath = interfacePath;
            this.monitorId = monitorId;
            this.instance = instance;
            this.serial = serial;
            this.number = number;
            this.monitorLeft = monitorLeft;
            this.monitorTop = monitorTop;
            this.monitorWidth = monitorWidth;
            this.monitorHeight = monitorHeight;
            this.workArea = workArea;
        }

        public string InterfacePath { get { return interfacePath; } }
        public string DeviceName { get; private set; }
        public bool Primary { get; private set; }
        public MonitorKey StableKey { get { return new MonitorKey(serial, interfacePath, monitorId, instance); } }
        public string MonitorId { get { return monitorId; } }
        public string Instance { get { return instance; } }
        public string Serial { get { return serial; } }

        /// <summary>1-based index in EnumDisplayDevices enumeration order (plan.md B5).</summary>
        public int Number { get { return number; } }

        public int MonitorLeft { get { return monitorLeft; } }
        public int MonitorTop { get { return monitorTop; } }
        public int MonitorWidth { get { return monitorWidth; } }
        public int MonitorHeight { get { return monitorHeight; } }

        /// <summary>The work area rect in physical pixels and its DPI; an invalid WorkArea marks a degraded read.</summary>
        public WorkArea WorkArea { get { return workArea; } }

        /// <summary>The identity fields (id, instance, number, serial) for the fuzzy selector.</summary>
        public MonitorIdentity ToIdentity()
        {
            return new MonitorIdentity(monitorId, instance, number, serial);
        }
    }

    /// <summary>
    /// Enumerates the attached monitors (REQ-ID-003). Implementations sit behind this
    /// interface so pure consumers are testable with fakes; they never throw for
    /// environment failures — failures degrade to empty fields on that monitor.
    /// </summary>
    public interface IMonitorProvider
    {
        MonitorInfo[] GetMonitors();
    }

    /// <summary>Outcome of a desktop registry value read (REQ-VDT-001 steps 1-2).</summary>
    public enum DesktopReadStatus
    {
        /// <summary>The value exists; Value carries its normalized text (REG_SZ text or a braced GUID built from REG_BINARY bytes).</summary>
        Present,

        /// <summary>The key or value does not exist on this machine.</summary>
        Absent,

        /// <summary>The read failed (permissions, unexpected value kind); never thrown.</summary>
        Failed
    }

    /// <summary>A desktop registry read result (REQ-VDT-001, REQ-VDT-003).</summary>
    public sealed class DesktopReadResult
    {
        private readonly DesktopReadStatus status;
        private readonly string value;

        private DesktopReadResult(DesktopReadStatus status, string value)
        {
            this.status = status;
            this.value = value;
        }

        public DesktopReadStatus Status { get { return status; } }

        /// <summary>The normalized value text when Present; null otherwise.</summary>
        public string Value { get { return value; } }

        public static DesktopReadResult Present(string value)
        {
            return new DesktopReadResult(DesktopReadStatus.Present, value);
        }

        public static DesktopReadResult Absent()
        {
            return new DesktopReadResult(DesktopReadStatus.Absent, null);
        }

        public static DesktopReadResult Failed()
        {
            return new DesktopReadResult(DesktopReadStatus.Failed, null);
        }
    }

    /// <summary>The outcome of IVirtualDesktopManager.GetWindowDesktopId (REQ-VDT-003): success with the GUID, or failure with a reason.</summary>
    public sealed class DesktopIdResult
    {
        private readonly bool success;
        private readonly Guid value;
        private readonly string error;

        private DesktopIdResult(bool success, Guid value, string error)
        {
            this.success = success;
            this.value = value;
            this.error = error;
        }

        public bool Success { get { return success; } }
        public Guid Value { get { return value; } }
        public string Error { get { return error; } }

        public static DesktopIdResult Ok(Guid value)
        {
            return new DesktopIdResult(true, value, null);
        }

        public static DesktopIdResult Failure(string error)
        {
            return new DesktopIdResult(false, Guid.Empty, error);
        }
    }

    /// <summary>The outcome of IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop (REQ-VDT-003): success with the flag, or failure with a reason.</summary>
    public sealed class DesktopFlagResult
    {
        private readonly bool success;
        private readonly bool value;
        private readonly string error;

        private DesktopFlagResult(bool success, bool value, string error)
        {
            this.success = success;
            this.value = value;
            this.error = error;
        }

        public bool Success { get { return success; } }
        public bool Value { get { return value; } }
        public string Error { get { return error; } }

        public static DesktopFlagResult Ok(bool value)
        {
            return new DesktopFlagResult(true, value, null);
        }

        public static DesktopFlagResult Failure(string error)
        {
            return new DesktopFlagResult(false, false, error);
        }
    }

    /// <summary>
    /// Supplies the current-desktop inputs (REQ-VDT-003): the two registry reads of
    /// REQ-VDT-001 steps 1-2 and the documented IVirtualDesktopManager queries. A failed
    /// COM activation or registry read returns a failure result, never an exception.
    /// </summary>
    public interface IDesktopSource
    {
        /// <summary>Reads the Win10 session value (REQ-VDT-001 step 1).</summary>
        DesktopReadResult TryReadWin10SessionValue();

        /// <summary>Reads the Win11 value (REQ-VDT-001 step 2).</summary>
        DesktopReadResult TryReadWin11Value();

        /// <summary>The desktop id of a window via IVirtualDesktopManager (REQ-VDT-001 step 3).</summary>
        DesktopIdResult GetWindowDesktopId(IntPtr window);

        /// <summary>Whether a window is on the current virtual desktop (REQ-VDT-003).</summary>
        DesktopFlagResult IsWindowOnCurrentVirtualDesktop(IntPtr window);
    }
}
