using System;
using System.Text.RegularExpressions;

namespace TerminalOrganizer.Core.Rules
{
    /// <summary>What a rule is matched against (REQ-RUL-004).</summary>
    public enum RuleSubject
    {
        /// <summary>The tab title.</summary>
        Title,

        /// <summary>The command line of the launcher session the tab is bound to.</summary>
        CommandLine
    }

    /// <summary>How a rule matches (REQ-RUL-002/003).</summary>
    public enum RuleKind
    {
        /// <summary>Whole-subject glob with * and ?.</summary>
        Glob,

        /// <summary>A .NET regular expression with optional named captures name and rank.</summary>
        Regex,

        /// <summary>A rank marker token such as #3.</summary>
        Marker
    }

    /// <summary>
    /// One skipped rule or one evaluation timeout (REQ-RUL-005): the 1-based position of the
    /// rule in the user list as written (0 for the built-in preset) and the reason. Immutable.
    /// </summary>
    public sealed class RuleDiagnostic
    {
        private readonly int position;
        private readonly string reason;

        public RuleDiagnostic(int position, string reason)
        {
            this.position = position;
            this.reason = reason;
        }

        /// <summary>The 1-based position in the user list as written.</summary>
        public int Position { get { return position; } }

        public string Reason { get { return reason; } }

        public override string ToString()
        {
            return string.Format("rule {0}: {1}", position, reason);
        }
    }

    /// <summary>
    /// One validated rule, compiled once per rule-set build (NFR-2). Immutable; the regular
    /// expression, when present, is shared by every evaluation.
    /// </summary>
    public sealed class CompiledRule
    {
        private readonly int position;
        private readonly RuleKind kind;
        private readonly RuleSubject subject;
        private readonly string pattern;
        private readonly string marker;
        private readonly Regex regex;
        private readonly string fixedName;
        private readonly int? fixedRank;
        private readonly string reference;
        private readonly bool preset;

        public CompiledRule(int position, RuleKind kind, RuleSubject subject, string pattern, string marker,
            Regex regex, string fixedName, int? fixedRank, string reference, bool preset)
        {
            this.position = position;
            this.kind = kind;
            this.subject = subject;
            this.pattern = pattern;
            this.marker = marker;
            this.regex = regex;
            this.fixedName = fixedName;
            this.fixedRank = fixedRank;
            this.reference = reference;
            this.preset = preset;
        }

        /// <summary>The 1-based position in the user list as written; 0 for the preset.</summary>
        public int Position { get { return position; } }

        public RuleKind Kind { get { return kind; } }
        public RuleSubject Subject { get { return subject; } }

        /// <summary>The glob text, or the regex remainder after the regex: prefix; null for marker rules.</summary>
        public string Pattern { get { return pattern; } }

        /// <summary>The marker text; null for glob and regex rules.</summary>
        public string Marker { get { return marker; } }

        /// <summary>The compiled regular expression; null for glob and marker rules.</summary>
        public Regex Regex { get { return regex; } }

        public string FixedName { get { return fixedName; } }
        public int? FixedRank { get { return fixedRank; } }

        /// <summary>The provenance reference: "preset launcher-prefix", "rule n: text" or "rule n: marker m".</summary>
        public string Reference { get { return reference; } }

        public bool IsPreset { get { return preset; } }
    }

    /// <summary>
    /// The result of one rule-set build (REQ-RUL-005): the ordered compiled rules (the preset
    /// first when enabled) and one diagnostic per skipped rule. Immutable; array properties
    /// return copies.
    /// </summary>
    public sealed class RuleSet
    {
        private readonly CompiledRule[] rules;
        private readonly RuleDiagnostic[] diagnostics;
        private readonly bool presetEnabled;

        public RuleSet(CompiledRule[] rules, RuleDiagnostic[] diagnostics, bool presetEnabled)
        {
            this.rules = rules == null ? new CompiledRule[0] : (CompiledRule[])rules.Clone();
            this.diagnostics = diagnostics == null ? new RuleDiagnostic[0] : (RuleDiagnostic[])diagnostics.Clone();
            this.presetEnabled = presetEnabled;
        }

        public CompiledRule[] Rules { get { return (CompiledRule[])rules.Clone(); } }
        public RuleDiagnostic[] Diagnostics { get { return (RuleDiagnostic[])diagnostics.Clone(); } }
        public bool PresetEnabled { get { return presetEnabled; } }

        /// <summary>A rule set holding only the enabled launcher-prefix preset (REQ-ID-001).</summary>
        public static RuleSet PresetOnly()
        {
            return RuleSetBuilder.Build(null, true);
        }
    }

    /// <summary>
    /// What the rules decided for one tab (REQ-RUL-001): whether a rule matched, the identity
    /// name (null when no source is non-blank), the rank the rule supplied (null when none),
    /// the deciding rule's reference (null when none matched) and any timeout diagnostics.
    /// </summary>
    public sealed class TabRuleResult
    {
        private readonly bool matched;
        private readonly string name;
        private readonly int? rank;
        private readonly string reference;
        private readonly RuleDiagnostic[] diagnostics;

        public TabRuleResult(bool matched, string name, int? rank, string reference, RuleDiagnostic[] diagnostics)
        {
            this.matched = matched;
            this.name = name;
            this.rank = rank;
            this.reference = reference;
            this.diagnostics = diagnostics == null ? new RuleDiagnostic[0] : (RuleDiagnostic[])diagnostics.Clone();
        }

        public bool Matched { get { return matched; } }
        public string Name { get { return name; } }
        public int? Rank { get { return rank; } }
        public string Reference { get { return reference; } }
        public RuleDiagnostic[] Diagnostics { get { return (RuleDiagnostic[])diagnostics.Clone(); } }
    }

    /// <summary>
    /// The rule outcome of one window (REQ-ID-001): the identity name (null when none), the
    /// rank its rules supplied (null when none), one rule reference (the rank-supplying rule's
    /// when a rank exists, else the first tab's matched rule, else null) and the timeout
    /// diagnostics raised while evaluating its tabs.
    /// </summary>
    public sealed class WindowRuleOutcome
    {
        private readonly string identityName;
        private readonly int? rank;
        private readonly string reference;
        private readonly RuleDiagnostic[] diagnostics;

        public WindowRuleOutcome(string identityName, int? rank, string reference, RuleDiagnostic[] diagnostics)
        {
            this.identityName = identityName;
            this.rank = rank;
            this.reference = reference;
            this.diagnostics = diagnostics == null ? new RuleDiagnostic[0] : (RuleDiagnostic[])diagnostics.Clone();
        }

        public string IdentityName { get { return identityName; } }
        public int? Rank { get { return rank; } }
        public string Reference { get { return reference; } }
        public RuleDiagnostic[] Diagnostics { get { return (RuleDiagnostic[])diagnostics.Clone(); } }
    }
}
