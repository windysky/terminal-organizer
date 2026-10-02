using System;
using System.Reflection;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// The product version as the user sees it (tray tooltip and menu row), read from
    /// this assembly's informational version (src/AssemblyVersion.cs).
    /// </summary>
    public static class AppVersion
    {
        public static string Text
        {
            get
            {
                object[] attributes = typeof(AppVersion).Assembly.GetCustomAttributes(
                    typeof(AssemblyInformationalVersionAttribute), false);
                return attributes.Length == 0
                    ? "0.0.0"
                    : ((AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;
            }
        }

        public static string DisplayName
        {
            get { return "TerminalOrganizer v" + Text; }
        }
    }
}
