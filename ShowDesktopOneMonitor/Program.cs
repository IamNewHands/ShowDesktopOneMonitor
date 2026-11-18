using FrigoTab;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    static class Program
    {
        /// <summary>
        /// Главная точка входа для приложения.
        /// </summary>
        [STAThread]
        static void Main ()
        {
            Diagnostics.Write("=== start  exe=" + Application.ExecutablePath
                + "  os=" + Environment.OSVersion.VersionString
                + "  clr=" + Environment.Version
                + "  x64process=" + Environment.Is64BitProcess
                + "  log=" + Diagnostics.LogPath);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Only one copy may run: a second launch would install a duplicate tray icon
            // and a duplicate keyboard hook, and both copies would fight over the same
            // per-monitor window state. The second copy says so and leaves.
            if (!SingleInstance.TryAcquire()) {
                Diagnostics.Write("=== second instance blocked, a copy is already running");
                MessageBox.Show(
                    Localization.AlreadyRunningMessage,
                    Localization.AlreadyRunningTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try {
                Application.Run(new MainAppContext());
                Diagnostics.Write("=== message loop returned normally");
            }
            catch (Exception ex) {
                Diagnostics.Write("FATAL: Application.Run threw", ex);
                throw;
            }
        }
    }
}
