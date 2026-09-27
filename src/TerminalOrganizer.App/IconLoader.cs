using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace TerminalOrganizer.App
{
    /// <summary>
    /// Loads the embedded application icons (night-design B1): selects the theme
    /// asset from the Personalize registry value and returns a CLONE of the
    /// manifest-resource icon so the returned handle outlives the loader's stream.
    /// </summary>
    public static class IconLoader
    {
        private const string LightResourceName = "TerminalOrganizer.ico";
        private const string DarkResourceName = "TerminalOrganizer.Dark.ico";
        private const string ThemeKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        /// <summary>
        /// True when the Windows application theme is dark. A missing or unreadable
        /// AppsUseLightTheme value selects the light asset.
        /// </summary>
        public static bool IsDarkApplicationTheme()
        {
            try
            {
                object value = Microsoft.Win32.Registry.GetValue(
                    "HKEY_CURRENT_USER\\" + ThemeKey, "AppsUseLightTheme", null);
                if (!(value is int))
                {
                    return false;
                }
                return (int)value == 0;
            }
            catch
            {
                // Unreadable value: light asset (never a startup failure).
                return false;
            }
        }

        /// <summary>
        /// Loads the theme asset as an independent icon. The manifest-resource stream
        /// is closed inside this call; the returned clone stays valid afterwards.
        /// </summary>
        public static Icon LoadApplicationIcon(bool darkTheme)
        {
            string name = darkTheme ? DarkResourceName : LightResourceName;
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null)
            {
                // A correct build always embeds both resources (B1 build flags);
                // there is no fallback icon to hide a broken build behind.
                throw new InvalidOperationException("icon resource missing: " + name);
            }
            using (stream)
            using (Icon source = new Icon(stream))
            {
                return (Icon)source.Clone();
            }
        }
    }
}
