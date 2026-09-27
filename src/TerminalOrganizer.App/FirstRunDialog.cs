using System;
using System.Drawing;
using System.Windows.Forms;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The first-run dialog outcome (night-design B1): the acknowledgement plus the
    /// startup preference. The start-with-Windows wiring unit persists the checkbox
    /// AND applies it through StartupRegistration (a Startup-folder shortcut, never
    /// Run keys).
    /// </summary>
    public sealed class FirstRunResult
    {
        private readonly bool accepted;
        private readonly bool startWithWindows;

        public FirstRunResult(bool accepted, bool startWithWindows)
        {
            this.accepted = accepted;
            this.startWithWindows = startWithWindows;
        }

        /// <summary>True when the dialog closed through its button; the close box dismisses without accepting.</summary>
        public bool Accepted { get { return accepted; } }

        /// <summary>The checkbox state at close; persisted and applied to the Startup shortcut by the flow saving it.</summary>
        public bool StartWithWindows { get { return startWithWindows; } }
    }

    /// <summary>Injectable seam over the first-run dialog (the suite drives fakes; the real dialog is the morning checklist).</summary>
    public delegate FirstRunResult FirstRunShow(Icon icon, string hotkey, bool startWithWindows);

    /// <summary>
    /// The one-time onboarding flow (night-design B1): an incomplete first run invokes
    /// the dialog seam exactly once and persists firstRunCompleted=true together with
    /// the checkbox value; a completed first run returns the settings untouched and
    /// never invokes the seam. The WithStartup form additionally applies the checkbox
    /// through StartupRegistration — the explicit toggle event; a plain settings load
    /// never reaches any registration call.
    /// </summary>
    internal static class FirstRunFlow
    {
        /// <summary>
        /// The B1-compatible form: persists the checkbox without startup registration
        /// (the port is null). Production uses EnsureCompletedWithStartup.
        /// </summary>
        internal static AppSettings EnsureCompleted(AppSettings current, Icon applicationIcon,
            SettingsStore store, FirstRunShow show)
        {
            return EnsureCompletedWithStartup(current, applicationIcon, store, show, null);
        }

        /// <summary>
        /// The full form: after the dialog closes, persists firstRunCompleted=true
        /// with the checkbox value AND applies the start-with-Windows preference
        /// through StartupRegistration. A null port skips the registration (the
        /// 4-arg overload above); the registration runs only when the dialog seam
        /// actually returned a result, never for an already-completed first run.
        /// </summary>
        internal static AppSettings EnsureCompletedWithStartup(AppSettings current, Icon applicationIcon,
            SettingsStore store, FirstRunShow show, StartupShortcutPort startup)
        {
            if (show == null) throw new ArgumentNullException("show");
            AppSettings settings = current == null ? SettingsStore.Defaults() : current;
            if (settings.FirstRunCompleted)
            {
                return settings;
            }
            FirstRunResult result = show(applicationIcon, settings.Hotkey, settings.StartWithWindows);
            bool startWith = result == null ? settings.StartWithWindows : result.StartWithWindows;
            AppSettings updated = new AppSettings(settings.Hotkey, settings.LogPath,
                settings.ManagerWindowName, settings.MergeEnabled, true, startWith);
            if (store != null)
            {
                store.Save(updated);
            }
            if (result != null && startup != null)
            {
                StartupRegistration.Apply(startWith, startup);
            }
            return updated;
        }
    }

    /// <summary>
    /// The real first-run dialog (night-design B1 pinned copy): welcome title, the
    /// hotkey line, the tray-overflow hint, the startup checkbox (retained only) and
    /// one button. Never exercised by the suite — the suite drives FirstRunFlow with
    /// fakes.
    /// </summary>
    // @MX:WARN: [AUTO] real WinForms dialog surface — morning checklist; not an acceptance claim.
    internal static class FirstRunDialog
    {
        internal static FirstRunResult Show(Icon icon, string hotkey, bool startWithWindows)
        {
            bool accepted = false;
            bool checkedState = startWithWindows;
            using (Form dialog = new Form())
            using (System.Windows.Forms.Label primary = new System.Windows.Forms.Label())
            using (System.Windows.Forms.Label secondary = new System.Windows.Forms.Label())
            using (CheckBox startBox = new CheckBox())
            using (Button gotIt = new Button())
            {
                dialog.Text = "Welcome to TerminalOrganizer";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.Width = 460;
                dialog.Height = 200;
                if (icon != null)
                {
                    dialog.Icon = icon;
                }
                primary.Left = 16;
                primary.Top = 16;
                primary.Width = 412;
                primary.Height = 20;
                primary.Text = "Press " + EffectiveHotkey(hotkey) + " to organize Windows Terminal windows.";
                secondary.Left = 16;
                secondary.Top = 40;
                secondary.Width = 412;
                secondary.Height = 44;
                secondary.Text = "If the icon is hidden, open the ^ tray menu and drag TerminalOrganizer to the taskbar.";
                startBox.Left = 16;
                startBox.Top = 96;
                startBox.Width = 412;
                startBox.Height = 24;
                startBox.Text = "Start TerminalOrganizer with Windows";
                startBox.Checked = startWithWindows;
                gotIt.Left = 348;
                gotIt.Top = 132;
                gotIt.Width = 80;
                gotIt.Height = 28;
                gotIt.Text = "Got it";
                gotIt.DialogResult = DialogResult.OK;
                dialog.Controls.Add(primary);
                dialog.Controls.Add(secondary);
                dialog.Controls.Add(startBox);
                dialog.Controls.Add(gotIt);
                dialog.AcceptButton = gotIt;
                accepted = dialog.ShowDialog() == DialogResult.OK;
                checkedState = startBox.Checked;
            }
            return new FirstRunResult(accepted, checkedState);
        }

        private static string EffectiveHotkey(string hotkey)
        {
            return string.IsNullOrEmpty(hotkey) ? SettingsStore.DefaultHotkey : hotkey;
        }
    }
}
