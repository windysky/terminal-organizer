using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>
    /// The helper-process surface the UIA probe client drives (B4): one started
    /// helper instance. The client only ever touches THIS handle — a Windows
    /// Terminal process is never killed (the helper has no mutation capability;
    /// it only reads).
    /// </summary>
    public interface IHelperProcess : IDisposable
    {
        /// <summary>Blocks up to the given milliseconds waiting for exit (the poll quantum).</summary>
        bool WaitForExit(int milliseconds);

        /// <summary>Terminates the OWNED helper process only.</summary>
        void Kill();

        /// <summary>The helper's stdout after it has exited.</summary>
        string ReadStandardOutput();

        /// <summary>The helper's stderr after it has exited (diagnostics).</summary>
        string ReadStandardError();
    }

    /// <summary>Injectable seam over the helper launch (B4): receives the full argument string.</summary>
    public delegate IHelperProcess HelperProcessStart(string arguments);

    /// <summary>
    /// The B4 hard UIA timeout: instead of abandoning a timed-out in-process COM
    /// call (which would continue and leak workers), the tab read runs in a small
    /// STA console helper (TerminalOrganizer.UiaProbe.exe) whose entire lifetime
    /// this client owns. Protocol: the helper is started hidden with redirected
    /// stdout/stderr as <c>--hwnd &lt;signed-decimal-int64&gt;</c> and answers with ONE
    /// UTF-8 line:
    /// <c>OK|count|base64(identity):base64(title);...</c>,
    /// <c>INCOMPLETE|count|payload|base64(error)</c> or
    /// <c>ERROR|0||base64(error)</c>.
    /// The client polls WaitForExit(50) while checking cancellation and elapsed
    /// time; on timeout/cancellation it kills ONLY the owned helper and returns a
    /// TimedOut result carrying the GetWindowText fallback title. Malformed output
    /// fails closed to a Fallback result. Never throws; a Windows Terminal process
    /// is never killed.
    /// </summary>
    // @MX:WARN: spawns the real helper in production (morning checklist); the suite drives the factory seam.
    public sealed class UiaProbeClient
    {
        /// <summary>The B4 hard bound: 1.5s per window probe.</summary>
        public const int DefaultTimeoutMilliseconds = 1500;

        private const int PollMilliseconds = 50;
        private const int PostKillWaitMilliseconds = 200;

        private readonly string helperPath;
        private readonly int timeoutMilliseconds;
        private readonly HelperProcessStart startProcess;

        /// <summary>Production constructor: the default 1.5-second bound.</summary>
        public UiaProbeClient(string helperPath)
            : this(helperPath, DefaultTimeoutMilliseconds)
        {
        }

        /// <summary>B4 timeout constructor; the bound is clamped to at least one millisecond.</summary>
        public UiaProbeClient(string helperPath, int timeoutMilliseconds)
            : this(helperPath, timeoutMilliseconds, null)
        {
        }

        /// <summary>Test seam (plan.md B.4 pattern): an injectable helper-launch port.</summary>
        public UiaProbeClient(string helperPath, int timeoutMilliseconds, HelperProcessStart startProcess)
        {
            if (helperPath == null)
            {
                throw new ArgumentNullException("helperPath");
            }
            this.helperPath = helperPath;
            this.timeoutMilliseconds = timeoutMilliseconds < 1 ? 1 : timeoutMilliseconds;
            this.startProcess = startProcess;
        }

        /// <summary>Runs one bounded tab read for the window; never throws (B4).</summary>
        public TabTitleResult ReadTabs(IntPtr window, CancellationToken cancellationToken)
        {
            string fallbackTitle = UiaTabTitleReader.GetWindowTextTitle(window);
            try
            {
                HelperProcessStart start = startProcess;
                IHelperProcess helper = start != null
                    ? start(BuildArguments(window))
                    : StartRealHelper(BuildArguments(window));
                if (helper == null)
                {
                    return TabTitleResult.Failure(fallbackTitle, "helper process failed to start");
                }
                try
                {
                    long started = Environment.TickCount;
                    while (!helper.WaitForExit(PollMilliseconds))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            KillOwnedHelper(helper);
                            return TabTitleResult.TimedOut(fallbackTitle, "cancelled while waiting for the UIA helper");
                        }
                        if (Environment.TickCount - started >= timeoutMilliseconds)
                        {
                            KillOwnedHelper(helper);
                            return TabTitleResult.TimedOut(fallbackTitle,
                                "UIA helper timed out after " + timeoutMilliseconds + "ms");
                        }
                        // Pace the poll when the wait returns instantly (fake seams);
                        // the real wait already blocked for the full quantum.
                        Thread.Sleep(1);
                    }
                    return ParseOutput(helper.ReadStandardOutput(), fallbackTitle);
                }
                finally
                {
                    helper.Dispose();
                }
            }
            catch (Exception ex)
            {
                return TabTitleResult.Failure(fallbackTitle, ex.Message);
            }
        }

        /// <summary>
        /// Parses the helper's one protocol line into a TabTitleResult; anything
        /// malformed fails closed to a Fallback result carrying the given title.
        /// </summary>
        public static TabTitleResult ParseOutput(string output, string fallbackTitle)
        {
            try
            {
                if (string.IsNullOrEmpty(output))
                {
                    return TabTitleResult.Failure(fallbackTitle, "empty helper output");
                }
                string line = output.Split('\n')[0].TrimEnd('\r');
                string[] parts = line.Split('|');
                if (parts.Length < 1)
                {
                    return Malformed(fallbackTitle);
                }
                if (string.Equals(parts[0], "OK", StringComparison.Ordinal))
                {
                    if (parts.Length != 3)
                    {
                        return Malformed(fallbackTitle);
                    }
                    TabEvidence[] evidence = ParsePairs(parts[2]);
                    int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    if (evidence.Length != count)
                    {
                        return Malformed(fallbackTitle);
                    }
                    return TabTitleResult.TrustedResult(evidence);
                }
                if (string.Equals(parts[0], "INCOMPLETE", StringComparison.Ordinal))
                {
                    if (parts.Length != 4)
                    {
                        return Malformed(fallbackTitle);
                    }
                    TabEvidence[] evidence = ParsePairs(parts[2]);
                    int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    return TabTitleResult.Incomplete(evidence, count, fallbackTitle, Decode(parts[3]));
                }
                if (string.Equals(parts[0], "ERROR", StringComparison.Ordinal))
                {
                    if (parts.Length != 4)
                    {
                        return Malformed(fallbackTitle);
                    }
                    return TabTitleResult.Failure(fallbackTitle, Decode(parts[3]));
                }
                return Malformed(fallbackTitle);
            }
            catch (Exception)
            {
                return Malformed(fallbackTitle);
            }
        }

        private static TabTitleResult Malformed(string fallbackTitle)
        {
            return TabTitleResult.Failure(fallbackTitle, "malformed helper output");
        }

        private static TabEvidence[] ParsePairs(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return new TabEvidence[0];
            }
            string[] pairs = payload.Split(';');
            TabEvidence[] evidence = new TabEvidence[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                int separator = pairs[i].IndexOf(':');
                if (separator < 0)
                {
                    throw new FormatException("tab pair is missing the identity separator");
                }
                string identity = Decode(pairs[i].Substring(0, separator));
                string title = Decode(pairs[i].Substring(separator + 1));
                evidence[i] = new TabEvidence(string.IsNullOrEmpty(identity) ? null : identity, title);
            }
            return evidence;
        }

        private static string Decode(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return string.Empty;
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        private static string BuildArguments(IntPtr window)
        {
            return "--hwnd " + window.ToInt64().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Kills only the OWNED helper and waits briefly for its exit; never throws.</summary>
        private static void KillOwnedHelper(IHelperProcess helper)
        {
            try
            {
                helper.Kill();
                helper.WaitForExit(PostKillWaitMilliseconds);
            }
            catch (Exception)
            {
                // The helper is dying either way; the read degrades to TimedOut.
            }
        }

        private IHelperProcess StartRealHelper(string arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo(helperPath, arguments);
            start.CreateNoWindow = true;
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            return new RealHelperProcess(Process.Start(start));
        }

        /// <summary>The production IHelperProcess over System.Diagnostics.Process (owned lifetime).</summary>
        private sealed class RealHelperProcess : IHelperProcess
        {
            private readonly Process process;

            public RealHelperProcess(Process process)
            {
                this.process = process;
            }

            public bool WaitForExit(int milliseconds)
            {
                return process != null && process.WaitForExit(milliseconds);
            }

            public void Kill()
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }

            public string ReadStandardOutput()
            {
                return process == null ? null : process.StandardOutput.ReadToEnd();
            }

            public string ReadStandardError()
            {
                return process == null ? null : process.StandardError.ReadToEnd();
            }

            public void Dispose()
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }
        }
    }
}
