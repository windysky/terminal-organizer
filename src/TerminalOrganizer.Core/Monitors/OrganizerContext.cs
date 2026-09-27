using System;
using TerminalOrganizer.Core.Geometry;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// The resolver inputs composed from one selected applied entry and one monitor
    /// (REQ-CMP-001): the entry's own DeviceKey plus the monitor's work area.
    /// </summary>
    public sealed class OrganizerDeviceContext
    {
        private readonly DeviceKey key;
        private readonly WorkArea workArea;

        public OrganizerDeviceContext(DeviceKey key, WorkArea workArea)
        {
            this.key = key;
            this.workArea = workArea;
        }

        /// <summary>The selected entry's own DeviceKey, ready for LayoutResolver.ResolveByDeviceKey.</summary>
        public DeviceKey Key { get { return key; } }

        /// <summary>The monitor's work area in physical pixels and its DPI.</summary>
        public WorkArea WorkArea { get { return workArea; } }
    }

    /// <summary>
    /// Builds the resolution context from the selected entry and the chosen monitor
    /// (REQ-CMP-001). Rejects a null monitor with ArgumentNullException; returns the entry's
    /// own DeviceKey without copying the entry, keeping exact-match resolution in the
    /// resolver (SPEC-LAYOUT-001 untouched).
    /// </summary>
    public static class OrganizerContext
    {
        public static OrganizerDeviceContext Build(AppliedLayoutEntry selectedEntry, MonitorInfo monitor)
        {
            if (monitor == null)
            {
                throw new ArgumentNullException("monitor");
            }
            if (selectedEntry == null)
            {
                throw new ArgumentNullException("selectedEntry");
            }
            return new OrganizerDeviceContext(selectedEntry.Device, monitor.WorkArea);
        }
    }
}
