using System;
using System.IO;
using System.Reflection;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The injectable seam over the Startup-folder shortcut (start-with-Windows
    /// unit): an exists / create / remove trio invoked with one shortcut path.
    /// Tests drive fakes whose file effects land in a temp directory — the real
    /// Startup folder is never touched by the suite. Default() is the real shell
    /// implementation: WScript.Shell COM reached through reflection only (C#5-safe,
    /// no dynamic, no new assembly references — the App /r: block is unchanged).
    /// </summary>
    public sealed class StartupShortcutPort
    {
        private readonly Func<string, bool> exists;
        private readonly Action<string> create;
        private readonly Action<string> remove;

        public StartupShortcutPort(Func<string, bool> exists, Action<string> create, Action<string> remove)
        {
            if (exists == null) throw new ArgumentNullException("exists");
            if (create == null) throw new ArgumentNullException("create");
            if (remove == null) throw new ArgumentNullException("remove");
            this.exists = exists;
            this.create = create;
            this.remove = remove;
        }

        /// <summary>The production port: file-system exists/delete plus COM shortcut creation.</summary>
        public static StartupShortcutPort Default()
        {
            return new StartupShortcutPort(ShellExists, ShellCreate, ShellRemove);
        }

        /// <summary>Probes the shortcut path; port methods stay assembly-internal (the port is a capability handed to StartupRegistration).</summary>
        internal bool Exists(string shortcutPath) { return exists(shortcutPath); }

        /// <summary>Creates the shortcut at the given path.</summary>
        internal void Create(string shortcutPath) { create(shortcutPath); }

        /// <summary>Removes the shortcut at the given path.</summary>
        internal void Remove(string shortcutPath) { remove(shortcutPath); }

        private static bool ShellExists(string shortcutPath)
        {
            try
            {
                return File.Exists(shortcutPath);
            }
            catch
            {
                // Best-effort probe (SettingsStore save doctrine): an unreadable
                // path reads as absent, never throws.
                return false;
            }
        }

        // @MX:WARN: [AUTO] real WScript.Shell COM surface — morning checklist; not an acceptance claim.
        // @MX:REASON: the suite exercises only fakes; the real shortcut round-trip is verified by hand on the operator machine.
        private static void ShellCreate(string shortcutPath)
        {
            try
            {
                // A relative path means the Startup folder did not resolve; creating
                // would drop a stray .lnk beside the working directory instead.
                if (string.IsNullOrEmpty(Path.GetDirectoryName(shortcutPath)))
                {
                    return;
                }
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }
                object shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return;
                }
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut == null)
                {
                    return;
                }
                string targetPath = ExecutablePath();
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut,
                    new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                    new object[] { Path.GetDirectoryName(targetPath) });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut,
                    new object[] { targetPath + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                    new object[] { StartupRegistration.ShortcutDescription });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch
            {
                // Best-effort registration (SettingsStore save doctrine): a failed
                // COM shortcut creation is dropped, never surfaced into the
                // first-run flow as a crash.
            }
        }

        private static void ShellRemove(string shortcutPath)
        {
            try
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
            catch
            {
                // Best-effort removal: same doctrine, never throws.
            }
        }

        /// <summary>
        /// The running exe: the assembly location is the exe path for a csc-built
        /// winexe; the BaseDirectory + assembly-name fallback covers a host where
        /// Location reads empty (byte-loaded, like the Pester suite host).
        /// </summary>
        private static string ExecutablePath()
        {
            Assembly assembly = typeof(StartupShortcutPort).Assembly;
            string location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return location;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assembly.GetName().Name + ".exe");
        }
    }

    /// <summary>
    /// Applies the persisted start-with-Windows preference to the user's Startup
    /// folder (start-with-Windows unit): true ensures the canonical
    /// TerminalOrganizer.lnk shortcut exists (idempotent), false removes it when
    /// present. Invoked ONLY on explicit toggle events — the first-run dialog
    /// completion (FirstRunFlow.EnsureCompletedWithStartup) and future explicit
    /// settings changes — never on a plain settings load, so a hand-made manual
    /// shortcut at the canonical path survives every non-toggle path.
    /// </summary>
    public static class StartupRegistration
    {
        /// <summary>The shortcut file name inside the per-user Startup folder.</summary>
        public const string ShortcutFileName = "TerminalOrganizer.lnk";

        /// <summary>The shortcut description shown by the shell.</summary>
        public const string ShortcutDescription = "TerminalOrganizer tray app";

        /// <summary>
        /// The canonical shortcut path: the user's Start Menu\Programs\Startup
        /// folder (SpecialFolder.Startup resolves the redirected form) plus the
        /// pinned file name.
        /// </summary>
        public static string CanonicalShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);
        }

        /// <summary>true: create when missing (idempotent ensure); false: remove when present.</summary>
        public static void Apply(bool startWithWindows, StartupShortcutPort port)
        {
            if (port == null) throw new ArgumentNullException("port");
            string shortcutPath = CanonicalShortcutPath();
            if (startWithWindows)
            {
                if (!port.Exists(shortcutPath))
                {
                    port.Create(shortcutPath);
                }
            }
            else if (port.Exists(shortcutPath))
            {
                port.Remove(shortcutPath);
            }
        }
    }
}
