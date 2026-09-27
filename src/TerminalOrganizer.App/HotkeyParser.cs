using System;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// One hotkey parse outcome (REQ-SET-002): success carrying the RegisterHotKey modifier
    /// flags and virtual-key code, or failure. Immutable; failures are values, never throws.
    /// </summary>
    public sealed class HotkeyParseResult
    {
        private readonly bool success;
        private readonly int modifierFlags;
        private readonly int virtualKey;

        internal HotkeyParseResult(bool success, int modifierFlags, int virtualKey)
        {
            this.success = success;
            this.modifierFlags = modifierFlags;
            this.virtualKey = virtualKey;
        }

        public bool Success { get { return success; } }
        public int ModifierFlags { get { return modifierFlags; } }
        public int VirtualKey { get { return virtualKey; } }

        public static HotkeyParseResult Ok(int modifierFlags, int virtualKey)
        {
            return new HotkeyParseResult(true, modifierFlags, virtualKey);
        }

        public static HotkeyParseResult Fail()
        {
            return new HotkeyParseResult(false, 0, 0);
        }

        public override string ToString()
        {
            return string.Format("{0} mods=0x{1:X} vk=0x{2:X}", success, modifierFlags, virtualKey);
        }
    }

    /// <summary>
    /// Parses a settings hotkey string like "Ctrl+Alt+O" into RegisterHotKey inputs
    /// (REQ-SET-002, plan.md B.3). Pure and case-insensitive; every rejection is a failure
    /// result. Tokens split on '+': the modifiers Ctrl / Alt / Shift / Win (at least one
    /// required — a bare key would steal the key from normal typing) and exactly one key
    /// token: a letter A-Z, a digit 0-9, or an F-key F1-F12. Unknown tokens, a second key
    /// token, an empty token or a null/empty input all fail.
    /// </summary>
    // @MX:NOTE: [AUTO] RegisterHotKey flag mapping (MOD_ALT 0x1 / MOD_CONTROL 0x2 / MOD_SHIFT 0x4 / MOD_WIN 0x8) pinned in plan.md B.3 / J.2.
    public static class HotkeyParser
    {
        /// <summary>RegisterHotKey MOD_ALT.</summary>
        public const int ModAlt = 0x0001;

        /// <summary>RegisterHotKey MOD_CONTROL.</summary>
        public const int ModControl = 0x0002;

        /// <summary>RegisterHotKey MOD_SHIFT.</summary>
        public const int ModShift = 0x0004;

        /// <summary>RegisterHotKey MOD_WIN.</summary>
        public const int ModWin = 0x0008;

        /// <summary>Parses one hotkey string; never throws.</summary>
        public static HotkeyParseResult Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return HotkeyParseResult.Fail();
            }
            string[] tokens = text.Split('+');
            int modifiers = 0;
            bool sawModifier = false;
            int virtualKey = 0;
            foreach (string raw in tokens)
            {
                string token = (raw ?? string.Empty).Trim();
                if (token.Length == 0)
                {
                    return HotkeyParseResult.Fail();
                }
                string lower = token.ToLowerInvariant();
                if (lower == "ctrl")
                {
                    modifiers |= ModControl;
                    sawModifier = true;
                }
                else if (lower == "alt")
                {
                    modifiers |= ModAlt;
                    sawModifier = true;
                }
                else if (lower == "shift")
                {
                    modifiers |= ModShift;
                    sawModifier = true;
                }
                else if (lower == "win")
                {
                    modifiers |= ModWin;
                    sawModifier = true;
                }
                else
                {
                    if (virtualKey != 0)
                    {
                        // A second key token (e.g. "Ctrl+Alt+O+P") is not one hotkey.
                        return HotkeyParseResult.Fail();
                    }
                    int key = ParseKey(lower);
                    if (key == 0)
                    {
                        return HotkeyParseResult.Fail();
                    }
                    virtualKey = key;
                }
            }
            if (!sawModifier || virtualKey == 0)
            {
                return HotkeyParseResult.Fail();
            }
            return HotkeyParseResult.Ok(modifiers, virtualKey);
        }

        /// <summary>Maps one key token to its virtual-key code; 0 when the token is no key.</summary>
        private static int ParseKey(string lower)
        {
            if (lower.Length == 1)
            {
                char c = lower[0];
                if (c >= 'a' && c <= 'z')
                {
                    return 0x41 + (c - 'a');
                }
                if (c >= '0' && c <= '9')
                {
                    return 0x30 + (c - '0');
                }
                return 0;
            }
            if (lower.Length >= 2 && lower[0] == 'f')
            {
                int function;
                if (int.TryParse(lower.Substring(1), out function) && function >= 1 && function <= 12)
                {
                    return 0x70 + (function - 1);
                }
            }
            return 0;
        }
    }
}
