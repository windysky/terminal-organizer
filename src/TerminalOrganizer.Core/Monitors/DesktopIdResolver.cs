using System;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// Resolves the current virtual-desktop GUID (REQ-VDT-001), in order: the Win10 session
    /// value, the Win11 value, the desktop id of a known current-desktop window; null when
    /// none supplies a value that parses as a GUID. Pure — every input is a plain string,
    /// so plan.md J.0 holds: step 3 takes the desktop-id VALUE, not a window handle; the
    /// caller (SPEC-WIN-004 or the tray app) supplies the value from GetWindowDesktopId.
    /// </summary>
    public static class DesktopIdResolver
    {
        // @MX:NOTE: tech.md precedence SSOT — Win10 session value, Win11 value, GetWindowDesktopId, null.
        public static Guid? Resolve(string win10SessionValue, string win11Value, string windowDesktopIdValue)
        {
            Guid parsed;
            if (TryParseGuid(win10SessionValue, out parsed))
            {
                return parsed;
            }
            if (TryParseGuid(win11Value, out parsed))
            {
                return parsed;
            }
            if (TryParseGuid(windowDesktopIdValue, out parsed))
            {
                return parsed;
            }
            return null;
        }

        private static bool TryParseGuid(string value, out Guid parsed)
        {
            if (string.IsNullOrEmpty(value))
            {
                parsed = Guid.Empty;
                return false;
            }
            return Guid.TryParse(value, out parsed);
        }
    }
}
