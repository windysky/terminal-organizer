using System;
using System.Collections.Generic;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// Matches a window's tab titles against the open-session set (REQ-MCH-001..003).
    /// Per window: each title is stripped (TitleNormalizer) and matched against the session
    /// names ordinally; the window is identified only when EVERY tab matched, otherwise it
    /// is unidentified with the unmatched stripped titles listed. Merge eligibility
    /// (REQ-MCH-002) is single tab AND identified AND that tab's session is tmux-backed
    /// (Local or Remote). The matcher never reorders windows or tabs, never mutates its
    /// inputs, and never treats an unmatched title as a session.
    /// </summary>
    public static class SessionMatcher
    {
        // @MX:ANCHOR
        // @MX:REASON: identity contract feeding assignment (SPEC-ZONE-005) + merge safety (SPEC-OVERFLOW-006).
        public static WindowMatchResult Match(string[] titles, SessionRecord[] sessions)
        {
            List<TabSnapshot> tabs = new List<TabSnapshot>();
            List<string> unmatched = new List<string>();
            Dictionary<string, SessionRecord> byName = BuildNameIndex(sessions);

            int count = titles == null ? 0 : titles.Length;
            for (int i = 0; i < count; i++)
            {
                string stripped = TitleNormalizer.StripPrefix(titles[i]);
                SessionRecord session;
                if (!byName.TryGetValue(stripped, out session))
                {
                    session = null;
                }
                if (session == null)
                {
                    unmatched.Add(stripped);
                }
                tabs.Add(new TabSnapshot(titles[i], stripped, session));
            }

            bool identified = count > 0 && unmatched.Count == 0;
            bool mergeable = identified
                && tabs.Count == 1
                && tabs[0].Session != null
                && tabs[0].Session.TmuxBacked
                && !string.IsNullOrWhiteSpace(tabs[0].Session.CommandLine);
            return new WindowMatchResult(tabs.ToArray(), identified, unmatched.ToArray(), mergeable);
        }

        /// <summary>Session names to records, ordinal; on duplicate names the first occurrence wins.</summary>
        private static Dictionary<string, SessionRecord> BuildNameIndex(SessionRecord[] sessions)
        {
            Dictionary<string, SessionRecord> index = new Dictionary<string, SessionRecord>(StringComparer.Ordinal);
            if (sessions == null)
            {
                return index;
            }
            foreach (SessionRecord session in sessions)
            {
                if (session == null || session.Name == null || index.ContainsKey(session.Name))
                {
                    continue;
                }
                index.Add(session.Name, session);
            }
            return index;
        }
    }
}
