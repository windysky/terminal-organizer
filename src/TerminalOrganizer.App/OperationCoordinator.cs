using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TerminalOrganizer.Core.Monitors;

namespace TerminalOrganizer.App
{
    /// <summary>The coordinator's lifecycle phase. A5 drives Idle/Acquiring/Completing; AwaitingChoice and Mutating arrive with B4/C3.</summary>
    public enum OperationPhase
    {
        /// <summary>No organize operation is running or queued.</summary>
        Idle,

        /// <summary>A request started; the worker is acquiring (discovery/layout state).</summary>
        Acquiring,

        /// <summary>Waiting for a user choice (B4/C3 surface; reserved).</summary>
        AwaitingChoice,

        /// <summary>Guarded native mutation in progress; never interrupted mid-unit (reserved).</summary>
        Mutating,

        /// <summary>The work returned; the completion is being published on the UI thread.</summary>
        Completing
    }

    /// <summary>
    /// One serialized organize intent: the stable monitor key to organize (null when the
    /// monitor should be resolved under the cursor), whether the monitor is resolved when
    /// the run BEGINS (a pending under-cursor request re-reads the cursor at rerun time,
    /// not at queue time), and the request source ("menu"/"hotkey") for the log.
    /// Immutable.
    /// </summary>
    public sealed class OrganizeRequest
    {
        private readonly MonitorKey monitorKey;
        private readonly bool resolveMonitorUnderCursor;
        private readonly string source;

        public OrganizeRequest(MonitorKey monitorKey, bool resolveMonitorUnderCursor, string source)
        {
            this.monitorKey = monitorKey;
            this.resolveMonitorUnderCursor = resolveMonitorUnderCursor;
            this.source = source;
        }

        /// <summary>The stable monitor key; null when <see cref="ResolveMonitorUnderCursor"/> is true.</summary>
        public MonitorKey MonitorKey
        {
            get { return monitorKey; }
        }

        public bool ResolveMonitorUnderCursor
        {
            get { return resolveMonitorUnderCursor; }
        }

        public string Source
        {
            get { return source; }
        }
    }

    /// <summary>One completed work unit: the pipeline result (null when never run), whether it stopped on cancellation, and any captured error. Immutable.</summary>
    public sealed class OperationCompletion
    {
        private readonly OrganizeRunResult result;
        private readonly bool cancelled;
        private readonly Exception error;

        public OperationCompletion(OrganizeRunResult result, bool cancelled, Exception error)
        {
            this.result = result;
            this.cancelled = cancelled;
            this.error = error;
        }

        /// <summary>The organize pipeline result; null when the work never ran (no target, cancelled, or error).</summary>
        public OrganizeRunResult Result
        {
            get { return result; }
        }

        public bool Cancelled
        {
            get { return cancelled; }
        }

        public Exception Error
        {
            get { return error; }
        }
    }

    /// <summary>The serialized work unit: resolve and run one organize request, honouring cancellation before the first mutation.</summary>
    public delegate OperationCompletion OperationWork(OrganizeRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Serializes organize operations in-process (A5): at most ONE work unit runs at a
    /// time on a TaskScheduler.Default worker; requests arriving while busy coalesce into
    /// a single pending rerun slot (last request wins — never a second concurrent task);
    /// completions are published on the UI thread through uiControl.BeginInvoke (WinForms
    /// controls are never touched from the worker). RequestExit cancels the token, clears
    /// the pending rerun, exits immediately when idle and otherwise defers the exit until
    /// the in-flight unit has finished and its completion marshalled back — an indivisible
    /// native mutation is never interrupted midway (the work's own cancellation
    /// checkpoints stop it before the NEXT guarded mutation).
    /// </summary>
    // @MX:WARN: [AUTO] worker thread + lock + BeginInvoke marshalling — concurrency surface; Pester drives it through a real hidden form.
    public sealed class OperationCoordinator : IDisposable
    {
        private readonly object sync = new object();
        private readonly Control uiControl;
        private readonly OperationWork work;
        private readonly Action<OperationPhase> phaseChanged;
        private readonly Action<OperationCompletion> completed;

        private OperationPhase phase;
        private bool busy;
        private bool hasPending;
        private OrganizeRequest pendingRequest;
        private CancellationTokenSource cancellation;
        private bool exitRequested;
        private Action deferredExit;
        private bool disposed;

        /// <param name="uiControl">Marshalling control; its handle must be created (BeginInvoke target).</param>
        /// <param name="phaseChanged">Optional phase notifications (invoked on the UI thread only).</param>
        /// <param name="completed">Optional per-run completion publisher (invoked on the UI thread only).</param>
        public OperationCoordinator(Control uiControl, OperationWork work,
            Action<OperationPhase> phaseChanged, Action<OperationCompletion> completed)
        {
            if (uiControl == null) throw new ArgumentNullException("uiControl");
            if (work == null) throw new ArgumentNullException("work");
            this.uiControl = uiControl;
            this.work = work;
            this.phaseChanged = phaseChanged;
            this.completed = completed;
            this.phase = OperationPhase.Idle;
        }

        public bool IsBusy
        {
            get { lock (sync) { return busy; } }
        }

        /// <summary>True when a coalesced request is waiting for the current unit to finish.</summary>
        public bool HasPendingRerun
        {
            get { lock (sync) { return hasPending; } }
        }

        public OperationPhase Phase
        {
            get { lock (sync) { return phase; } }
        }

        /// <summary>
        /// First request starts exactly one worker; a request while busy only replaces the
        /// pending slot (last request wins, at most one coalesced rerun). A request
        /// arriving after an exit request is dropped outright (R2-1): during a deferred
        /// exit it must not park as a pending rerun that could race ExitApplication.
        /// </summary>
        public void Request(OrganizeRequest request)
        {
            if (request == null)
            {
                return;
            }
            bool started = false;
            lock (sync)
            {
                if (disposed || exitRequested)
                {
                    return;
                }
                if (busy)
                {
                    pendingRequest = request;
                    hasPending = true;
                    return;
                }
                busy = true;
                hasPending = false;
                pendingRequest = null;
                cancellation = new CancellationTokenSource();
                phase = OperationPhase.Acquiring;
                StartWorkLocked(request, cancellation.Token);
                started = true;
            }
            if (started)
            {
                NotifyPhase(OperationPhase.Acquiring);
            }
        }

        /// <summary>
        /// Exit request: cancels the token and clears the pending rerun immediately; the
        /// exit action runs at once when idle, otherwise only after the in-flight unit's
        /// completion has marshalled back (never mid-mutation).
        /// </summary>
        public void RequestExit(Action exitWhenSafe)
        {
            if (exitWhenSafe == null)
            {
                return;
            }
            Action runNow = null;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                exitRequested = true;
                hasPending = false;
                pendingRequest = null;
                if (cancellation != null)
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The in-flight unit already consumed and released the source.
                    }
                }
                if (!busy)
                {
                    exitRequested = false;
                    runNow = exitWhenSafe;
                }
                else
                {
                    deferredExit = exitWhenSafe;
                }
            }
            if (runNow != null)
            {
                runNow();
            }
        }

        /// <summary>
        /// Publishes one completion on the UI thread: marshal outcome, complete, then
        /// either run the deferred exit, start exactly one rerun, or return to Idle.
        /// </summary>
        private void HandleCompletion(OperationCompletion outcome)
        {
            Action exitNow = null;
            bool notifyCompleting = false;
            bool notifyRerun = false;
            bool notifyIdle = false;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                busy = false;
                if (cancellation != null)
                {
                    try
                    {
                        cancellation.Dispose();
                    }
                    catch (ObjectDisposedException) { }
                    cancellation = null;
                }
                phase = OperationPhase.Completing;
                notifyCompleting = true;
                if (exitRequested)
                {
                    // R2-1: exitRequested stays SET here — the app is leaving, so the
                    // completion publisher (which runs after this lock section) must
                    // not be able to arm a fresh rerun through Request either.
                    hasPending = false;
                    pendingRequest = null;
                    phase = OperationPhase.Idle;
                    exitNow = deferredExit;
                    deferredExit = null;
                }
                else if (hasPending)
                {
                    OrganizeRequest next = pendingRequest;
                    pendingRequest = null;
                    hasPending = false;
                    busy = true;
                    cancellation = new CancellationTokenSource();
                    StartWorkLocked(next, cancellation.Token);
                    phase = OperationPhase.Acquiring;
                    notifyRerun = true;
                }
                else
                {
                    phase = OperationPhase.Idle;
                    notifyIdle = true;
                }
            }
            if (notifyCompleting)
            {
                NotifyPhase(OperationPhase.Completing);
            }
            PublishCompleted(outcome);
            if (exitNow != null)
            {
                exitNow();
            }
            if (notifyRerun)
            {
                NotifyPhase(OperationPhase.Acquiring);
            }
            else if (notifyIdle)
            {
                NotifyPhase(OperationPhase.Idle);
            }
        }

        private void StartWorkLocked(OrganizeRequest request, CancellationToken token)
        {
            Task.Factory.StartNew(delegate { RunWork(request, token); },
                CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
        }

        private void RunWork(OrganizeRequest request, CancellationToken token)
        {
            OperationCompletion outcome;
            try
            {
                outcome = work(request, token);
                if (outcome == null)
                {
                    outcome = new OperationCompletion(null, false, null);
                }
            }
            catch (Exception ex)
            {
                outcome = new OperationCompletion(null, false, ex);
            }
            try
            {
                uiControl.BeginInvoke((Action)delegate { HandleCompletion(outcome); }, new object[0]);
            }
            catch (InvalidOperationException)
            {
                // ObjectDisposedException derives from this type: the marshalling control
                // died mid-shutdown, the UI is gone, so the completion (and any deferred
                // exit, already handled by the shell) drops.
            }
        }

        private void NotifyPhase(OperationPhase value)
        {
            Action<OperationPhase> handler = phaseChanged;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(value);
            }
            catch (Exception)
            {
                // Phase notifications are advisory; a handler failure never breaks a run.
            }
        }

        private void PublishCompleted(OperationCompletion outcome)
        {
            Action<OperationCompletion> handler = completed;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(outcome);
            }
            catch (Exception)
            {
                // Completion publishing is advisory for the log/menu; never break the loop.
            }
        }

        /// <summary>
        /// Stops accepting requests, clears the pending rerun and cancels the in-flight
        /// token (the work stops before its NEXT guarded mutation). UI objects are NOT
        /// disposed here — the shell owns them and tears them down after the completion
        /// has marshalled back.
        /// </summary>
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                hasPending = false;
                pendingRequest = null;
                deferredExit = null;
                exitRequested = false;
                if (cancellation != null)
                {
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                    // The source is deliberately NOT disposed here: an in-flight worker
                    // may still be observing its token.
                }
            }
        }
    }
}
