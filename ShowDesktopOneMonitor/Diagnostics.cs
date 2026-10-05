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

        public static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShowDesktopOneMonitor");

        public static readonly string LogPath = Path.Combine(LogDirectory, "log.txt");

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
