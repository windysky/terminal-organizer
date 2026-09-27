using System;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>
    /// Builds the wt new-tab launch arguments (REQ-MRG-003): "new-tab " followed by the
    /// source's FULL command line INCLUDING its image token (for example
    /// "wsl.exe -d Ubuntu --exec ..."), copied verbatim from the session record. Exactly ONE
    /// transformation is applied: every ";" is escaped as "\;" because wt uses ";" to separate
    /// its own subcommands — stripping the image token instead would let wt consume "-d" as
    /// its own --startingDirectory option and the child would never see the source's argument
    /// list (plan.md B.2, the launcher duplicate-detection contract). No re-quoting,
    /// reordering, or whitespace change. Pure and deterministic: the same input yields the
    /// same string on every call.
    /// </summary>
    public static class MergeCommandBuilder
    {
        // @MX:NOTE: full command line (image token kept) + \; escape rule (REQ-MRG-003); the launcher's duplicate detection reads this line byte-for-byte.
        public static string Build(string commandLine)
        {
            if (commandLine == null)
            {
                throw new ArgumentNullException("commandLine");
            }
            return "new-tab " + commandLine.Replace(";", "\\;");
        }
    }
}
