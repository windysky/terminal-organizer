using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>Injectable seam over the searcher execution (B4): lets the suite
    /// simulate a WMI timeout without a live WMI round-trip.</summary>
    public delegate ManagementObjectCollection SearcherRun(ManagementObjectSearcher searcher);

    /// <summary>
    /// The bounded WMI query outcome (B4): the children plus whether the query
    /// degraded (timeout or failure — children are then empty and every window
    /// reads unidentified, the safe default). Immutable.
    /// </summary>
    public sealed class ProcessSessionQueryResult
    {
        private readonly ChildProcess[] children;
        private readonly bool timedOut;
        private readonly string error;

        public ProcessSessionQueryResult(ChildProcess[] children, bool timedOut, string error)
        {
            this.children = children == null ? new ChildProcess[0] : (ChildProcess[])children.Clone();
            this.timedOut = timedOut;
            this.error = error;
        }

        public ChildProcess[] Children { get { return (ChildProcess[])children.Clone(); } }

        /// <summary>True when the query did not complete successfully (timeout/failure): a degraded, empty acquisition.</summary>
        public bool TimedOut { get { return timedOut; } }

        /// <summary>The degradation detail; null on a completed query.</summary>
        public string Error { get { return error; } }
    }

    /// <summary>
    /// Queries the child processes of the given WindowsTerminal.exe pids via WMI
    /// Win32_Process (REQ-ACQ-003, System.Management) under a HARD two-second
    /// timeout (B4): the EnumerationOptions construct pins Timeout, blocks the
    /// semi-synchronous fast-return path (ReturnImmediately=false) and the
    /// rewindable enumerator (Rewindable=false). One sweep over the pid set; for
    /// each wsl.exe/powershell.exe child it returns the image name and the full
    /// command line (which may be null when the system withholds it — the
    /// classifier degrades that to no session). A timeout or query failure returns
    /// an empty result flagged TimedOut (acquisition degradation — nothing is
    /// merged); nothing throws. Cancellation is checked before the sweep and
    /// between result objects; a cancelled query returns an empty result.
    /// </summary>
    // @MX:WARN: WMI live query, morning checklist (tools/dump-windows.ps1); not an acceptance claim.
    public sealed class ProcessSessionQuery
    {
        /// <summary>The B4 hard bound: two seconds per sweep.</summary>
        public const int DefaultTimeoutMilliseconds = 2000;

        private const string QueryPrefix = "SELECT Name, CommandLine, ParentProcessId FROM Win32_Process WHERE ";
        private const string ParentClauseFormat = "ParentProcessId = {0}";

        private readonly int timeoutMilliseconds;
        private readonly SearcherRun runSearcher;

        /// <summary>Production constructor: the default two-second bound.</summary>
        public ProcessSessionQuery()
            : this(DefaultTimeoutMilliseconds)
        {
        }

        /// <summary>B4 timeout constructor; the bound is clamped to at least one millisecond.</summary>
        public ProcessSessionQuery(int timeoutMilliseconds)
            : this(timeoutMilliseconds, null)
        {
        }

        /// <summary>Test seam (plan.md B.4 pattern): an injectable searcher-execution port.</summary>
        public ProcessSessionQuery(int timeoutMilliseconds, SearcherRun runSearcher)
        {
            this.timeoutMilliseconds = timeoutMilliseconds < 1 ? 1 : timeoutMilliseconds;
            this.runSearcher = runSearcher;
        }

        /// <summary>The pinned EnumerationOptions construct (B4): the timeout, no
        /// fast-return buffering, no rewindable enumerator.</summary>
        public static EnumerationOptions BuildOptions(int timeoutMilliseconds)
        {
            EnumerationOptions options = new EnumerationOptions();
            options.Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
            options.ReturnImmediately = false;
            options.Rewindable = false;
            return options;
        }

        /// <summary>Returns the wsl.exe/powershell.exe children of the given pids; empty on degradation.</summary>
        public ChildProcess[] QueryChildren(int[] parentPids)
        {
            return QueryChildren(parentPids, CancellationToken.None).Children;
        }

        /// <summary>The B4 result surface: children plus the degradation flag (timeout/failure).</summary>
        public ProcessSessionQueryResult QueryChildren(int[] parentPids, CancellationToken cancellationToken)
        {
            List<ChildProcess> children = new List<ChildProcess>();
            if (parentPids == null || parentPids.Length == 0)
            {
                return new ProcessSessionQueryResult(children.ToArray(), false, null);
            }
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    new ManagementScope(@"\\.\root\cimv2"),
                    new ObjectQuery(BuildQuery(parentPids)),
                    BuildOptions(timeoutMilliseconds)))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return Cancelled();
                    }
                    using (ManagementObjectCollection results = runSearcher != null
                        ? runSearcher(searcher) : searcher.Get())
                    {
                        if (results != null)
                        {
                            foreach (ManagementObject item in results)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    return Cancelled();
                                }
                                using (item)
                                {
                                    CollectItem(item, children);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Timeout or query failure degrades to the empty set with the flag
                // set (never an exception): every window then reads unidentified
                // and nothing is merged.
                return new ProcessSessionQueryResult(new ChildProcess[0], true, ex.Message);
            }
            return new ProcessSessionQueryResult(children.ToArray(), false, null);
        }

        private static ProcessSessionQueryResult Cancelled()
        {
            return new ProcessSessionQueryResult(new ChildProcess[0], false, "operation cancelled");
        }

        private static string BuildQuery(int[] parentPids)
        {
            List<string> clauses = new List<string>();
            foreach (int pid in parentPids)
            {
                clauses.Add(string.Format(ParentClauseFormat, pid));
            }
            return QueryPrefix + string.Join(" OR ", clauses.ToArray());
        }

        private static void CollectItem(ManagementObject item, List<ChildProcess> children)
        {
            string name = item["Name"] as string;
            if (!IsSessionImage(name))
            {
                return;
            }
            string commandLine = item["CommandLine"] as string;
            children.Add(new ChildProcess(name, commandLine));
        }

        private static bool IsSessionImage(string imageName)
        {
            return string.Equals(imageName, "wsl.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(imageName, "powershell.exe", StringComparison.OrdinalIgnoreCase);
        }
    }
}
