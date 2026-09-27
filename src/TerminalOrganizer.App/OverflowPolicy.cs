using System;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The persisted overflow policy (C3, night-design-2026-09-25): what an organize run
    /// does with overflow windows after pure planning. Ask defers the decision to the
    /// pre-mutation choice dialog; Stack applies only the final local assignment; Merge
    /// additionally executes the planned merges (permitted only while BOTH the saved
    /// policy and the merge release gate say enabled — otherwise it degrades to Stack
    /// with one warning); Redistribute applies the C2 cross-monitor plan first, then the
    /// recomputed local assignments. Immutable value semantics.
    /// </summary>
    public enum OverflowPolicy
    {
        Ask,
        Stack,
        Merge,
        Redistribute
    }

    /// <summary>
    /// One choice-dialog outcome (C3): the chosen action, or Cancel for "do nothing".
    /// Ask never reaches Commit — the UI resolves it to another value or cancels.
    /// </summary>
    public enum OverflowChoice
    {
        Cancel,
        Stack,
        Merge,
        Redistribute
    }

    /// <summary>
    /// The choice dialog's result (C3): the resolved action and whether the user asked
    /// to remember it as the saved policy. Immutable.
    /// </summary>
    public sealed class OverflowChoiceResult
    {
        private readonly OverflowChoice choice;
        private readonly bool rememberChoice;

        public OverflowChoiceResult(OverflowChoice choice, bool rememberChoice)
        {
            this.choice = choice;
            this.rememberChoice = rememberChoice;
        }

        public OverflowChoice Choice { get { return choice; } }

        /// <summary>True when the chosen policy should replace the saved Ask.</summary>
        public bool RememberChoice { get { return rememberChoice; } }
    }

    /// <summary>
    /// The persisted-policy text mapping for the settings file (C3): the four enum
    /// names round-trip case-insensitively and every unknown or missing text loads as
    /// Ask (tolerant by contract, like every other settings field).
    /// </summary>
    public static class OverflowPolicyParser
    {
        /// <summary>Unknown, empty or null text resolves to Ask.</summary>
        public static OverflowPolicy Parse(string text)
        {
            OverflowPolicy policy;
            if (Enum.TryParse<OverflowPolicy>(text, true, out policy))
            {
                return policy;
            }
            return OverflowPolicy.Ask;
        }

        public static string Format(OverflowPolicy policy)
        {
            return policy.ToString();
        }
    }
}
