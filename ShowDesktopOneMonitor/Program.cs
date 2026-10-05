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
