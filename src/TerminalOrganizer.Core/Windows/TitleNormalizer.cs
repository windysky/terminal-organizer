using System;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// Strips the launcher's window-name prefix from a tab title (REQ-TTL-001): exactly one
    /// leading prefix of the form [OHNW][CGD]_ or [OHNW][CGD]&lt;digit&gt;_ — case-sensitive,
    /// uppercase classes, a SINGLE rank digit (C1; multi-digit needs a future grammar change).
    /// A title without a valid prefix returns unchanged; a second prefix survives (a
    /// window_name may itself contain '_'). An empty session title after a prefix is invalid
    /// and is not stripped. Never throws; null passes through as null.
    /// </summary>
    public static class TitleNormalizer
    {
        // @MX:NOTE: C1 grammar entry — the matcher path (StripPrefix) and the priority model
        // (DeclaredRank) both consume this parse; the rank digit is never a family/context letter.
        public static NormalizedTitle Parse(string title)
        {
            if (title == null || title.Length < 3)
            {
                return Unchanged(title);
            }
            if (title[0] != 'O' && title[0] != 'H' && title[0] != 'N' && title[0] != 'W')
            {
                return Unchanged(title);
            }
            if (title[1] != 'C' && title[1] != 'G' && title[1] != 'D')
            {
                return Unchanged(title);
            }
            if (title[2] == '_')
            {
                return Stripped(title, 3, null);
            }
            if (title[2] >= '0' && title[2] <= '9')
            {
                // Rank form: the digit must be followed by '_' and a non-empty session title.
                if (title.Length < 4 || title[3] != '_')
                {
                    return Unchanged(title);
                }
                return Stripped(title, 4, title[2] - '0');
            }
            return Unchanged(title);
        }

        // @MX:NOTE: one-prefix rule, product.md decision 1 (OC_YODA1 -> YODA1; NG_AB_X -> AB_X, not X);
        // delegates to Parse so matcher code and the grammar can never disagree.
        public static string StripPrefix(string title)
        {
            NormalizedTitle parsed = Parse(title);
            return parsed.SessionTitle;
        }

        private static NormalizedTitle Stripped(string title, int sessionStart, int? declaredRank)
        {
            if (title.Length == sessionStart)
            {
                // Empty session title after a prefix is invalid and is not stripped.
                return Unchanged(title);
            }
            return new NormalizedTitle(title, title.Substring(sessionStart), true, declaredRank);
        }

        private static NormalizedTitle Unchanged(string title)
        {
            return new NormalizedTitle(title, title, false, null);
        }
    }
}
