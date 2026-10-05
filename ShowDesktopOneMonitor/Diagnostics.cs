using System;
using System.IO;
using System.Text;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Append-only diagnostic log. Logging must never be able to break the app, so
    /// every failure here is swallowed.
    /// </summary>
    internal static class Diagnostics
    {
        private static readonly object Gate = new object();

        // Preferred location: right next to the executable, so the file is easy to find.
        // Falls back to %LOCALAPPDATA% when that directory is not writable (for example
        // when the app was extracted under Program Files).
        public static readonly string LogPath = ResolveLogPath();

        public static readonly string LogDirectory = Path.GetDirectoryName(LogPath);

        private static string ResolveLogPath ()
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(exeDirectory)) {
                string candidate = Path.Combine(exeDirectory, "log.txt");
                if (CanWrite(candidate)) {
                    return candidate;
                }
            }

            return Path.Combine(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShowDesktopOneMonitor"), "log.txt");
        }

        private static bool CanWrite (string path)
        {
            try {
                using (FileStream probe = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) {
                }
                return true;
            }
            catch (Exception) {
                return false;
            }
        }

        public static void Write (string message)
        {
            Write(message, null);
        }

        public static void Write (string message, Exception exception)
        {
            try {
                lock (Gate) {
                    Directory.CreateDirectory(LogDirectory);
                    StringBuilder line = new StringBuilder();
                    line.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    line.Append("  ");
                    line.Append(message);
                    if (exception != null) {
                        line.Append(Environment.NewLine);
                        line.Append("        ");
                        line.Append(exception.GetType().FullName);
                        line.Append(": ");
                        line.Append(exception.Message);
                        line.Append(Environment.NewLine);
                        line.Append(exception.StackTrace);
                    }
                    line.Append(Environment.NewLine);
                    File.AppendAllText(LogPath, line.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception) {
            }
        }
    }
}
