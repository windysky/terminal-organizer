using System;
using System.Globalization;
using System.IO;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The append-only log wrapper (REQ-NOTI-002): one timestamped line per call to the
    /// configured path, written through (each Append is its own File.AppendAllText, so
    /// Flush is a no-op kept for the REQ-SHELL-003 exit sequence). A write failure drops
    /// the line silently — the sink never throws and never takes the organizer down.
    /// </summary>
    public sealed class LogSink
    {
        private readonly string path;

        public LogSink(string path)
        {
            this.path = path;
        }

        /// <summary>Appends one timestamped line; a write failure drops the line, never throws (REQ-NOTI-002).</summary>
        public void Append(string line)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + " " + line + Environment.NewLine);
            }
            catch
            {
                // REQ-NOTI-002: the line is dropped silently; logging must never break an organize run.
            }
        }

        /// <summary>Write-through sink: nothing to flush. Present for the exit sequence (REQ-SHELL-003).</summary>
        public void Flush()
        {
        }
    }
}
