using System;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    /// <summary>Pure baseline, launch, delta, close-gate and close-observation state machine.</summary>
    public static class MergeSequencer
    {
        public const int DefaultPollIntervalMs = 250;
        public const int DefaultConfirmationBudgetMs = 10000;
        public static MergeStep Start(PlannedMerge merge)
        {
            return Start(merge, DefaultPollIntervalMs, DefaultConfirmationBudgetMs);
        }
        public static MergeStep Start(PlannedMerge merge, int pollIntervalMs, int budgetMs)
        {
            if (merge == null) throw new ArgumentNullException("merge");
            if (pollIntervalMs < 250) throw new ArgumentException("poll interval must be at least 250 ms", "pollIntervalMs");
            if (budgetMs < 1) throw new ArgumentException("budget must be positive", "budgetMs");
            if (!MergeEvidence.ValidPlan(merge))
                return new MergeStep(merge, MergePhase.Aborted, MergeAction.None, pollIntervalMs, budgetMs, 0,
                    MergeOutcome.SafetyCheckFailed(merge.SourceWindowId, "merge plan lacks trusted window/session identity"));
            return new MergeStep(merge, MergePhase.CapturingBaseline, MergeAction.CaptureBaseline, pollIntervalMs, budgetMs, 0, null);
        }

        // @MX:NOTE: only a newly created target tab can advance to the executor's close gate.
        public static MergeStep Next(MergeStep step, MergeObservation observation)
        {
            if (step == null) throw new ArgumentNullException("step");
            if (observation == null) throw new ArgumentNullException("observation");
            if (step.IsTerminal) return step;
            switch (step.Phase)
            {
                case MergePhase.CapturingBaseline:
                    if (observation.Kind != MergeObservationKind.Baseline) return step;
                    TargetTabBaseline baseline = observation.TargetBaseline;
                    if (baseline == null || !step.Merge.TargetIdentity.EqualsForMutation(baseline.Identity)
                        || !TabTitleResult.TrustedResult(baseline.Tabs).Trusted)
                        return Abort(step, MergeOutcome.SafetyCheckFailed(step.Merge.SourceWindowId, "target baseline is untrusted"));
                    return new MergeStep(step.Merge, MergePhase.AwaitingLaunch, MergeAction.Launch,
                        step.PollIntervalMs, step.BudgetMs, step.PollCount, null, baseline);
                case MergePhase.AwaitingLaunch:
                    if (observation.Kind != MergeObservationKind.Launched) return step;
                    return observation.LaunchSuccess ? Advance(step, MergePhase.Polling, MergeAction.Poll, 0)
                        : Abort(step, MergeOutcome.LaunchFailed(step.Merge.SourceWindowId, observation.LaunchError));
                case MergePhase.Polling:
                    if (observation.Kind != MergeObservationKind.Tabs) return step;
                    if (!step.Merge.TargetIdentity.EqualsForMutation(observation.TargetIdentity))
                        return Abort(step, MergeOutcome.SafetyCheckFailed(step.Merge.SourceWindowId, "target identity changed"));
                    if (MergeEvidence.HasNewMatchingTab(step.Baseline, observation.TabResult, step.Merge.Session.Name))
                        return Advance(step, MergePhase.ConfirmingSource, MergeAction.VerifyAndClose, 1);
                    if (observation.BudgetRemainingMs <= 0) return Abort(step, MergeOutcome.ConfirmTimeout(step.Merge.SourceWindowId));
                    return Advance(step, MergePhase.Polling, MergeAction.Poll, 1);
                case MergePhase.ConfirmingSource:
                    if (observation.Kind != MergeObservationKind.CloseGate) return step;
                    return observation.ClosePosted ? Advance(step, MergePhase.AwaitingClose, MergeAction.ObserveClose, 0)
                        : Abort(step, MergeOutcome.SafetyCheckFailed(step.Merge.SourceWindowId, observation.Detail));
                case MergePhase.AwaitingClose:
                    if (observation.Kind != MergeObservationKind.Close) return step;
                    if (!observation.SourceClosed) return Abort(step, MergeOutcome.ConfirmedNotClosed(step.Merge.SourceWindowId));
                    return new MergeStep(step.Merge, MergePhase.Merged, MergeAction.None, step.PollIntervalMs,
                        step.BudgetMs, step.PollCount, MergeOutcome.Merged(step.Merge.SourceWindowId), step.Baseline);
                default: return step;
            }
        }
        private static MergeStep Advance(MergeStep step, MergePhase phase, MergeAction action, int polls)
        {
            return new MergeStep(step.Merge, phase, action, step.PollIntervalMs, step.BudgetMs, step.PollCount + polls, null, step.Baseline);
        }
        private static MergeStep Abort(MergeStep step, MergeOutcome outcome)
        {
            return new MergeStep(step.Merge, MergePhase.Aborted, MergeAction.None, step.PollIntervalMs, step.BudgetMs, step.PollCount, outcome, step.Baseline);
        }
    }
}
