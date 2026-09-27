using System;
using System.Collections.Generic;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.Core.Overflow
{
    public sealed class TargetTabBaseline
    {
        private readonly TabEvidence[] tabs;
        public TargetTabBaseline(WindowIdentity identity, TabEvidence[] tabs)
        {
            Identity = identity;
            this.tabs = tabs == null ? new TabEvidence[0] : (TabEvidence[])tabs.Clone();
        }
        public WindowIdentity Identity { get; private set; }
        public TabEvidence[] Tabs { get { return (TabEvidence[])tabs.Clone(); } }
    }

    public sealed class SourceCloseEvidence
    {
        public SourceCloseEvidence(bool sourceIdentityMatches, bool targetIdentityMatches,
            bool sourceTabsTrusted, bool targetTabsTrusted, bool sourceSessionMatches,
            bool targetDeltaStillPresent, int sourceTabCount, string detail)
        {
            SourceIdentityMatches = sourceIdentityMatches; TargetIdentityMatches = targetIdentityMatches;
            SourceTabsTrusted = sourceTabsTrusted; TargetTabsTrusted = targetTabsTrusted;
            SourceSessionMatches = sourceSessionMatches; TargetDeltaStillPresent = targetDeltaStillPresent;
            SourceTabCount = sourceTabCount; Detail = detail;
        }
        public bool SourceIdentityMatches { get; private set; }
        public bool TargetIdentityMatches { get; private set; }
        public bool SourceTabsTrusted { get; private set; }
        public bool TargetTabsTrusted { get; private set; }
        public bool SourceSessionMatches { get; private set; }
        public bool TargetDeltaStillPresent { get; private set; }
        public int SourceTabCount { get; private set; }
        public string Detail { get; private set; }
        public bool MayClose
        {
            get { return SourceIdentityMatches && TargetIdentityMatches && SourceTabsTrusted && TargetTabsTrusted
                && SourceSessionMatches && TargetDeltaStillPresent && SourceTabCount == 1; }
        }
    }

    public static class MergeEvidence
    {
        public static bool ValidPlan(PlannedMerge merge)
        {
            return merge != null && merge.SourceIdentity != null && merge.TargetIdentity != null
                && merge.SourceIdentity.EqualsForMutation(merge.SourceIdentity)
                && merge.TargetIdentity.EqualsForMutation(merge.TargetIdentity)
                && merge.SourceHandle != merge.TargetHandle && merge.Session != null && merge.Session.TmuxBacked
                && !string.IsNullOrWhiteSpace(merge.CommandLine) && !string.IsNullOrEmpty(merge.Session.Name)
                && string.Equals(TitleNormalizer.StripPrefix(merge.SourceTabTitle), merge.Session.Name, StringComparison.Ordinal);
        }

        // @MX:NOTE: a matching title present before launch can never authorize a close.
        public static bool HasNewMatchingTab(TargetTabBaseline baseline, TabTitleResult post, string sessionName)
        {
            if (baseline == null || post == null || !post.Trusted || string.IsNullOrEmpty(sessionName)) return false;
            TabEvidence[] before = baseline.Tabs;
            if (!TabTitleResult.TrustedResult(before).Trusted || post.ObservedTabItemCount <= before.Length) return false;
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            bool allKeys = true;
            int beforeMatches = 0, afterMatches = 0;
            foreach (TabEvidence item in before)
            {
                if (string.IsNullOrEmpty(item.IdentityKey)) allKeys = false;
                else keys.Add(item.IdentityKey);
                if (Matches(item.Title, sessionName)) beforeMatches++;
            }
            foreach (TabEvidence item in post.Evidence)
            {
                if (Matches(item.Title, sessionName)) afterMatches++;
                if (string.IsNullOrEmpty(item.IdentityKey)) allKeys = false;
            }
            if (allKeys)
            {
                foreach (TabEvidence item in post.Evidence)
                    if (!keys.Contains(item.IdentityKey) && Matches(item.Title, sessionName)) return true;
                return false;
            }
            return afterMatches > beforeMatches;
        }

        public static SourceCloseEvidence EvaluateClose(PlannedMerge merge, TargetTabBaseline baseline,
            WindowInspection source, WindowInspection target)
        {
            bool valid = ValidPlan(merge);
            bool sourceIdentity = valid && source != null && merge.SourceIdentity.EqualsForMutation(source.Identity);
            bool targetIdentity = valid && target != null && merge.TargetIdentity.EqualsForMutation(target.Identity)
                && baseline != null && merge.TargetIdentity.EqualsForMutation(baseline.Identity);
            bool sourceTrusted = source != null && source.Tabs != null && source.Tabs.Trusted;
            bool targetTrusted = target != null && target.Tabs != null && target.Tabs.Trusted;
            int count = sourceTrusted ? source.Tabs.ObservedTabItemCount : 0;
            bool session = valid && count == 1 && Matches(source.Tabs.Titles[0], merge.Session.Name)
                && string.Equals(TitleNormalizer.StripPrefix(source.Tabs.Titles[0]), TitleNormalizer.StripPrefix(merge.SourceTabTitle), StringComparison.Ordinal);
            bool delta = valid && targetTrusted && HasNewMatchingTab(baseline, target.Tabs, merge.Session.Name);
            string detail = !sourceIdentity ? "source identity changed" : !targetIdentity ? "target identity changed"
                : !sourceTrusted || count != 1 ? "source tab evidence is incomplete or no longer single-tab"
                : !session ? "source session changed" : !targetTrusted || !delta ? "new target tab is no longer proven" : null;
            return new SourceCloseEvidence(sourceIdentity, targetIdentity, sourceTrusted, targetTrusted, session, delta, count, detail);
        }

        private static bool Matches(string title, string sessionName)
        {
            return string.Equals(TitleNormalizer.StripPrefix(title), sessionName, StringComparison.Ordinal);
        }
    }
}
