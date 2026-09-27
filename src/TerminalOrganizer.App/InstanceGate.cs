using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The single-instance gate (REQ: one tray process per user/session). Acquired once in
    /// Main; the holder keeps the mutex for the whole Application.Run lifetime and releases
    /// it in Dispose. A second process finds the mutex held and exits silently before any
    /// shell construction (exit code 0, no tray icon, no hotkey, no settings write).
    /// </summary>
    public interface IInstanceGate : IDisposable
    {
        /// <summary>True when this process owns the single-instance mutex.</summary>
        bool Acquired { get; }

        /// <summary>The mutex name this gate attempted (empty when identity could not be read).</summary>
        string Name { get; }
    }

    /// <summary>
    /// Named-mutex gate over <c>Local\TerminalOrganizer.{SID}.Session.{sessionId}</c>.
    /// TryAcquire is strictly NONBLOCKING (WaitOne(0) — never an unbounded wait), and any
    /// failure to read the user SID or the session id is an instance-gate failure: the gate
    /// returns Acquired=false (fail closed), never permission to run unguarded.
    /// </summary>
    // @MX:NOTE: [AUTO] second instance exits rc=0 before EnableVisualStyles/NotifyIcon; no IPC in this unit.
    public sealed class InstanceGate : IInstanceGate
    {
        private readonly Mutex mutex;
        private readonly bool acquired;
        private readonly string name;

        private InstanceGate(Mutex mutex, bool acquired, string name)
        {
            this.mutex = mutex;
            this.acquired = acquired;
            this.name = name;
        }

        /// <summary>
        /// The pinned mutex name: Local\TerminalOrganizer.{sid}.Session.{sessionId}
        /// (Local\ namespace + explicit session id — both per-session by design).
        /// </summary>
        public static string BuildName(string sid, int sessionId)
        {
            return "Local\\TerminalOrganizer." + sid + ".Session." + sessionId;
        }

        /// <summary>
        /// Nonblocking acquire. Failure to obtain the SID or session id, or any mutex
        /// error, yields Acquired=false (fail closed). An abandoned mutex from a crashed
        /// previous instance is still an acquire (ownership transfers with the exception).
        /// </summary>
        public static InstanceGate TryAcquire()
        {
            string sid = null;
            int sessionId = 0;
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity.User != null)
                    {
                        sid = identity.User.Value;
                    }
                }
                sessionId = Process.GetCurrentProcess().SessionId;
            }
            catch (Exception)
            {
                return Failed(string.Empty);
            }
            if (string.IsNullOrEmpty(sid))
            {
                return Failed(string.Empty);
            }
            string mutexName = BuildName(sid, sessionId);
            try
            {
                Mutex candidate = new Mutex(false, mutexName);
                try
                {
                    bool owned;
                    try
                    {
                        owned = candidate.WaitOne(0, false);
                    }
                    catch (AbandonedMutexException)
                    {
                        owned = true; // the previous holder died; ownership transferred
                    }
                    if (owned)
                    {
                        return new InstanceGate(candidate, true, mutexName);
                    }
                    candidate.Dispose();
                    return new InstanceGate(null, false, mutexName);
                }
                catch (Exception)
                {
                    candidate.Dispose();
                    return new InstanceGate(null, false, mutexName);
                }
            }
            catch (Exception)
            {
                // Existing mutex under a different security descriptor or an unexpected
                // creation failure: treat as another instance holding the gate.
                return new InstanceGate(null, false, mutexName);
            }
        }

        private static InstanceGate Failed(string name)
        {
            return new InstanceGate(null, false, name);
        }

        public bool Acquired
        {
            get { return acquired; }
        }

        public string Name
        {
            get { return name; }
        }

        /// <summary>Releases ownership (when held) and the mutex handle. Must run on the acquiring thread.</summary>
        public void Dispose()
        {
            if (mutex != null)
            {
                if (acquired)
                {
                    try
                    {
                        mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Ownership already lost (shutdown edge); nothing further to release.
                    }
                }
                mutex.Dispose();
            }
        }
    }

    /// <summary>
    /// Entry-point composition (A5): acquire the instance gate FIRST; a second instance
    /// returns 0 before any shell construction (EnableVisualStyles, HotkeyWindow,
    /// TrayShell, controller, NotifyIcon). The acquired gate is held via using for the
    /// complete runShell lifetime (Application.Run inside) and released in Dispose.
    /// </summary>
    public static class AppBootstrap
    {
        /// <returns>Process exit code; 0 both for the acquired path (after the shell exits) and the second-instance path.</returns>
        public static int Run(Func<IInstanceGate> gateFactory, Action runShell)
        {
            if (gateFactory == null) throw new ArgumentNullException("gateFactory");
            if (runShell == null) throw new ArgumentNullException("runShell");
            using (IInstanceGate gate = gateFactory())
            {
                if (gate == null || !gate.Acquired)
                {
                    return 0;
                }
                runShell();
                return 0;
            }
        }
    }
}
