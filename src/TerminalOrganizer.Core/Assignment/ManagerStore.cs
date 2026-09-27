using System;
using System.IO;
using System.Web.Script.Serialization;

namespace TerminalOrganizer.Core.Assignment
{
    /// <summary>
    /// Loads and saves the persisted manager choice (REQ-MGR-001): a single window_name
    /// string stored as JSON at a caller-supplied path in the shape
    /// {"managerWindowName":"..."} (plan.md B.2). Tolerant by contract: a missing or
    /// malformed file loads as no choice and no file content ever throws; an empty string
    /// clears the choice. TRAY-007 decides the real location (expected
    /// %LOCALAPPDATA%\TerminalOrganizer\settings.json).
    /// </summary>
    public sealed class ManagerStore
    {
        private readonly string path;

        public ManagerStore(string path)
        {
            this.path = path;
        }

        // @MX:NOTE: [AUTO] window_name-keyed persistence (product.md decision 3): the choice matches a window by exact ordinal name equality.
        /// <summary>
        /// Loads the manager choice; null (no choice) when the file is missing, malformed,
        /// of the wrong shape, or carries an empty name. Never throws for file content.
        /// </summary>
        public string Load()
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }
                string json = File.ReadAllText(path);
                ManagerChoiceDto dto = new JavaScriptSerializer().Deserialize<ManagerChoiceDto>(json);
                if (dto == null || string.IsNullOrEmpty(dto.managerWindowName))
                {
                    return null;
                }
                return dto.managerWindowName;
            }
            catch
            {
                // A missing or malformed file degrades to no choice; never throws (REQ-MGR-001).
                return null;
            }
        }

        // @MX:NOTE: [AUTO] window_name-keyed persistence (product.md decision 3); an empty string clears the choice.
        /// <summary>
        /// Saves the manager choice; an empty string clears it. Best-effort: an I/O failure
        /// is swallowed so a settings write can never take the caller down.
        /// </summary>
        public void Save(string managerWindowName)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }
                ManagerChoiceDto dto = new ManagerChoiceDto();
                dto.managerWindowName = managerWindowName ?? string.Empty;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(dto));
            }
            catch
            {
                // Best-effort save; a store write failure is not worth an exception (plan.md D tolerance).
            }
        }

        /// <summary>The one-field JSON shape (plan.md B.2); a public field so JavaScriptSerializer maps it.</summary>
        private sealed class ManagerChoiceDto
        {
            public string managerWindowName;
        }
    }
}
