using FrigoTab;
using ShowDesktopOneMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    public class MainAppContext : ApplicationContext
    {
        private NotifyIcon trayIcon = null;
        private List<DesktopWindowID>[] PrevStateByScreen = new List<DesktopWindowID>[0];

        public MainAppContext ()
        {
            // The log can be switched off from the tray menu; honour that before the
            // first line is written.
            Diagnostics.Enabled = SettingsManager.ReadLoggingEnabled();
            MinimizeAnimation.SuppressUnsteerableAnimation = SettingsManager.ReadSuppressUnsteerableAnimation();

            Application.ThreadException += this.Application_ThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += this.CurrentDomain_UnhandledException;

            Icon trayIconImage = LoadTrayIcon();
            Diagnostics.Write("tray icon image loaded: " + trayIconImage.Width + "x" + trayIconImage.Height);

            // Checkable tray item: the log writes a lot while debugging a monitor setup,
            // and it can be silenced completely without restarting the app.
            MenuItem loggingItem = new MenuItem("Write log file", (s, e) => ToggleLogging((MenuItem)s));
            loggingItem.Checked = Diagnostics.Enabled;

            // Off by default: skipping the animation of a window Windows will not aim broke
            // the secondary screen on the machine this was tried on, so it stays a switch
            // until that is understood.
            MenuItem animationItem = new MenuItem("No animation without a taskbar button",
                (s, e) => ToggleUnsteerableAnimation((MenuItem)s));
            animationItem.Checked = MinimizeAnimation.SuppressUnsteerableAnimation;

            trayIcon = new NotifyIcon() {
                Icon = trayIconImage,
                ContextMenu = new ContextMenu(new MenuItem[] {
                    new MenuItem("Open log folder", (s, e) => OpenLogFolder()),
                    loggingItem,
                    animationItem,
                    new MenuItem("-"),
                    new MenuItem("Exit", (s, e) => {trayIcon.Visible = false; Application.Exit(); }),
                }),
                Visible = true,
                Text = "Show Desktop Enhanced",
            };
            Diagnostics.Write("tray icon created, Visible=" + trayIcon.Visible);
            PrevStateByScreen = new List<DesktopWindowID>[Screen.AllScreens.Length];

            Keys hotKey = SettingsManager.ReadHotkey();
            KeyModifiers keyModifiers = SettingsManager.ReadKeyModifiers();
            Diagnostics.Write("configured hot key: key=" + hotKey + " modifiers=" + keyModifiers
                + "  screens=" + Screen.AllScreens.Length);

            int primaryHotKeyId = HotKeyManager.RegisterHotKey(hotKey, keyModifiers);
            Diagnostics.Write("registered hot key id=" + primaryHotKeyId);

            // Take over Win+D natively (overriding the shell's show-desktop) so the
            // same action fires without an extra remapping layer. Two keyboard hooks
            // swallowing and injecting keys around each other desync the Win key
            // state and cause the stuck-Win-key "ghost press".
            if (!(hotKey == Keys.D && keyModifiers == KeyModifiers.Windows)) {
                int winDId = HotKeyManager.RegisterHotKey(Keys.D, KeyModifiers.Windows);
                Diagnostics.Write("registered Win+D id=" + winDId);
            }
            else {
                Diagnostics.Write("Win+D already covered by the configured hot key");
            }

            HotKeyManager.HotKeyPressed += new EventHandler<HotKeyEventArgs>(OnHotkeyPressed);

            // Optional extra: middle-clicking a title bar moves that window to the next
            // monitor. Its own hook, isolated from the hot key hook.
            TitleBarMover.Enable();

            // Keeps the minimize/restore animation on the monitor the toggle runs on.
            // Installed here, on the UI thread, because the shell asks for the
            // animation rectangle while the message loop thread is inside ShowWindow.
            MinimizeAnimation.Enable();

            Diagnostics.Write("startup complete, waiting for the hot key");
        }

        // Tray menu switch for the diagnostic log. The state goes into the settings file,
        // so a silenced log stays silenced across restarts.
        private static void ToggleLogging (MenuItem item)
        {
            bool enabled = !item.Checked;
            item.Checked = enabled;
            Diagnostics.Enabled = enabled;
            SettingsManager.WriteLoggingEnabled(enabled);
            SettingsManager.Save();
            Diagnostics.Write("diagnostic log " + (enabled ? "enabled" : "disabled") + " from the tray menu");
        }

        // Tray menu switch for skipping the animation of windows Windows will not aim.
        private static void ToggleUnsteerableAnimation (MenuItem item)
        {
            bool enabled = !item.Checked;
            item.Checked = enabled;
            MinimizeAnimation.SuppressUnsteerableAnimation = enabled;
            SettingsManager.WriteSuppressUnsteerableAnimation(enabled);
            SettingsManager.Save();
            Diagnostics.Write("no-animation for button-less windows " + (enabled ? "enabled" : "disabled")
                + " from the tray menu");
        }

        private static void OpenLogFolder ()
        {
            try {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + Diagnostics.LogPath + "\"");
            }
            catch (Exception ex) {
                Diagnostics.Write("OpenLogFolder failed", ex);
            }
        }

        // The tray icon is taken from the executable's own icon (set through
        // ApplicationIcon) instead of Resources.resx. The old binary-serialized
        // resx cannot be processed by the SDK resource task without a VS task host.
        private static Icon LoadTrayIcon ()
        {
            try {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) {
                    return icon;
                }
            }
            catch (Exception) {
            }
            return SystemIcons.Application;
        }

        private void OnHotkeyPressed(object sender, HotKeyEventArgs e)
        {
            Diagnostics.Write("hot key fired: key=" + e.Key + " modifiers=" + e.Modifiers);
            OnShowDesktopKeyComb();
        }

        private void OnShowDesktopKeyComb ()
        {
            Console.WriteLine("Toogling hide windows on screen...");

            // 1. Get current screen
            Screen currentScreen = Screen.FromPoint(Cursor.Position);
            int screenIdx = Array.IndexOf(Screen.AllScreens, currentScreen);

            Diagnostics.Write("toggle: screens=" + Screen.AllScreens.Length
                + " cursor=" + Cursor.Position.X + "," + Cursor.Position.Y
                + " screen=" + currentScreen.DeviceName
                + " screenIdx=" + screenIdx);

            // 2. Get windows on selected screen
            List<WindowHandle> windows = GetWindowsOnScreen(currentScreen);

            // 3. Has the list changed from the previous list ?
            List<DesktopWindowID> newWindowIDs = ConvertWindowsToIDs(windows);

            // screen number may have changed
            Array.Resize(ref PrevStateByScreen, Screen.AllScreens.Length);

            // restore if all windows are minimized AND prev state differs only by windows style
            if (newWindowIDs.All(x => x.WindowStyle != WindowStyles.Visible) && PrevStateByScreen[screenIdx] != null 
                                        && DoesPrevStateDiffersOnlyByWindowsStyle(newWindowIDs, screenIdx)) {
                restoreAllWindows(screenIdx);
            }
            else {
                minimizeAllWindows(newWindowIDs, screenIdx);
            }           
        }
        // Toggles one window and logs it. Windows only asks for the animation rectangle of
        // a window that has a taskbar button; for a window without one it animates to a
        // fallback spot of its own, which on a multi-monitor desktop reads as the animation
        // sliding sideways. Skipping the animation of such a window is available from the
        // tray menu, but off by default - see MinimizeAnimation.SuppressUnsteerableAnimation.
        private static void ToggleWindow (DesktopWindowID window, bool minimize)
        {
            bool steerable = !MinimizeAnimation.HasNoTaskbarButton(window.WindowHandle);
            bool skipAnimation = !steerable && MinimizeAnimation.SuppressUnsteerableAnimation;

            string note = "";
            if (!steerable) {
                note = skipAnimation
                    ? "  [no taskbar button: Windows never asks, animation skipped]"
                    : "  [no taskbar button: Windows never asks]";
            }
            Diagnostics.Write((minimize ? "minimizing: " : "restoring: ")
                + MinimizeAnimation.DescribeWindow(window.WindowHandle) + note);

            if (skipAnimation) {
                MinimizeAnimation.SetAnimationSuppressed(window.WindowHandle, true);
            }

            if (minimize) {
                window.SourceHandleObj.SetMinimizeWindow();
            }
            else {
                window.SourceHandleObj.SetRestoreWindow();
            }

            if (skipAnimation) {
                MinimizeAnimation.SetAnimationSuppressed(window.WindowHandle, false);
            }
        }

        private void minimizeAllWindows (List<DesktopWindowID> windowList, int screenIdx)
        {
            MinimizeAnimation.RedirectFor(Screen.AllScreens[screenIdx]);

            //int count = 0;
            // sort by ZOrder to restore windows in reverse order
            windowList = windowList.Select(x => new { window = x, zOrder = WindowApi.GetWindowZOrder(x.WindowHandle) })
                                                            .OrderByDescending(x => x.zOrder).Select(x => x.window).ToList();
            foreach (var window in windowList) {
                if (window.WindowStyle == WindowStyles.Visible) {
                    ToggleWindow(window, minimize: true);

                    //count++;
                }
            }

            PrevStateByScreen[screenIdx] = windowList;
            Diagnostics.Write("minimized: " + windowList.Count + " window(s) on screen " + screenIdx);
            //Console.WriteLine($"Minimizing {count} windows!");
        }
        private void restoreAllWindows (int screenIdx)
        {
            //int count = 0;
            if (PrevStateByScreen[screenIdx] != null) {
                MinimizeAnimation.RedirectFor(Screen.AllScreens[screenIdx]);

                foreach (var window in PrevStateByScreen[screenIdx].Reverse<DesktopWindowID>()) {
                    if (window.WindowStyle == WindowStyles.Visible) {
                        ToggleWindow(window, minimize: false);

                        //count++;
                    }
                }
            }

            PrevStateByScreen[screenIdx] = null;
            Diagnostics.Write("restored windows on screen " + screenIdx);
            //Console.WriteLine($"Restoring {count} windows!");
        }

        private List<WindowHandle> GetWindowsOnScreen (Screen screen)
        {
            WindowFinder finder = new WindowFinder();

            var windows = finder.Windows.Where(x => x.GetScreen().Equals(screen)).ToList();
            return windows;
        }
        private List<DesktopWindowID> ConvertWindowsToIDs (List<WindowHandle> windows)
        {
            List<DesktopWindowID> list = new List<DesktopWindowID>(windows.Count);
            for (int i = 0; i < windows.Count; i++) {
                var window = windows[i];
                list.Add(new DesktopWindowID(window));
            }
            return list;
        }
        private bool DoesPrevStateDiffersOnlyByWindowsStyle (List<DesktopWindowID> newList, int screenIdx)
        {
            if (PrevStateByScreen[screenIdx] == null) return false; // prev state is null
            if (newList.Count != PrevStateByScreen[screenIdx].Count) return false; // count differs
            if (false == newList.All(x => PrevStateByScreen[screenIdx].Contains(x))) return false; // windows differ

            // return true if style differs
            return false == newList.All(x => PrevStateByScreen[screenIdx].First(y => y == x).WindowStyle.Equals(x.WindowStyle));
        }

        protected override void ExitThreadCore ()
        {
            Diagnostics.Write("exit requested");
            trayIcon.Visible = false;
            trayIcon.Dispose();
            MinimizeAnimation.Disable();

            // Saving from a finalizer could throw on the finalizer thread and kill
            // the process; do it here instead, and never let it break the shutdown.
            try {
                SettingsManager.Save();
            }
            catch (Exception) {
            }

            base.ExitThreadCore();
        }

        private void Application_ThreadException (object sender, ThreadExceptionEventArgs e)
        {
            Diagnostics.Write("Application.ThreadException", e.Exception);
            MessageBox.Show("Необработанное исключение: " + e.Exception.ToString(), "Show Desktop Enhanced", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void CurrentDomain_UnhandledException (object sender, UnhandledExceptionEventArgs e)
        {
            Diagnostics.Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            MessageBox.Show($"Необработанное исключение: {e.ExceptionObject as Exception}", "Show Desktop Enhanced", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    public class DesktopWindowID : IEquatable<DesktopWindowID>
    {
        public WindowHandle SourceHandleObj;

        public IntPtr WindowHandle = IntPtr.Zero;
        public WindowStyles WindowStyle = WindowStyles.Disabled;

        public DesktopWindowID (WindowHandle sourceHandleObj)
        {
            this.SourceHandleObj = sourceHandleObj;

            this.WindowHandle = this.SourceHandleObj.GetHandle();

            this.WindowStyle = WindowStyles.Disabled;
            var wndStyle = this.SourceHandleObj.GetWindowStyles();

            if (wndStyle.HasFlag(WindowStyles.Minimize))
                this.WindowStyle = WindowStyles.Minimize;
            else if (wndStyle.HasFlag(WindowStyles.Visible))
                this.WindowStyle = WindowStyles.Visible;
        }

        public bool Equals (DesktopWindowID other)
        {
            return Equals((object)other);
        }
        public override int GetHashCode ()
        {
            return this.WindowHandle.GetHashCode();
        }
        public override bool Equals (object obj)
        {
            if (obj == null) return false;
            return this.GetHashCode() == obj.GetHashCode();
        }
        public static bool operator ==(DesktopWindowID obj1, DesktopWindowID obj2)
        {
            if (ReferenceEquals(obj1, null)) {
                return ReferenceEquals(obj2, null);
            }
            return obj1.Equals(obj2);
        }
        public static bool operator !=(DesktopWindowID obj1, DesktopWindowID obj2)
        {
            return (obj1 == obj2) == false;
        }
    }
}