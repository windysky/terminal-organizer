using System;
using System.Collections.Generic;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// Run-scoped labels for unidentified windows (REQ-PIPE-004, spec D-1): a label lasts
    /// for the CURRENT process lifetime only — never persisted, never written to settings.
    /// The discovery adapter consults this registry when composing the matcher input (a
    /// labeled window is identified on subsequent organize runs in the same process); a
    /// fresh process starts with a fresh registry and never sees the label.
    /// </summary>
    public sealed class LabelRegistry
    {
        private readonly Dictionary<long, string> labels = new Dictionary<long, string>();
        private readonly object gate = new object();

        /// <summary>Attaches a session-name label to a window handle; null/empty names are ignored.</summary>
        public void SetLabel(IntPtr window, string sessionName)
        {
            if (window == IntPtr.Zero || string.IsNullOrEmpty(sessionName))
            {
                return;
            }
            lock (gate)
            {
                labels[window.ToInt64()] = sessionName;
            }
        }

        /// <summary>The label for a window handle; null when it has none.</summary>
        public string TryGetLabel(IntPtr window)
        {
            lock (gate)
            {
                string name;
                if (labels.TryGetValue(window.ToInt64(), out name))
                {
                    return name;
                }
                return null;
            }
        }

        /// <summary>Drops every label (run-scope reset).</summary>
        public void Clear()
        {
            lock (gate)
            {
                labels.Clear();
            }
        }

        public int Count
        {
            get { lock (gate) { return labels.Count; } }
        }
    }
}
