using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    internal class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool HasUpdate { get; set; }
        public string RemoteVersion { get; set; }
        public string LocalVersion => "v" + BuildInfo.Version;
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Handles querying latest release from GitHub, downloading updates,
    /// and smoothly restarting the app with the newly installed executable.
    /// </summary>
    internal static class AppUpdateManager
    {
        public const string ReleaseLatestUrl = "https://github.com/IamNewHands/ShowDesktopOneMonitor/releases/latest";
        public const string DownloadUrl = "https://github.com/IamNewHands/ShowDesktopOneMonitor/releases/latest/download/ShowDesktopOneMonitor.exe";

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync ()
        {
            return await Task.Run(() =>
            {
                UpdateCheckResult result = new UpdateCheckResult();
                try {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string targetTag = null;

                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ReleaseLatestUrl);
                    req.Method = "HEAD";
                    req.UserAgent = "ShowDesktopOneMonitor-Updater";
                    req.AllowAutoRedirect = false;
                    req.Timeout = 10000;

                    try {
                        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                            string location = resp.Headers["Location"];
                            if (!string.IsNullOrEmpty(location)) {
                                Match m = Regex.Match(location, @"/releases/tag/([^/?#\s]+)", RegexOptions.IgnoreCase);
                                if (m.Success) {
                                    targetTag = m.Groups[1].Value;
                                }
                            }
                            if (string.IsNullOrEmpty(targetTag) && resp.ResponseUri != null) {
                                Match m = Regex.Match(resp.ResponseUri.AbsoluteUri, @"/releases/tag/([^/?#\s]+)", RegexOptions.IgnoreCase);
                                if (m.Success) {
                                    targetTag = m.Groups[1].Value;
                                }
                            }
                        }
                    }
                    catch (WebException wex) when (wex.Response is HttpWebResponse wr && ((int)wr.StatusCode >= 300 && (int)wr.StatusCode < 400)) {
                        string location = wr.Headers["Location"];
                        if (!string.IsNullOrEmpty(location)) {
                            Match m = Regex.Match(location, @"/releases/tag/([^/?#\s]+)", RegexOptions.IgnoreCase);
                            if (m.Success) {
                                targetTag = m.Groups[1].Value;
                            }
                        }
                    }

                    // Fallback: if HEAD failed or tag not found, download page HTML
                    if (string.IsNullOrEmpty(targetTag)) {
                        using (WebClient client = new WebClient()) {
                            client.Headers.Add("User-Agent", "ShowDesktopOneMonitor-Updater");
                            string html = client.DownloadString(ReleaseLatestUrl);
                            Match m = Regex.Match(html, @"/releases/tag/([^""'/?#\s]+)", RegexOptions.IgnoreCase);
                            if (m.Success) {
                                targetTag = m.Groups[1].Value;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(targetTag)) {
                        result.Success = false;
                        result.ErrorMessage = "Unable to locate latest release tag.";
                        return result;
                    }

                    result.RemoteVersion = targetTag.Trim();
                    Version vRemote = ParseVersion(result.RemoteVersion);
                    Version vLocal = ParseVersion(BuildInfo.Version);

                    if (vRemote != null && vLocal != null) {
                        result.HasUpdate = vRemote > vLocal;
                    }
                    else {
                        // Fallback comparison
                        result.HasUpdate = !string.Equals(result.RemoteVersion, "v" + BuildInfo.Version, StringComparison.OrdinalIgnoreCase)
                                        && !string.Equals(result.RemoteVersion, BuildInfo.Version, StringComparison.OrdinalIgnoreCase);
                    }

                    result.Success = true;
                }
                catch (Exception ex) {
                    Diagnostics.Write("CheckForUpdatesAsync failed", ex);
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
                return result;
            });
        }

        private static Version ParseVersion (string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            Match m = Regex.Match(tag, @"\d+(\.\d+)+");
            if (m.Success && Version.TryParse(m.Value, out Version v)) {
                return v;
            }
            return null;
        }

        public static void DownloadAndApplyUpdateAsync (Action<int> onProgress, Action<Exception> onError)
        {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string tempExe = Path.Combine(Path.GetTempPath(), "ShowDesktopOneMonitor_Update_" + Guid.NewGuid().ToString("N") + ".exe");

                WebClient client = new WebClient();
                client.Headers.Add("User-Agent", "ShowDesktopOneMonitor-Updater");

                client.DownloadProgressChanged += (s, e) =>
                {
                    onProgress?.Invoke(e.ProgressPercentage);
                };

                client.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Error != null) {
                        Diagnostics.Write("Download update failed", e.Error);
                        onError?.Invoke(e.Error);
                        try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                        return;
                    }

                    try {
                        FileInfo fi = new FileInfo(tempExe);
                        if (!fi.Exists || fi.Length < 30000) {
                            throw new InvalidOperationException("Downloaded file appears invalid or truncated (size=" + (fi.Exists ? fi.Length : 0) + ").");
                        }

                        ApplyUpdateAndRestart(tempExe);
                    }
                    catch (Exception ex) {
                        Diagnostics.Write("Apply update failed", ex);
                        onError?.Invoke(ex);
                        try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                    }
                };

                client.DownloadFileAsync(new Uri(DownloadUrl), tempExe);
            }
            catch (Exception ex) {
                Diagnostics.Write("DownloadAndApplyUpdateAsync startup failed", ex);
                onError?.Invoke(ex);
            }
        }

        public static void ApplyUpdateAndRestart (string newExePath)
        {
            string currentExe = Application.ExecutablePath;
            int currentPid = Process.GetCurrentProcess().Id;
            string batPath = Path.Combine(Path.GetTempPath(), "sde_updater_" + Guid.NewGuid().ToString("N") + ".bat");

            // Batch updater script:
            // 1. Waits for current process to exit.
            // 2. Overwrites current executable with downloaded executable.
            //    If denied, attempts elevated move with powershell.
            // 3. Launches the newly updated executable.
            // 4. Deletes itself.
            string batContent = "@echo off\r\n"
                + ":wait_loop\r\n"
                + "tasklist /fi \"PID eq " + currentPid + "\" | find \"" + currentPid + "\" >nul\r\n"
                + "if not errorlevel 1 (\r\n"
                + "    ping 127.0.0.1 -n 2 >nul\r\n"
                + "    goto wait_loop\r\n"
                + ")\r\n"
                + "move /y \"" + newExePath + "\" \"" + currentExe + "\" >nul\r\n"
                + "if errorlevel 1 (\r\n"
                + "    powershell -NoProfile -Command \"Start-Process cmd -ArgumentList '/c move /y \\\"\"" + newExePath + "\\\"\" \\\"\"" + currentExe + "\\\"\" & start \\\"\"\\\"\" \\\"\"" + currentExe + "\\\"\" --updated' -Verb RunAs\"\r\n"
                + ") else (\r\n"
                + "    start \"\" \"" + currentExe + "\" --updated\r\n"
                + ")\r\n"
                + "del \"%~f0\"\r\n";

            File.WriteAllText(batPath, batContent, Encoding.Default);
            Diagnostics.Write("AppUpdateManager: updater batch created at " + batPath + ", exiting to apply update");

            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = "/c \"" + batPath + "\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            Application.Exit();
        }
    }
}
