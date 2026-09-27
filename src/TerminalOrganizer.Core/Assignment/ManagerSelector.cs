using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Assignment
{
    /// <summary>
    /// What a persisted manager choice is keyed on (night-design-2026-09-25 unit B2): a
    /// canonical launcher session name (survives active-tab changes), a run-scoped user
    /// label, or a raw window title (legacy compatibility). None marks "no choice".
    /// </summary>
    public enum ManagerSelectorKind
    {
        None,
        Session,
        UserLabel,
        RawTitle
    }

    /// <summary>
    /// The persisted canonical manager identity (B2): the kind, its value (session name,
    /// label or raw title) and the raw-title fallback captured at choice time. Never a
    /// HWND, a first-tab display text alone, a monitor number or a transient rect. The
    /// RawTitleFallback is the only field legacy raw-title matching consumes. Immutable.
    /// </summary>
    public sealed class ManagerSelector
    {
        private readonly ManagerSelectorKind kind;
        private readonly string value;
        private readonly string rawTitleFallback;

        public ManagerSelector(ManagerSelectorKind kind, string value, string rawTitleFallback)
        {
            this.kind = kind;
            this.value = value;
            this.rawTitleFallback = rawTitleFallback;
        }

        public ManagerSelectorKind Kind { get { return kind; } }
        public string Value { get { return value; } }
        public string RawTitleFallback { get { return rawTitleFallback; } }

        /// <summary>True when no choice is persisted (kind None, or no value at all).</summary>
        public bool IsEmpty
        {
            get { return kind == ManagerSelectorKind.None || string.IsNullOrEmpty(value); }
        }

        /// <summary>The no-choice selector (B2 Clear manager persists exactly this).</summary>
        public static ManagerSelector None()
        {
            return new ManagerSelector(ManagerSelectorKind.None, null, null);
        }

        public override bool Equals(object obj)
        {
            ManagerSelector other = obj as ManagerSelector;
            if (other == null)
            {
                return false;
            }
            return kind == other.kind
                && string.Equals(value, other.value, StringComparison.Ordinal)
                && string.Equals(rawTitleFallback, other.rawTitleFallback, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            return kind.GetHashCode()
                ^ (value == null ? 0 : value.GetHashCode())
                ^ (rawTitleFallback == null ? 0 : rawTitleFallback.GetHashCode());
        }

        public override string ToString()
        {
            return string.Format("{0}|{1}", kind, value);
        }
    }

    /// <summary>The outcome of resolving a persisted selector against the live windows (B2).</summary>
    public enum ManagerResolutionStatus
    {
        /// <summary>No selector was persisted; nothing to resolve.</summary>
        None,

        /// <summary>Exactly one valid identified non-full-screen window matched.</summary>
        Resolved,

        /// <summary>No window matched (the controller suppresses the notice when there are no windows at all).</summary>
        NotFound,

        /// <summary>More than one window matched; never resolved to a first match.</summary>
        Ambiguous,

        /// <summary>The single match is not an identified window.</summary>
        Unidentified,

        /// <summary>The single match is full screen (or its state cannot be mutated).</summary>
        FullScreen
    }

    /// <summary>
    /// One resolution outcome (B2): the status, the resolved window id and handle when
    /// Resolved, the number of matching windows (the ambiguity count), and a reason for
    /// the non-resolved statuses. Immutable.
    /// </summary>
    public sealed class ManagerResolution
    {
        private readonly ManagerResolutionStatus status;
        private readonly string windowId;
        private readonly IntPtr handle;
        private readonly int candidateCount;
        private readonly string reason;

        public ManagerResolution(ManagerResolutionStatus status, string windowId, IntPtr handle,
            int candidateCount, string reason)
        {
            this.status = status;
            this.windowId = windowId;
            this.handle = handle;
            this.candidateCount = candidateCount;
            this.reason = reason;
        }

        public ManagerResolutionStatus Status { get { return status; } }
        public string WindowId { get { return windowId; } }
        public IntPtr Handle { get { return handle; } }
        public int CandidateCount { get { return candidateCount; } }
        public string Reason { get { return reason; } }

        public override string ToString()
        {
            return string.Format("{0}|{1}|candidates={2}", status, windowId, candidateCount);
        }
    }

    /// <summary>
    /// Creates and resolves canonical manager selectors (B2). Pure: no Win32 calls; the
    /// inputs are discovery outputs joined by handle. Creation prefers the single
    /// canonical session, then a run-scoped user label, then the raw title; resolution
    /// NEVER takes a first match — more than one matching window is Ambiguous.
    /// </summary>
    public static class ManagerResolver
    {
        // @MX:ANCHOR: [AUTO] canonical manager identity consumed by the tray menu (B2) and the ZoneAssigner resolution overload.
        // @MX:REASON: every manager action (choose, show, clear, organize pin) resolves through this call; a first-match shortcut here would silently pin the wrong window.
        /// <summary>
        /// Creates the selector for one chosen window (B2 click-time rules): a single
        /// trusted canonical session wins; otherwise a run-scoped user label; otherwise
        /// the raw title. Never persists a HWND, monitor number or transient rect.
        /// </summary>
        public static ManagerSelector CreateSelector(WindowSnapshot snapshot, string userLabel)
        {
            if (snapshot == null)
            {
                return ManagerSelector.None();
            }
            string rawTitle = ResolveRawTitle(snapshot);
            string sessionName;
            if (TrySingleCanonicalSession(snapshot, out sessionName))
            {
                return new ManagerSelector(ManagerSelectorKind.Session, sessionName, rawTitle);
            }
            if (!string.IsNullOrEmpty(userLabel))
            {
                return new ManagerSelector(ManagerSelectorKind.UserLabel, userLabel, rawTitle);
            }
            return new ManagerSelector(ManagerSelectorKind.RawTitle, rawTitle, rawTitle);
        }

        /// <summary>
        /// Resolves a persisted selector against the live windows (B2): Session and
        /// UserLabel match first and fall back to the raw title on zero hits; exactly one
        /// valid identified non-full-screen match is Resolved, more than one is Ambiguous
        /// (never first-match), a full-screen-only match is FullScreen, an unidentified
        /// single match is Unidentified, and no match at all is NotFound.
        /// </summary>
        public static ManagerResolution Resolve(ManagerSelector selector, WindowFact[] facts, WindowSnapshot[] snapshots)
        {
            if (selector == null || selector.IsEmpty)
            {
                return new ManagerResolution(ManagerResolutionStatus.None, null, IntPtr.Zero, 0, null);
            }
            List<WindowPair> matches = Match(selector.Kind, selector.Value, facts, snapshots);
            if (matches.Count == 0 && selector.Kind != ManagerSelectorKind.RawTitle
                && !string.IsNullOrEmpty(selector.RawTitleFallback))
            {
                matches = Match(ManagerSelectorKind.RawTitle, selector.RawTitleFallback, facts, snapshots);
            }
            if (matches.Count == 0)
            {
                return new ManagerResolution(ManagerResolutionStatus.NotFound, null, IntPtr.Zero, 0, "window not found");
            }
            if (matches.Count > 1)
            {
                return new ManagerResolution(ManagerResolutionStatus.Ambiguous, null, IntPtr.Zero,
                    matches.Count, "ambiguous manager name");
            }
            WindowPair match = matches[0];
            if (match.Snapshot == null || !match.Snapshot.Identified)
            {
                return new ManagerResolution(ManagerResolutionStatus.Unidentified, null, IntPtr.Zero, 1, "window unidentified");
            }
            if (match.Fact == null || match.Fact.FullScreen)
            {
                return new ManagerResolution(ManagerResolutionStatus.FullScreen, null, IntPtr.Zero, 1, "window full screen");
            }
            return new ManagerResolution(ManagerResolutionStatus.Resolved, match.Fact.Id, match.Fact.Handle, 1, null);
        }

        /// <summary>
        /// The run-scoped label a snapshot carries: a labeled window is rebuilt with one
        /// synthetic session whose command line is null (canonical sessions always carry
        /// the verbatim command line), so that is the label marker.
        /// </summary>
        public static string DeriveUserLabel(WindowSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }
            TabSnapshot[] tabs = snapshot.Tabs;
            if (tabs.Length != 1 || tabs[0] == null || tabs[0].Session == null)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(tabs[0].Session.CommandLine))
            {
                return null;
            }
            return tabs[0].Session.Name;
        }

        /// <summary>Collects every window matching one keyed lookup, joined by handle.</summary>
        private static List<WindowPair> Match(ManagerSelectorKind kind, string value, WindowFact[] facts, WindowSnapshot[] snapshots)
        {
            List<WindowPair> matches = new List<WindowPair>();
            if (string.IsNullOrEmpty(value))
            {
                return matches;
            }
            Dictionary<long, WindowFact> factsByHandle = IndexFacts(facts);
            Dictionary<long, bool> seen = new Dictionary<long, bool>();
            if (snapshots != null)
            {
                foreach (WindowSnapshot snapshot in snapshots)
                {
                    if (snapshot == null)
                    {
                        continue;
                    }
                    long key = snapshot.Handle.ToInt64();
                    if (seen.ContainsKey(key))
                    {
                        continue;
                    }
                    seen[key] = true;
                    WindowFact fact;
                    factsByHandle.TryGetValue(key, out fact);
                    if (Matches(kind, value, fact, snapshot))
                    {
                        matches.Add(new WindowPair(fact, snapshot));
                    }
                }
            }
            if (facts != null)
            {
                foreach (WindowFact fact in facts)
                {
                    if (fact == null)
                    {
                        continue;
                    }
                    long key = fact.Handle.ToInt64();
                    if (seen.ContainsKey(key))
                    {
                        continue;
                    }
                    seen[key] = true;
                    if (Matches(kind, value, fact, null))
                    {
                        matches.Add(new WindowPair(fact, null));
                    }
                }
            }
            return matches;
        }

        private static bool Matches(ManagerSelectorKind kind, string value, WindowFact fact, WindowSnapshot snapshot)
        {
            if (kind == ManagerSelectorKind.Session && snapshot != null)
            {
                foreach (TabSnapshot tab in snapshot.Tabs)
                {
                    // Synthetic label sessions never satisfy a canonical session lookup.
                    if (tab != null && tab.Session != null
                        && !string.IsNullOrEmpty(tab.Session.CommandLine)
                        && string.Equals(tab.Session.Name, value, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (kind == ManagerSelectorKind.UserLabel)
            {
                return snapshot != null && string.Equals(DeriveUserLabel(snapshot), value, StringComparison.Ordinal);
            }
            return fact != null && string.Equals(fact.Name, value, StringComparison.Ordinal);
        }

        /// <summary>The single distinct canonical session name, when the trusted tabs carry exactly one.</summary>
        private static bool TrySingleCanonicalSession(WindowSnapshot snapshot, out string sessionName)
        {
            sessionName = null;
            if (!snapshot.HasTrustedCompleteTabs)
            {
                return false;
            }
            foreach (TabSnapshot tab in snapshot.Tabs)
            {
                if (tab == null || tab.Session == null || string.IsNullOrEmpty(tab.Session.CommandLine))
                {
                    continue;
                }
                if (sessionName == null)
                {
                    sessionName = tab.Session.Name;
                }
                else if (!string.Equals(sessionName, tab.Session.Name, StringComparison.Ordinal))
                {
                    sessionName = null;
                    return false;
                }
            }
            return sessionName != null;
        }

        private static string ResolveRawTitle(WindowSnapshot snapshot)
        {
            if (snapshot.Identity != null && !string.IsNullOrEmpty(snapshot.Identity.RawWindowTitle))
            {
                return snapshot.Identity.RawWindowTitle;
            }
            TabSnapshot[] tabs = snapshot.Tabs;
            return tabs.Length > 0 && tabs[0] != null ? tabs[0].Title : null;
        }

        private static Dictionary<long, WindowFact> IndexFacts(WindowFact[] facts)
        {
            Dictionary<long, WindowFact> index = new Dictionary<long, WindowFact>();
            if (facts != null)
            {
                foreach (WindowFact fact in facts)
                {
                    if (fact != null && !index.ContainsKey(fact.Handle.ToInt64()))
                    {
                        index[fact.Handle.ToInt64()] = fact;
                    }
                }
            }
            return index;
        }

        /// <summary>One window as the two discovery outputs see it, joined by handle; either half may be null.</summary>
        private sealed class WindowPair
        {
            public readonly WindowFact Fact;
            public readonly WindowSnapshot Snapshot;

            public WindowPair(WindowFact fact, WindowSnapshot snapshot)
            {
                this.Fact = fact;
                this.Snapshot = snapshot;
            }
        }
    }
}
