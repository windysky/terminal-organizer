using System;
using System.Runtime.InteropServices;

namespace TerminalOrganizer.Core.Assignment
{
    public enum WindowStateReadStatus { Valid, RectUnavailable, StyleUnavailable, MonitorUnavailable, Failed }
    /// <summary>
    /// One window's acquired rect and state flags (REQ-PLC-002): left/top/width/height in
    /// screen pixels plus the maximized, minimized and full-screen flags. Immutable; a
    /// degraded read is explicitly invalid and never permits mutation.
    /// </summary>
    public sealed class WindowState
    {
        private readonly int left;
        private readonly int top;
        private readonly int width;
        private readonly int height;
        private readonly bool maximized;
        private readonly bool minimized;
        private readonly bool fullScreen;
        private readonly FrameMargins margins;

        public WindowState(int left, int top, int width, int height, bool maximized, bool minimized, bool fullScreen)
            : this(WindowStateReadStatus.Valid, null, left, top, width, height, maximized, minimized, fullScreen)
        {
        }

        public WindowState(int left, int top, int width, int height, bool maximized, bool minimized, bool fullScreen,
            FrameMargins margins)
            : this(WindowStateReadStatus.Valid, null, left, top, width, height, maximized, minimized, fullScreen, margins)
        {
        }

        public WindowState(WindowStateReadStatus status, string error, int left, int top, int width, int height,
            bool maximized, bool minimized, bool fullScreen)
            : this(status, error, left, top, width, height, maximized, minimized, fullScreen, FrameMargins.None)
        {
        }

        public WindowState(WindowStateReadStatus status, string error, int left, int top, int width, int height,
            bool maximized, bool minimized, bool fullScreen, FrameMargins margins)
        {
            Status = status;
            Error = error;
            this.left = left;
            this.top = top;
            this.width = width;
            this.height = height;
            this.maximized = maximized;
            this.minimized = minimized;
            this.fullScreen = fullScreen;
            this.margins = margins ?? FrameMargins.None;
        }

        public int Left { get { return left; } }
        public WindowStateReadStatus Status { get; private set; }
        public string Error { get; private set; }
        public bool IsValidForMutation { get { return Status == WindowStateReadStatus.Valid && width > 0 && height > 0; } }
        public static WindowState Invalid(WindowStateReadStatus status, string error)
        {
            return new WindowState(status == WindowStateReadStatus.Valid ? WindowStateReadStatus.Failed : status,
                error, 0, 0, 0, 0, false, false, false);
        }
        public int Top { get { return top; } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public bool Maximized { get { return maximized; } }
        public bool Minimized { get { return minimized; } }
        public bool FullScreen { get { return fullScreen; } }

        /// <summary>The invisible-border margins the rect was corrected by (None when unmeasured).</summary>
        public FrameMargins Margins { get { return margins; } }

        /// <summary>Compatibility degradation value: invalid with a diagnostic zero rect.</summary>
        public static WindowState Degrade()
        {
            return Invalid(WindowStateReadStatus.Failed, "window state unavailable");
        }

        public override string ToString()
        {
            return string.Format("{0},{1} {2}x{3} max={4} min={5} fullscreen={6}",
                left, top, width, height, maximized, minimized, fullScreen);
        }
    }

    /// <summary>
    /// The rect/state acquisition seam (REQ-PLC-002, plan.md B.5): reads a window's rect
    /// via GetWindowRect, its maximized/minimized state via IsZoomed/IsIconic, and its
    /// full-screen status via style inspection (GetWindowLong GWL_STYLE) plus the monitor
    /// rect (MonitorFromWindow/GetMonitorInfo) feeding the pinned pure computation
    /// IsFullScreenWindow. The reported rect is the VISIBLE
    /// frame (DwmGetWindowAttribute DWMWA_EXTENDED_FRAME_BOUNDS, the invisible resize border
    /// removed, FancyZones-style) so it compares directly against a zone; when the DWM read
    /// fails or the window is minimized the raw GetWindowRect is kept. A failed read returns an explicitly invalid state,
    /// never an exception. The live style inspection is a morning-checklist surface.
    /// </summary>
    // @MX:WARN: [AUTO] real-screen surface, morning checklist (tools/organize-dryrun.ps1); not an acceptance claim.
    public sealed class WindowStateReader
    {
        private const int GwlStyle = -16;
        private const int DwmwaExtendedFrameBounds = 9;

        private const long WsCaption = 0x00C00000L;
        private const long WsThickframe = 0x00040000L;

        /// <summary>
        /// Reads one window's rect and state flags (REQ-PLC-002); never throws — any failed
        /// call returns an invalid state with its failure category.
        /// </summary>
        public WindowState Read(IntPtr handle)
        {
            try
            {
                NativeMethods.Rect rect;
                if (!NativeMethods.GetWindowRect(handle, out rect))
                {
                    return WindowState.Invalid(WindowStateReadStatus.RectUnavailable, "GetWindowRect failed");
                }
                int left = rect.Left;
                int top = rect.Top;
                int width = checked(rect.Right - rect.Left);
                int height = checked(rect.Bottom - rect.Top);
                if (width <= 0 || height <= 0) return WindowState.Invalid(WindowStateReadStatus.RectUnavailable, "empty window rectangle");
                bool maximized = NativeMethods.IsZoomed(handle);
                bool minimized = NativeMethods.IsIconic(handle);

                NativeMethods.SetLastError(0);
                int style = NativeMethods.GetWindowLong(handle, GwlStyle);
                if (style == 0 && Marshal.GetLastWin32Error() != 0)
                    return WindowState.Invalid(WindowStateReadStatus.StyleUnavailable, "GetWindowLong failed");
                NativeMethods.MonitorInfo info = new NativeMethods.MonitorInfo();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.MonitorInfo));
                IntPtr monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
                    return WindowState.Invalid(WindowStateReadStatus.MonitorUnavailable, "monitor information unavailable");
                bool fullScreen = IsFullScreenWindow(style, left, top, width, height,
                    info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
                FrameMargins margins = FrameMargins.None;
                if (!minimized)
                {
                    NativeMethods.Rect frame;
                    int hr = NativeMethods.DwmGetWindowAttribute(handle, DwmwaExtendedFrameBounds, out frame,
                        Marshal.SizeOf(typeof(NativeMethods.Rect)));
                    if (hr == 0)
                    {
                        margins = FrameBounds.Measure(rect.Left, rect.Top, rect.Right, rect.Bottom,
                            frame.Left, frame.Top, frame.Right, frame.Bottom);
                        int[] visible = FrameBounds.VisibleRect(left, top, width, height, margins);
                        if (visible[2] > 0 && visible[3] > 0)
                        {
                            left = visible[0]; top = visible[1]; width = visible[2]; height = visible[3];
                        }
                        else
                        {
                            margins = FrameMargins.None;
                        }
                    }
                }
                return new WindowState(left, top, width, height, maximized, minimized, fullScreen, margins);
            }
            catch (Exception ex)
            {
                return WindowState.Invalid(WindowStateReadStatus.Failed, ex.Message);
            }
        }

        // @MX:NOTE: heuristic was observed on the current WT build; cross-version behavior remains a manual check.
        /// <summary>
        /// The pinned pure full-screen computation (acceptance AC-011): a borderless window
        /// — no WS_CAPTION and no WS_THICKFRAME in its style — whose rect covers the
        /// monitor rect. Injected inputs only; the live style inspection feeding it is a
        /// morning-checklist surface.
        /// </summary>
        public static bool IsFullScreenWindow(long style, int left, int top, int width, int height,
            int monitorLeft, int monitorTop, int monitorWidth, int monitorHeight)
        {
            if ((style & (WsCaption | WsThickframe)) != 0L)
            {
                return false;
            }
            int right = left + width;
            int bottom = top + height;
            int monitorRight = monitorLeft + monitorWidth;
            int monitorBottom = monitorTop + monitorHeight;
            return left <= monitorLeft && top <= monitorTop && right >= monitorRight && bottom >= monitorBottom;
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; no assembly reference needed).</summary>
        private static class NativeMethods
        {
            public const uint MonitorDefaultToNearest = 2;

            [DllImport("user32.dll")]
            public static extern bool GetWindowRect(IntPtr window, out Rect rect);

            [DllImport("dwmapi.dll")]
            public static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect value, int size);

            [DllImport("user32.dll")]
            public static extern bool IsZoomed(IntPtr window);

            [DllImport("user32.dll")]
            public static extern bool IsIconic(IntPtr window);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern int GetWindowLong(IntPtr window, int index);

            [DllImport("kernel32.dll")]
            public static extern void SetLastError(uint error);

            [DllImport("user32.dll")]
            public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

            [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
            public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MonitorInfo
            {
                public int cbSize;
                public Rect rcMonitor;
                public Rect rcWork;
                public int dwFlags;
            }
        }
    }
}
