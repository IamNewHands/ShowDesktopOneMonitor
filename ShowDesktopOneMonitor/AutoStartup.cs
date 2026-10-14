using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    public enum AutoStartupMode
    {
        Disabled,
        Normal,
        Administrator
    }

    /// <summary>
    /// Manages auto-startup at Windows logon.
    /// Supports two modes:
    /// 1. Normal privilege: standard HKCU Run registry key. Runs asInvoker without UAC.
    /// 2. Administrator privilege: Windows Task Scheduler with HighestAvailable privileges.
    ///    Runs at logon with full administrative rights without popping any UAC prompt.
    /// </summary>
    internal static class AutoStartup
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string TaskName = "ShowDesktopOneMonitor";

        /// <summary>
        /// Returns the current active auto-startup mode.
        /// Priority: Administrator (Task Scheduler) > Normal (Registry) > Disabled.
        /// </summary>
        public static AutoStartupMode GetCurrentMode ()
        {
            if (IsTaskScheduled()) {
                return AutoStartupMode.Administrator;
            }
            if (IsRegistryEnabled()) {
                return AutoStartupMode.Normal;
            }
            return AutoStartupMode.Disabled;
        }

        public static bool IsEnabled ()
        {
            return GetCurrentMode() != AutoStartupMode.Disabled;
        }

        /// <summary>
        /// Switches the auto-startup mode.
        /// </summary>
        public static bool SetMode (AutoStartupMode mode)
        {
            try {
                switch (mode) {
                    case AutoStartupMode.Disabled:
                        DeleteRegistryRun();
                        DeleteScheduledTask();
                        return true;

                    case AutoStartupMode.Normal:
                        DeleteScheduledTask();
                        return SetRegistryRun();

                    case AutoStartupMode.Administrator:
                        DeleteRegistryRun();
                        return CreateScheduledTask();

                    default:
                        return false;
                }
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.SetMode(" + mode + ") failed", ex);
                return false;
            }
        }

        public static void CheckAndUpdatePathIfNeeded ()
        {
            // Only update normal registry path silently without triggering UAC
            try {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)) {
                    if (runKey == null) {
                        return;
                    }
                    object val = runKey.GetValue(TaskName);
                    if (val == null) {
                        return;
                    }

                    string registered = val.ToString().Trim('\"');
                    if (string.Equals(registered, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) {
                        return;
                    }

                    if (registered.EndsWith("ShowDesktopOneMonitor.exe", StringComparison.OrdinalIgnoreCase)) {
                        runKey.SetValue(TaskName, "\"" + Application.ExecutablePath + "\"");
                        Diagnostics.Write("AutoStartup: updated registry executable path from " + registered
                            + " to " + Application.ExecutablePath);
                    }
                }
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.CheckAndUpdatePathIfNeeded failed", ex);
            }
        }

        // ---- Registry (Normal privilege) ----

        private static bool IsRegistryEnabled ()
        {
            try {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false)) {
                    if (runKey == null) {
                        return false;
                    }
                    object val = runKey.GetValue(TaskName);
                    if (val == null) {
                        return false;
                    }

                    string path = val.ToString().Trim('\"');
                    if (!string.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) {
                        return false;
                    }
                }

                using (RegistryKey approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, false)) {
                    if (approvedKey != null) {
                        object val = approvedKey.GetValue(TaskName);
                        if (val is byte[] bytes && bytes.Length > 0) {
                            if ((bytes[0] & 1) != 0) {
                                return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.IsRegistryEnabled failed", ex);
                return false;
            }
        }

        private static bool SetRegistryRun ()
        {
            try {
                using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath)) {
                    if (runKey == null) {
                        return false;
                    }
                    runKey.SetValue(TaskName, "\"" + Application.ExecutablePath + "\"");
                }

                try {
                    using (RegistryKey approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true)) {
                        if (approvedKey != null) {
                            object val = approvedKey.GetValue(TaskName);
                            if (val is byte[] bytes && bytes.Length > 0) {
                                bytes[0] = 2;
                                approvedKey.SetValue(TaskName, bytes, RegistryValueKind.Binary);
                            }
                        }
                    }
                }
                catch (Exception) {
                }

                return true;
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.SetRegistryRun failed", ex);
                return false;
            }
        }

        private static void DeleteRegistryRun ()
        {
            try {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)) {
                    if (runKey != null) {
                        runKey.DeleteValue(TaskName, false);
                    }
                }
                using (RegistryKey approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true)) {
                    if (approvedKey != null) {
                        approvedKey.DeleteValue(TaskName, false);
                    }
                }
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.DeleteRegistryRun failed", ex);
            }
        }

        // ---- Task Scheduler (Administrator privilege, bypasses UAC at logon) ----

        private static bool IsTaskScheduled ()
        {
            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = "schtasks.exe",
                    Arguments = "/query /tn \"" + TaskName + "\" /xml",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process p = Process.Start(psi)) {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0 || string.IsNullOrEmpty(output)) {
                        return false;
                    }

                    // Confirm the scheduled command matches our executable
                    return output.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.IsTaskScheduled check failed", ex);
                return false;
            }
        }

        private static bool CreateScheduledTask ()
        {
            string tempXml = Path.Combine(Path.GetTempPath(), "ShowDesktopOneMonitor_Task_" + Guid.NewGuid().ToString("N") + ".xml");
            try {
                string escapedPath = SecurityElement.Escape(Application.ExecutablePath);
                string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                    + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                    + "  <RegistrationInfo>\n"
                    + "    <Description>ShowDesktopOneMonitor Auto-Start (Elevated)</Description>\n"
                    + "  </RegistrationInfo>\n"
                    + "  <Triggers>\n"
                    + "    <LogonTrigger>\n"
                    + "      <Enabled>true</Enabled>\n"
                    + "    </LogonTrigger>\n"
                    + "  </Triggers>\n"
                    + "  <Principals>\n"
                    + "    <Principal>\n"
                    + "      <RunLevel>HighestAvailable</RunLevel>\n"
                    + "    </Principal>\n"
                    + "  </Principals>\n"
                    + "  <Settings>\n"
                    + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n"
                    + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n"
                    + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n"
                    + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n"
                    + "    <Priority>7</Priority>\n"
                    + "  </Settings>\n"
                    + "  <Actions>\n"
                    + "    <Exec>\n"
                    + "      <Command>" + escapedPath + "</Command>\n"
                    + "    </Exec>\n"
                    + "  </Actions>\n"
                    + "</Task>";

                File.WriteAllText(tempXml, xml, Encoding.Unicode);

                bool success = RunSchtasks("/create /tn \"" + TaskName + "\" /xml \"" + tempXml + "\" /f", elevated: true);
                Diagnostics.Write("AutoStartup: create scheduled task with highest privileges " + (success ? "succeeded" : "failed"));
                return success;
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.CreateScheduledTask failed", ex);
                return false;
            }
            finally {
                try {
                    if (File.Exists(tempXml)) {
                        File.Delete(tempXml);
                    }
                }
                catch (Exception) {
                }
            }
        }

        private static bool DeleteScheduledTask ()
        {
            // First check if the task actually exists before trying to delete
            if (!IsTaskScheduled()) {
                return true;
            }

            // Try non-elevated first
            if (RunSchtasks("/delete /tn \"" + TaskName + "\" /f", elevated: false)) {
                return true;
            }

            // If non-elevated fails, delete with elevation
            return RunSchtasks("/delete /tn \"" + TaskName + "\" /f", elevated: true);
        }

        private static bool RunSchtasks (string arguments, bool elevated)
        {
            try {
                ProcessStartInfo psi = new ProcessStartInfo {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = elevated,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (elevated) {
                    psi.Verb = "runas";
                }
                else {
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                }

                using (Process p = Process.Start(psi)) {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (System.ComponentModel.Win32Exception ex) {
                // User clicked "No" or cancelled UAC prompt (1223 = ERROR_CANCELLED)
                Diagnostics.Write("AutoStartup.RunSchtasks: UAC cancelled or Win32 error " + ex.NativeErrorCode);
                return false;
            }
            catch (Exception ex) {
                Diagnostics.Write("AutoStartup.RunSchtasks exception", ex);
                return false;
            }
        }
    }
}
