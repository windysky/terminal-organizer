using System;
using System.Collections.Generic;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// Classifies a child process's image name and command line into a session record
    /// (REQ-CLI-001). Three shapes match: wsl.exe invoking run_dev_launch.sh with
    /// --attach &lt;session&gt; (Local), wsl.exe invoking rdl_attach.sh with
    /// &lt;ssh-target&gt; &lt;session&gt; (Remote), and powershell.exe whose command line contains
    /// LAUNCHER_SESSION='&lt;session&gt;' or LAUNCHER_SESSION="&lt;session&gt;" (WindowsNative);
    /// anything else yields null. Matching is on token substrings, tolerant of extra
    /// arguments before and after the marker tokens, and never throws for arbitrary input
    /// (REQ-CLI-003). The record carries the full original command line verbatim
    /// (REQ-CLI-002) — copied, never reconstructed.
    /// </summary>
    public static class CommandLineClassifier
    {
        private const string LocalScriptMarker = "run_dev_launch.sh";
        private const string AttachMarker = "--attach";
        private const string RemoteScriptMarker = "rdl_attach.sh";
        private const string NativeVariable = "LAUNCHER_SESSION";
        private const string WslImage = "wsl.exe";
        private const string PowerShellImage = "powershell.exe";

        private static readonly char[] TokenSeparators = new char[] { ' ', '\t' };

        /// <summary>Classifies one process; returns null when the line matches no shape.</summary>
        // @MX:NOTE: shapes pinned from PROD recorded command lines (plan.md J.1).
        public static SessionRecord Classify(string imageName, string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine))
            {
                return null;
            }
            if (string.Equals(imageName, WslImage, StringComparison.OrdinalIgnoreCase))
            {
                SessionRecord local = ClassifyLocal(commandLine);
                if (local != null)
                {
                    return local;
                }
                return ClassifyRemote(commandLine);
            }
            if (string.Equals(imageName, PowerShellImage, StringComparison.OrdinalIgnoreCase))
            {
                return ClassifyNative(commandLine);
            }
            return null;
        }

        /// <summary>
        /// Classifies a set of child processes into the open-session set (REQ-CLI-002):
        /// records appearing more than once for the same session name keep the FIRST
        /// occurrence, so a re-attached session does not double-count; the merge uses
        /// the first command line. A null input yields an empty array.
        /// </summary>
        public static SessionRecord[] ClassifyAll(ChildProcess[] processes)
        {
            List<SessionRecord> records = new List<SessionRecord>();
            if (processes == null)
            {
                return records.ToArray();
            }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ChildProcess process in processes)
            {
                if (process == null)
                {
                    continue;
                }
                SessionRecord record = Classify(process.ImageName, process.CommandLine);
                if (record == null || !seen.Add(record.Name))
                {
                    continue;
                }
                records.Add(record);
            }
            return records.ToArray();
        }

        /// <summary>
        /// Local shape: a token containing run_dev_launch.sh, later a token containing
        /// --attach, and the session is the token immediately after the --attach token.
        /// Tokens after the session are tolerated; intervening tokens are never skipped.
        /// </summary>
        private static SessionRecord ClassifyLocal(string commandLine)
        {
            string[] tokens = Tokenize(commandLine);
            int scriptIndex = FindTokenContaining(tokens, LocalScriptMarker);
            if (scriptIndex < 0)
            {
                return null;
            }
            int attachIndex = FindTokenContainingFrom(tokens, AttachMarker, scriptIndex + 1);
            if (attachIndex < 0 || attachIndex + 1 >= tokens.Length)
            {
                return null;
            }
            return new SessionRecord(tokens[attachIndex + 1], SessionKind.Local, commandLine);
        }

        /// <summary>Remote shape: a token containing rdl_attach.sh, then the ssh-target
        /// token, then the session token (second token after the marker).</summary>
        private static SessionRecord ClassifyRemote(string commandLine)
        {
            string[] tokens = Tokenize(commandLine);
            int scriptIndex = FindTokenContaining(tokens, RemoteScriptMarker);
            if (scriptIndex < 0 || scriptIndex + 2 >= tokens.Length)
            {
                return null;
            }
            return new SessionRecord(tokens[scriptIndex + 2], SessionKind.Remote, commandLine);
        }

        /// <summary>Windows-native shape: LAUNCHER_SESSION='...' or LAUNCHER_SESSION="..."
        /// captured between the quotes; an unterminated quote does not match.</summary>
        private static SessionRecord ClassifyNative(string commandLine)
        {
            string name = ExtractQuoted(commandLine, "'");
            if (name == null)
            {
                name = ExtractQuoted(commandLine, "\"");
            }
            if (name == null)
            {
                return null;
            }
            return new SessionRecord(name, SessionKind.WindowsNative, commandLine);
        }

        private static string ExtractQuoted(string commandLine, string quote)
        {
            string marker = NativeVariable + "=" + quote;
            int start = commandLine.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }
            int valueStart = start + marker.Length;
            int valueEnd = commandLine.IndexOf(quote, valueStart, StringComparison.Ordinal);
            if (valueEnd <= valueStart)
            {
                return null;
            }
            return commandLine.Substring(valueStart, valueEnd - valueStart);
        }

        private static string[] Tokenize(string commandLine)
        {
            return commandLine.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        }

        private static int FindTokenContaining(string[] tokens, string value)
        {
            return FindTokenContainingFrom(tokens, value, 0);
        }

        private static int FindTokenContainingFrom(string[] tokens, string value, int from)
        {
            for (int i = from; i < tokens.Length; i++)
            {
                if (tokens[i] != null && tokens[i].IndexOf(value, StringComparison.Ordinal) >= 0)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
