using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace TerminalOrganizer.Core.Windows
{
    /// <summary>Injectable seam over GetWindowTextLengthW (B4): the raw title length, or a negative error.</summary>
    public delegate int TitleLengthProbe(IntPtr window);

    /// <summary>Injectable seam over GetWindowTextW (B4): fills the buffer, returns the copied length.</summary>
    public delegate int TitleBufferRead(IntPtr window, StringBuilder title, int maxCount);

    /// <summary>
    /// Reads the ordered tab-title list of a Windows Terminal window through UI
    /// Automation (REQ-ACQ-002). The walk uses automation names only — the tab-item
    /// descendants of the window element — so no WindowsBase dependency exists
    /// (plan.md B.4). A UIA failure returns a failure result carrying the raw
    /// GetWindowText title as fallback, never an exception. The fallback title source
    /// is an injectable seam (plan.md B.4, audit A6); the production default is
    /// GetWindowText. B4: the raw title is read with a DYNAMIC buffer
    /// (GetWindowTextLengthW, capped at 32767, length+1 allocation, one safe
    /// read attempt at length zero) — no silent 511-character truncation. The WT
    /// 1.24 tab pattern itself is validated on real windows by the morning
    /// checklist (tools/dump-windows.ps1), not by the suite.
    /// </summary>
    // @MX:WARN: real-screen surface, morning checklist (tools/dump-windows.ps1); not an acceptance claim.
    public sealed class UiaTabTitleReader
    {
        /// <summary>GetWindowTextLengthW's documented maximum window-title length.</summary>
        private const int MaxTitleLength = 32767;

        private readonly Func<IntPtr, string> fallbackTitleSource;
        private readonly TitleLengthProbe lengthProbe;
        private readonly TitleBufferRead bufferRead;

        /// <summary>Production constructor: the fallback title comes from the dynamic GetWindowText read.</summary>
        public UiaTabTitleReader()
            : this(new Func<IntPtr, string>(GetWindowTextTitle))
        {
        }

        /// <summary>Injectable seam (plan.md B.4): a supplier of the raw window title on the failure path.</summary>
        public UiaTabTitleReader(Func<IntPtr, string> fallbackTitleSource)
        {
            if (fallbackTitleSource == null)
            {
                throw new ArgumentNullException("fallbackTitleSource");
            }
            this.fallbackTitleSource = fallbackTitleSource;
            this.lengthProbe = NativeGetWindowTextLength;
            this.bufferRead = NativeGetWindowText;
        }

        /// <summary>Test seam (B4): injectable native length/read pair behind ReadWindowTitle.</summary>
        public UiaTabTitleReader(TitleLengthProbe lengthProbe, TitleBufferRead bufferRead)
            : this(new Func<IntPtr, string>(
                delegate(IntPtr window) { return ReadWindowTitleVia(lengthProbe, bufferRead, window); }))
        {
            this.lengthProbe = lengthProbe;
            this.bufferRead = bufferRead;
        }

        /// <summary>Reads the ordered tab titles of the window; never throws (REQ-ACQ-002).</summary>
        public TabTitleResult ReadTabs(IntPtr window)
        {
            try
            {
                AutomationElement root = AutomationElement.FromHandle(window);
                if (root == null)
                {
                    return TabTitleResult.Failure(FallbackTitle(window), "no automation element for the handle");
                }
                PropertyCondition tabItems = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                AutomationElementCollection children = root.FindAll(TreeScope.Descendants, tabItems);
                List<TabEvidence> evidence = new List<TabEvidence>();
                bool complete = true;
                foreach (AutomationElement child in children)
                {
                    string name = null;
                    string key = null;
                    try
                    {
                        name = child.Current.Name;
                        int[] runtimeId = child.GetRuntimeId();
                        if (runtimeId == null || runtimeId.Length == 0) complete = false;
                        else key = string.Join(".", Array.ConvertAll(runtimeId, delegate(int value) { return value.ToString(CultureInfo.InvariantCulture); }));
                    }
                    catch { complete = false; }
                    if (string.IsNullOrEmpty(name)) complete = false;
                    else evidence.Add(new TabEvidence(key, name));
                }
                if (children.Count == 0)
                {
                    // A WT window always has at least one tab; zero found means the pattern
                    // did not resolve — degrade to the fallback title.
                    return TabTitleResult.Failure(FallbackTitle(window), "no tab items found");
                }
                return complete ? TabTitleResult.TrustedResult(evidence.ToArray())
                    : TabTitleResult.Incomplete(evidence.ToArray(), children.Count, FallbackTitle(window), "incomplete tab names or runtime identities");
            }
            catch (Exception ex)
            {
                return TabTitleResult.Failure(FallbackTitle(window), ex.Message);
            }
        }

        /// <summary>
        /// The B4 dynamic raw-title read: query the true length, cap at 32767,
        /// allocate length+1, and still attempt a one-character-safe read when the
        /// reported length is zero. Returns null when the read fails; never throws.
        /// </summary>
        public string ReadWindowTitle(IntPtr window)
        {
            return ReadWindowTitleVia(lengthProbe, bufferRead, window);
        }

        /// <summary>The production fallback read: the raw window title through the instance probes, or null when the call fails.</summary>
        public static string GetWindowTextTitle(IntPtr window)
        {
            return ReadWindowTitleVia(NativeGetWindowTextLength, NativeGetWindowText, window);
        }

        private static string ReadWindowTitleVia(TitleLengthProbe lengthProbe, TitleBufferRead bufferRead, IntPtr window)
        {
            try
            {
                int length = lengthProbe(window);
                if (length < 0)
                {
                    return null;
                }
                if (length > MaxTitleLength)
                {
                    length = MaxTitleLength;
                }
                // Length zero still gets a one-character-safe buffer: the reported
                // length can undercount a title being written concurrently.
                int capacity = length + 1;
                if (capacity < 2)
                {
                    capacity = 2;
                }
                StringBuilder title = new StringBuilder(capacity);
                int read = bufferRead(window, title, title.Capacity);
                if (read <= 0)
                {
                    return null;
                }
                return read <= title.Length ? title.ToString(0, read) : title.ToString();
            }
            catch
            {
                return null;
            }
        }

        private string FallbackTitle(IntPtr window)
        {
            try
            {
                return fallbackTitleSource(window);
            }
            catch
            {
                return null;
            }
        }

        private static int NativeGetWindowTextLength(IntPtr window)
        {
            return NativeMethods.GetWindowTextLength(window);
        }

        private static int NativeGetWindowText(IntPtr window, StringBuilder title, int maxCount)
        {
            return NativeMethods.GetWindowText(window, title, maxCount);
        }

        /// <summary>Hand-written P/Invoke declarations (tech.md; no assembly reference needed).</summary>
        private static class NativeMethods
        {
            [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
            public static extern int GetWindowText(IntPtr window, StringBuilder title, int maxCount);

            [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
            public static extern int GetWindowTextLength(IntPtr window);
        }
    }
}
