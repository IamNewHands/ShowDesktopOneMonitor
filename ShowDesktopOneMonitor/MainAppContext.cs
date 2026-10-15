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
            AutoStartup.CheckAndUpdatePathIfNeeded();

            Application.ThreadException += this.Application_ThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += this.CurrentDomain_UnhandledException;

            TitleBarMover.Enabled = SettingsManager.ReadMiddleClickMoveEnabled();

            Icon trayIconImage = LoadTrayIcon();
            Diagnostics.Write("tray icon image loaded: " + trayIconImage.Width + "x" + trayIconImage.Height);

            trayIcon = new NotifyIcon() {
                Icon = trayIconImage,
                Visible = true,
                Text = "Show Desktop Enhanced",
            };
            BuildTrayMenu();

            Diagnostics.Write("tray icon created, Visible=" + trayIcon.Visible);
            PrevStateByScreen = new List<DesktopWindowID>[Screen.AllScreens.Length];

            RegisterConfiguredHotkeys();

            HotKeyManager.HotKeyPressed += new EventHandler<HotKeyEventArgs>(OnHotkeyPressed);

            // Optional extra: middle-clicking a title bar moves that window to the next
            // monitor. Its own hook, isolated from the hot key hook.
            TitleBarMover.Enable();

            // Keeps the minimize/restore animation on the monitor the toggle runs on.
            // Installed here, on the UI thread, because the shell asks for the
            // animation rectangle while the message loop thread is inside ShowWindow.
            MinimizeAnimation.Enable();

            Diagnostics.Write("startup complete, waiting for the hot key");

            // Two independent signals, because neither one covers every update:
            //  - the updater hands "--updated" to the executable it restarts (v1.1.4 and
            //    later). Only the outgoing build can send it, so it is the one signal that
            //    can cover the update which installs the version record below;
            //  - the recorded version differs from the running one. That also covers the
            //    elevated fallback path in the updater, where the switch does not survive
            //    the command line, and any start the updater did not launch at all.
            bool restartedByUpdater = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--updated", StringComparison.OrdinalIgnoreCase));
            bool versionChanged = UpdateState.TakeJustUpdated();

            if (restartedByUpdater || versionChanged) {
                ShowAboutForm(justUpdated: true);
            }
        }

        private MenuItem _loggingItem;
        private MenuItem _animationItem;
        private MenuItem _middleClickItem;
        private MenuItem _hotkeyMenu;
        private MenuItem _hotkeyWinShiftDItem;
        private MenuItem _hotkeyWinDItem;
        private MenuItem _hotkeyBothItem;
        private MenuItem _autoStartMenu;
        private MenuItem _autoStartDisabledItem;
        private MenuItem _autoStartNormalItem;
        private MenuItem _autoStartAdminItem;
        private MenuItem _languageMenu;
        private MenuItem _langEnItem;
        private MenuItem _langZhItem;

        private void BuildTrayMenu ()
        {
            _loggingItem = new MenuItem(Localization.WriteLogFile, (s, e) => ToggleLogging((MenuItem)s));
            _animationItem = new MenuItem(Localization.NoAnimationWithoutTaskbarButton, (s, e) => ToggleUnsteerableAnimation((MenuItem)s));
            _middleClickItem = new MenuItem(Localization.MiddleClickMove, (s, e) => ToggleMiddleClickMove((MenuItem)s));

            _hotkeyWinShiftDItem = new MenuItem(Localization.HotkeyWinShiftD, (s, e) => SetHotkeyMode(HotkeyMode.WinShiftD));
            _hotkeyWinDItem = new MenuItem(Localization.HotkeyWinD, (s, e) => SetHotkeyMode(HotkeyMode.WinD));
            _hotkeyBothItem = new MenuItem(Localization.HotkeyBoth, (s, e) => SetHotkeyMode(HotkeyMode.Both));

            _hotkeyMenu = new MenuItem(Localization.HotkeyMenu, new MenuItem[] {
                _hotkeyWinShiftDItem,
                _hotkeyWinDItem,
                _hotkeyBothItem,
            });

            _autoStartDisabledItem = new MenuItem(Localization.AutoStartDisabled, (s, e) => SetAutoStart(AutoStartupMode.Disabled));
            _autoStartNormalItem = new MenuItem(Localization.AutoStartNormal, (s, e) => SetAutoStart(AutoStartupMode.Normal));
            _autoStartAdminItem = new MenuItem(Localization.AutoStartAdmin, (s, e) => SetAutoStart(AutoStartupMode.Administrator));

            _autoStartMenu = new MenuItem(Localization.StartWithWindows, new MenuItem[] {
                _autoStartDisabledItem,
                _autoStartNormalItem,
                _autoStartAdminItem,
            });

            _langEnItem = new MenuItem("English", (s, e) => ChangeLanguage("en"));
            _langZhItem = new MenuItem("简体中文", (s, e) => ChangeLanguage("zh-CN"));

            _languageMenu = new MenuItem(Localization.LanguageMenu, new MenuItem[] {
                _langEnItem,
                _langZhItem,
            });

            ContextMenu trayMenu = new ContextMenu(new MenuItem[] {
                new MenuItem(Localization.OpenLogFolder, (s, e) => OpenLogFolder()),
                _loggingItem,
                _animationItem,
                _middleClickItem,
                _hotkeyMenu,
                _autoStartMenu,
                _languageMenu,
                new MenuItem(Localization.About, (s, e) => ShowAboutForm()),
                new MenuItem("-"),
                new MenuItem(Localization.Exit, (s, e) => { trayIcon.Visible = false; Application.Exit(); }),
            });

            trayMenu.Popup += (s, e) => RefreshMenuState();

            trayIcon.ContextMenu = trayMenu;
            RefreshMenuState();
        }

        private AboutForm _aboutForm = null;

        private void ShowAboutForm (bool justUpdated = false)
        {
            if (_aboutForm == null || _aboutForm.IsDisposed) {
                _aboutForm = new AboutForm(justUpdated);
                _aboutForm.Show();
            }
            else {
                if (justUpdated) {
                    _aboutForm.SetUpdateSuccess();
                }
                _aboutForm.BringToFront();
                _aboutForm.Activate();
            }
        }

        private void RefreshMenuState ()
        {
            if (_loggingItem != null) _loggingItem.Checked = Diagnostics.Enabled;
            if (_animationItem != null) _animationItem.Checked = MinimizeAnimation.SuppressUnsteerableAnimation;
            if (_middleClickItem != null) _middleClickItem.Checked = TitleBarMover.Enabled;

            HotkeyMode hotkeyMode = SettingsManager.ReadHotkeyMode();
            if (_hotkeyWinShiftDItem != null) _hotkeyWinShiftDItem.Checked = (hotkeyMode == HotkeyMode.WinShiftD);
            if (_hotkeyWinDItem != null) _hotkeyWinDItem.Checked = (hotkeyMode == HotkeyMode.WinD);
            if (_hotkeyBothItem != null) _hotkeyBothItem.Checked = (hotkeyMode == HotkeyMode.Both);

            AutoStartupMode mode = AutoStartup.GetCurrentMode();
            if (_autoStartDisabledItem != null) _autoStartDisabledItem.Checked = (mode == AutoStartupMode.Disabled);
            if (_autoStartNormalItem != null) _autoStartNormalItem.Checked = (mode == AutoStartupMode.Normal);
            if (_autoStartAdminItem != null) _autoStartAdminItem.Checked = (mode == AutoStartupMode.Administrator);

            if (_langEnItem != null) _langEnItem.Checked = !Localization.IsChinese;
            if (_langZhItem != null) _langZhItem.Checked = Localization.IsChinese;
        }

        private readonly List<int> _registeredHotKeyIds = new List<int>();

        private void RegisterConfiguredHotkeys ()
        {
            foreach (int id in _registeredHotKeyIds) {
                HotKeyManager.UnregisterHotKey(id);
            }
            _registeredHotKeyIds.Clear();

            HotkeyMode mode = SettingsManager.ReadHotkeyMode();
            Diagnostics.Write("RegisterConfiguredHotkeys: mode=" + mode
                + "  screens=" + Screen.AllScreens.Length
                + "  autoStartup=" + AutoStartup.GetCurrentMode()
                + "  middleClick=" + TitleBarMover.Enabled
                + "  lang=" + Localization.CurrentLanguage);

            if (mode == HotkeyMode.WinShiftD || mode == HotkeyMode.Both) {
                int id = HotKeyManager.RegisterHotKey(Keys.D, KeyModifiers.Windows | KeyModifiers.Shift);
                _registeredHotKeyIds.Add(id);
                Diagnostics.Write("registered Win+Shift+D id=" + id);
            }

            if (mode == HotkeyMode.WinD || mode == HotkeyMode.Both) {
                int id = HotKeyManager.RegisterHotKey(Keys.D, KeyModifiers.Windows);
                _registeredHotKeyIds.Add(id);
                Diagnostics.Write("registered Win+D id=" + id);
            }
        }

        private void SetHotkeyMode (HotkeyMode mode)
        {
            SettingsManager.WriteHotkeyMode(mode);
            SettingsManager.Save();
            RegisterConfiguredHotkeys();
            RefreshMenuState();
            Diagnostics.Write("hotkey mode switched to " + mode);
        }

        private void ChangeLanguage (string lang)
        {
            Localization.CurrentLanguage = lang;
            SettingsManager.WriteLanguage(lang);
            SettingsManager.Save();
            BuildTrayMenu();
            if (_aboutForm != null && !_aboutForm.IsDisposed) {
                _aboutForm.Close();
                _aboutForm = new AboutForm();
                _aboutForm.Show();
            }
            Diagnostics.Write("language changed to " + lang);
        }

        private void SetAutoStart (AutoStartupMode targetMode)
        {
            AutoStartupMode current = AutoStartup.GetCurrentMode();
            if (current == targetMode && targetMode != AutoStartupMode.Disabled) {
                targetMode = AutoStartupMode.Disabled;
            }

            if (AutoStartup.SetMode(targetMode)) {
                Diagnostics.Write("auto-startup mode set to " + targetMode);
            }
            else {
                Diagnostics.Write("failed to set auto-startup mode to " + targetMode);
            }
            RefreshMenuState();
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

        // Tray menu switch for moving windows by middle-clicking title bars.
        private static void ToggleMiddleClickMove (MenuItem item)
        {
            bool enabled = !item.Checked;
            item.Checked = enabled;
            TitleBarMover.Enabled = enabled;
            SettingsManager.WriteMiddleClickMoveEnabled(enabled);
            SettingsManager.Save();
            Diagnostics.Write("middle-click title bar moving " + (enabled ? "enabled" : "disabled")
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