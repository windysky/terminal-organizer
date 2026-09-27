using System;
using System.Windows.Forms;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The App entry point (REQ-APP-001): single-instance gate first (A5), then the tray
    /// context on an STA thread with the standard WinForms application setup. A second
    /// instance per user/session returns 0 here — before EnableVisualStyles or any shell
    /// construction. The tray shell (NotifyIcon, hotkey, menu) is the whole application;
    /// there is no main window.
    /// </summary>
    // @MX:NOTE: [AUTO] STA + ApplicationContext (REQ-APP-001); morning checklist launches the exe.
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Environment.ExitCode = AppBootstrap.Run(InstanceGate.TryAcquire, delegate
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (TrayShell shell = new TrayShell())
                {
                    Application.Run(shell);
                }
            });
        }
    }
}
