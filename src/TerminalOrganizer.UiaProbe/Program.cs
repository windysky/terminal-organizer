using System;
using System.Globalization;
using System.Text;
using TerminalOrganizer.Core.Windows;

namespace TerminalOrganizer.UiaProbe
{
    /// <summary>
    /// The B4 UIA probe helper: a small STA console exe that performs ONE tab-title
    /// read in its own process so a hung COM call can be killed wholesale instead of
    /// abandoned in-process. Read-only — it never mutates any window. Protocol: the
    /// client starts it as <c>--hwnd &lt;signed-decimal-int64&gt;</c>; it answers with one
    /// UTF-8 line (OK/INCOMPLETE/ERROR with base64(identity):base64(title) pairs)
    /// and always exits 0 — the client parses stdout, never the exit code.
    /// </summary>
    // @MX:WARN: real-screen UIA surface exercised by the client in production (morning checklist).
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                // A redirected pipe without an encoding switch still carries ASCII-safe base64.
            }
            IntPtr window;
            if (!TryParseHandle(args, out window))
            {
                Console.Out.WriteLine("ERROR|0||" + Encode("bad arguments: expected --hwnd <int64>"));
                return 0;
            }
            TabTitleResult result = new UiaTabTitleReader().ReadTabs(window);
            Console.Out.WriteLine(FormatResult(result));
            return 0;
        }

        private static bool TryParseHandle(string[] args, out IntPtr window)
        {
            window = IntPtr.Zero;
            if (args == null || args.Length != 2 || !string.Equals(args[0], "--hwnd", StringComparison.Ordinal))
            {
                return false;
            }
            long value;
            if (!long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }
            window = new IntPtr(value);
            return true;
        }

        private static string FormatResult(TabTitleResult result)
        {
            if (result == null)
            {
                return "ERROR|0||" + Encode("null read result");
            }
            if (result.Quality == TabReadQuality.Trusted)
            {
                return "OK|" + result.Evidence.Length.ToString(CultureInfo.InvariantCulture)
                    + "|" + FormatPairs(result.Evidence);
            }
            if (result.Quality == TabReadQuality.Incomplete)
            {
                return "INCOMPLETE|" + result.ObservedTabItemCount.ToString(CultureInfo.InvariantCulture)
                    + "|" + FormatPairs(result.Evidence) + "|" + Encode(result.Error ?? string.Empty);
            }
            return "ERROR|0||" + Encode(result.Error ?? result.Quality.ToString());
        }

        private static string FormatPairs(TabEvidence[] evidence)
        {
            if (evidence == null || evidence.Length == 0)
            {
                return string.Empty;
            }
            StringBuilder payload = new StringBuilder();
            foreach (TabEvidence item in evidence)
            {
                if (payload.Length > 0)
                {
                    payload.Append(';');
                }
                payload.Append(Encode(item == null ? null : item.IdentityKey));
                payload.Append(':');
                payload.Append(Encode(item == null ? null : item.Title));
            }
            return payload.ToString();
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }
    }
}
