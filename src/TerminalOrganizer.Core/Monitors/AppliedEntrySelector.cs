using System;
using TerminalOrganizer.Core.Layouts;

namespace TerminalOrganizer.Core.Monitors
{
    /// <summary>
    /// Two-tier applied-entry selection (REQ-SEL-001, REQ-SEL-002, REQ-VDT-002). Pure: it never
    /// mutates the document, never considers rejected entries, and never falls back to exact
    /// matching. Exact-match resolution stays with LayoutResolver (SPEC-LAYOUT-001 untouched).
    /// Tier 1 takes the first instance-exact entry (the serial never vetoes there); tier 2 is
    /// the legacy FancyZones fuzzy rule, consulted only when no instance-exact entry exists.
    /// </summary>
    public static class AppliedEntrySelector
    {
        /// <summary>
        /// Tier 1, the instance-exact rule: the entry monitor equals the monitor id ordinally,
        /// the entry monitor-instance equals the identity instance ordinally, and, when the
        /// current desktop is known, the entry's virtual desktop equals it by GUID value. The
        /// serial NEVER vetoes this tier and is never used to rank candidates: live evidence
        /// (2026-09-26) shows FancyZones cross-wires serials across monitor rows and replaces
        /// the live episode's row in place, so the just-applied row can carry a serial that
        /// mismatches the monitor's real EDID serial while stale ghost rows keep the real one.
        /// Monitor instances are stable and always correct; serials in the file are not.
        /// </summary>
        private static bool InstanceExact(DeviceKey entryDevice, MonitorIdentity identity, string currentDesktop)
        {
            if (entryDevice == null || identity == null)
            {
                return false;
            }
            if (!string.Equals(entryDevice.Monitor, identity.MonitorId, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.Equals(entryDevice.MonitorInstance, identity.Instance, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(currentDesktop)
                && !DeviceKey.GuidOrOrdinalEquals(entryDevice.VirtualDesktop, currentDesktop))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Tier 2, the FancyZones fuzzy device rule (REQ-SEL-001): the entry monitor equals the
        /// monitor id ordinally; the serials are equal or either side is empty; the instances are
        /// equal or the monitor numbers are equal; and, when the current desktop is known, the
        /// entry's virtual desktop equals it by GUID value. A null or empty currentDesktop means
        /// the desktop is unknown and the desktop condition is skipped (the REQ-VDT-002 fallback
        /// shape — Match with an unknown desktop is the desktop-ignored matcher).
        /// </summary>
        public static bool Match(DeviceKey entryDevice, MonitorIdentity identity, string currentDesktop)
        {
            if (entryDevice == null || identity == null)
            {
                return false;
            }
            if (!string.Equals(entryDevice.Monitor, identity.MonitorId, StringComparison.Ordinal))
            {
                return false;
            }
            bool serialMatches = string.Equals(entryDevice.SerialNumber, identity.Serial, StringComparison.Ordinal)
                || string.IsNullOrEmpty(entryDevice.SerialNumber)
                || string.IsNullOrEmpty(identity.Serial);
            if (!serialMatches)
            {
                return false;
            }
            bool instanceOrNumberMatches = string.Equals(entryDevice.MonitorInstance, identity.Instance, StringComparison.Ordinal)
                || entryDevice.MonitorNumber == identity.Number;
            if (!instanceOrNumberMatches)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(currentDesktop)
                && !DeviceKey.GuidOrOrdinalEquals(entryDevice.VirtualDesktop, currentDesktop))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns the first entry in document order that matches the identity and the current
        /// desktop (REQ-SEL-001): tier 1 (instance-exact, no serial veto) first; when no
        /// instance-exact entry exists, tier 2 (the legacy fuzzy rule). When the desktop is
        /// unknown (null or empty), the REQ-VDT-002 single-entry fallback applies within each
        /// tier in order: exactly one tier-1 entry matching while the desktop field is ignored
        /// is the match; zero or several fall through to the tier-2 single-entry rule, where
        /// zero or several again yield null.
        /// </summary>
        // @MX:ANCHOR: [AUTO] Two-tier selection (FancyZonesDataTypes.h:171-213 + 2026-09-26 live cross-wired-serial
        // evidence); drives every organize action through TrayContext.
        // @MX:REASON: the instance-exact tier must not serial-veto — FancyZones stamps cross-wired serials on the
        // live episode's rows while stale ghost rows keep the real EDID serial, so a serial veto rejects the
        // just-applied row (or an entire virtual desktop) and lets a dead episode's row win instead.
        public static AppliedLayoutEntry Select(AppliedLayoutsDocument document, MonitorIdentity identity, string currentDesktop)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
            if (identity == null)
            {
                throw new ArgumentNullException("identity");
            }
            if (!string.IsNullOrEmpty(currentDesktop))
            {
                foreach (AppliedLayoutEntry candidate in document.Entries)
                {
                    if (InstanceExact(candidate.Device, identity, currentDesktop))
                    {
                        return candidate;
                    }
                }
                foreach (AppliedLayoutEntry candidate in document.Entries)
                {
                    if (Match(candidate.Device, identity, currentDesktop))
                    {
                        return candidate;
                    }
                }
                return null;
            }
            AppliedLayoutEntry single = SingleEntry(document, identity, true);
            if (single != null)
            {
                return single;
            }
            return SingleEntry(document, identity, false);
        }

        /// <summary>
        /// The REQ-VDT-002 single-entry rule over one tier with the desktop field ignored:
        /// exactly one match is the match; zero or several yield null.
        /// </summary>
        private static AppliedLayoutEntry SingleEntry(AppliedLayoutsDocument document, MonitorIdentity identity, bool instanceExactTier)
        {
            AppliedLayoutEntry single = null;
            foreach (AppliedLayoutEntry candidate in document.Entries)
            {
                bool matched = instanceExactTier
                    ? InstanceExact(candidate.Device, identity, null)
                    : Match(candidate.Device, identity, null);
                if (matched)
                {
                    if (single != null)
                    {
                        return null;
                    }
                    single = candidate;
                }
            }
            return single;
        }
    }
}
