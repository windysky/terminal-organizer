using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TerminalOrganizer.Core.Rules
{
    /// <summary>
    /// Validates rule definitions and compiles them once into a RuleSet (REQ-RUL-005, NFR-2).
    /// A definition is the value a JSON reader produces: a string-keyed map (IDictionary) whose
    /// values are strings, integers, decimals, booleans or nested values, so the builder checks
    /// JSON value types itself. A JSON null counts as an absent field.
    /// </summary>
    public static class RuleSetBuilder
    {
        /// <summary>The per-evaluation time bound of every user regular expression (NFR-3).</summary>
        public const int UserRegexTimeoutMilliseconds = 100;

        /// <summary>The prefix that turns a match value from a glob into a regular expression.</summary>
        public const string RegexPrefix = "regex:";

        public const int RankMin = 0;
        public const int RankMax = 999;

        // @MX:NOTE: never throws; one diagnostic per skipped rule, 1-based position as written (null and malformed entries count).
        public static RuleSet Build(IEnumerable definitions, bool presetEnabled)
        {
            List<CompiledRule> rules = new List<CompiledRule>();
            List<RuleDiagnostic> diagnostics = new List<RuleDiagnostic>();
            if (presetEnabled)
            {
                rules.Add(LauncherPrefixPreset.CreateRule());
            }
            if (definitions != null)
            {
                int position = 0;
                try
                {
                    foreach (object definition in definitions)
                    {
                        position++;
                        CompileOne(position, definition, rules, diagnostics);
                    }
                }
                catch (Exception ex)
                {
                    // A hostile enumerable must not escape: report it against the next position.
                    diagnostics.Add(new RuleDiagnostic(position + 1, "rule list could not be read: " + ex.Message));
                }
            }
            return new RuleSet(rules.ToArray(), diagnostics.ToArray(), presetEnabled);
        }

        private static void CompileOne(int position, object definition, List<CompiledRule> rules, List<RuleDiagnostic> diagnostics)
        {
            try
            {
                string reason;
                CompiledRule rule = Compile(position, definition, out reason);
                if (rule != null)
                {
                    rules.Add(rule);
                }
                else
                {
                    diagnostics.Add(new RuleDiagnostic(position, reason));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new RuleDiagnostic(position, "rule could not be read: " + ex.Message));
            }
        }

        /// <summary>Validates one definition; returns the compiled rule, or null with the reason set.</summary>
        private static CompiledRule Compile(int position, object definition, out string reason)
        {
            reason = null;
            IDictionary map = definition as IDictionary;
            if (map == null)
            {
                reason = "not an object";
                return null;
            }

            object match = Field(map, "match");
            object marker = Field(map, "marker");
            object onField = Field(map, "on");
            object name = Field(map, "name");
            object rank = Field(map, "rank");

            if ((match == null) == (marker == null))
            {
                reason = "needs exactly one of match or marker";
                return null;
            }
            if (match != null && !(match is string))
            {
                reason = "match must be a string";
                return null;
            }
            if (marker != null && !(marker is string))
            {
                reason = "marker must be a string";
                return null;
            }
            if (onField != null && !(onField is string))
            {
                reason = "on must be a string";
                return null;
            }
            if (name != null && !(name is string))
            {
                reason = "name must be a string";
                return null;
            }
            int? fixedRank = null;
            if (rank != null)
            {
                long value;
                if (!TryReadInteger(rank, out value))
                {
                    reason = "rank must be an integer";
                    return null;
                }
                if (value < RankMin || value > RankMax)
                {
                    reason = "rank must be within 0..999";
                    return null;
                }
                fixedRank = (int)value;
            }

            if (marker != null)
            {
                return CompileMarker(position, (string)marker, onField, (string)name, fixedRank, out reason);
            }
            return CompileMatch(position, (string)match, (string)onField, (string)name, fixedRank, out reason);
        }

        private static CompiledRule CompileMarker(int position, string marker, object onField, string name, int? fixedRank, out string reason)
        {
            reason = null;
            if (marker.Length == 0)
            {
                reason = "marker is empty";
                return null;
            }
            foreach (char c in marker)
            {
                if (char.IsWhiteSpace(c) || char.IsDigit(c))
                {
                    reason = "marker must not contain whitespace or a digit";
                    return null;
                }
            }
            if (onField != null)
            {
                reason = "on is not allowed on a marker rule";
                return null;
            }
            return new CompiledRule(position, RuleKind.Marker, RuleSubject.Title, null, marker, null,
                name, fixedRank, "rule " + position + ": marker " + marker, false);
        }

        private static CompiledRule CompileMatch(int position, string match, string onValue, string name, int? fixedRank, out string reason)
        {
            reason = null;
            RuleSubject subject = RuleSubject.Title;
            if (onValue != null)
            {
                if (string.Equals(onValue, "commandline", StringComparison.Ordinal))
                {
                    subject = RuleSubject.CommandLine;
                }
                else if (!string.Equals(onValue, "title", StringComparison.Ordinal))
                {
                    reason = "on must be title or commandline";
                    return null;
                }
            }
            if (match.Length == 0)
            {
                reason = "pattern is empty";
                return null;
            }

            string reference = "rule " + position + ": " + match;
            if (match.StartsWith(RegexPrefix, StringComparison.Ordinal))
            {
                string pattern = match.Substring(RegexPrefix.Length);
                if (pattern.Length == 0)
                {
                    reason = "pattern is empty";
                    return null;
                }
                Regex regex;
                try
                {
                    // @MX:WARN: user-supplied pattern compiled here and run in RuleEvaluator; the timeout is the only bound.
                    // @MX:REASON: a hostile or careless pattern can backtrack catastrophically; evaluation must stay bounded.
                    regex = new Regex(pattern, RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(UserRegexTimeoutMilliseconds));
                }
                catch (ArgumentException ex)
                {
                    reason = "invalid regex: " + ex.Message;
                    return null;
                }
                return new CompiledRule(position, RuleKind.Regex, subject, pattern, null, regex,
                    name, fixedRank, reference, false);
            }
            return new CompiledRule(position, RuleKind.Glob, subject, match, null, null,
                name, fixedRank, reference, false);
        }

        /// <summary>The value of a field, or null when absent or a JSON null.</summary>
        private static object Field(IDictionary map, string key)
        {
            if (!map.Contains(key))
            {
                return null;
            }
            return map[key];
        }

        /// <summary>
        /// True for the integral CLR types a JSON reader yields for an integer; false for decimals,
        /// doubles, strings, booleans and everything else (a rank of 3.0 is not an integer).
        /// </summary>
        private static bool TryReadInteger(object value, out long result)
        {
            result = 0;
            if (value is int)
            {
                result = (int)value;
                return true;
            }
            if (value is long)
            {
                result = (long)value;
                return true;
            }
            if (value is short)
            {
                result = (short)value;
                return true;
            }
            if (value is byte)
            {
                result = (byte)value;
                return true;
            }
            if (value is sbyte)
            {
                result = (sbyte)value;
                return true;
            }
            if (value is ushort)
            {
                result = (ushort)value;
                return true;
            }
            if (value is uint)
            {
                result = (uint)value;
                return true;
            }
            if (value is ulong)
            {
                ulong big = (ulong)value;
                result = big > (ulong)RankMax ? (long)RankMax + 1 : (long)big;
                return true;
            }
            return false;
        }
    }
}
