using ShowDesktopOneMonitor.Properties;
using System;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    public enum HotkeyMode
    {
        WinShiftD = 0, // Win + Shift + D (默认)
        WinD = 1,      // Win + D (覆盖系统 Win+D)
        Both = 2       // 两者均支持 (Win+Shift+D 与 Win+D)
    }

    /// <summary>
    /// Util class for reading and writing to settings file
    /// </summary>
    static class SettingsManager
    {
        private static readonly Settings SETTINGS = Settings.Default;

        public static Keys ReadHotkey ()
        {
            return SETTINGS.HotKey;
        }
        public static KeyModifiers ReadKeyModifiers ()
        {
            KeyModifiers modifiers = SETTINGS.KeyModifiers[0];
            Array.ForEach(SETTINGS.KeyModifiers, m => modifiers |= m);
            return modifiers;
        }

        public static HotkeyMode ReadHotkeyMode ()
        {
            try {
                return (HotkeyMode)SETTINGS.HotkeyMode;
            }
            catch {
                return HotkeyMode.WinShiftD;
            }
        }

        public static void WriteHotkeyMode (HotkeyMode mode)
        {
            SETTINGS.HotkeyMode = (int)mode;
        }

        public static bool ReadLoggingEnabled ()
        {
            return SETTINGS.LoggingEnabled;
        }

        public static void WriteLoggingEnabled (bool enabled)
        {
            SETTINGS.LoggingEnabled = enabled;
        }

        public static bool ReadSuppressUnsteerableAnimation ()
        {
            return SETTINGS.SuppressUnsteerableAnimation;
        }

        public static void WriteSuppressUnsteerableAnimation (bool suppress)
        {
            SETTINGS.SuppressUnsteerableAnimation = suppress;
        }

        public static bool ReadMiddleClickMoveEnabled ()
        {
            return SETTINGS.MiddleClickMoveEnabled;
        }

        public static void WriteMiddleClickMoveEnabled (bool enabled)
        {
            SETTINGS.MiddleClickMoveEnabled = enabled;
        }

        public static string ReadLanguage ()
        {
            return SETTINGS.Language;
        }

        public static void WriteLanguage (string language)
        {
            SETTINGS.Language = language;
        }

        public static void Save () => SETTINGS.Save();

    }
}
