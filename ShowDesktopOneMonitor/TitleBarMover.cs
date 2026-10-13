using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Middle-clicking a window's title bar moves that window to the next monitor
    /// (like DisplayFusion's "move window to next monitor").
    ///
    /// Only the caption area reacts, so middle-click keeps its normal meaning
    /// everywhere else - opening a link in a new tab, closing a tab, autoscroll,
    /// paste. With a single monitor nothing is ever swallowed: the click is passed
    /// through untouched and only a log line is written.
    ///
    /// The decision (which window, which target screen) is taken synchronously inside
    /// the hook because the click has to be swallowed right there, but it only uses
    /// cheap window queries. The move itself and all logging happen asynchronously on
    /// the message loop: doing file I/O inside a low-level hook can make Windows drop
    /// the hook on timeout.
    /// </summary>
    internal static class TitleBarMover
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_NCMBUTTONDOWN = 0x00A7;
        private const int WM_NCMBUTTONUP = 0x00A8;
        private const int WM_TITLEBAR_MIDDLE_CLICK = 0x8000 + 1; // WM_APP + 1

        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int SM_CYCAPTION = 4;
        private const int SM_CYSIZEFRAME = 32;
        private const int SM_CXPADDEDBORDER = 92;

        private const int GA_ROOT = 2;
        private const int SW_RESTORE = 9;
        private const int SW_MAXIMIZE = 3;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // How long after a maximized move the outcome is read back. The immediate read is
        // useless: Windows animates the maximize and reports the rectangle it currently
        // draws, which is how "265x56" got into the log while the window ended up fine.
        private const int SettleMilliseconds = 900;

        // How far a window's size may differ from the one that was asked for and still count
        // as "nobody adjusted it". Sizes come back exactly, positions do not - applications
        // inset their own frame - so this only compares width and height.
        private const int SizeTolerance = 2;

        // The move converts a window's size between the scale factors of two monitors, so
        // it needs the DPI of a monitor, not the DPI a window reports.
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const int MDT_EFFECTIVE_DPI = 0;
        private const uint DefaultDpi = 96;

        private delegate IntPtr LowLevelMouseProc (int nCode, IntPtr wParam, IntPtr lParam);

        private static readonly LowLevelMouseProc _hookProcDelegate = HookProc;
        private static readonly uint _ownProcessId = GetCurrentProcessId();

        private static volatile MessageWindow _messageWindow;
        private static volatile IntPtr _hwnd;
        private static readonly ManualResetEvent _windowReadyEvent = new ManualResetEvent(false);

        private static IntPtr _hook = IntPtr.Zero;
        private static bool _swallowButtonUp;

        static TitleBarMover ()
        {
            Thread messageLoop = new Thread(delegate ()
            {
                Application.Run(new MessageWindow());
            });
            messageLoop.Name = "TitleBarMoverThread";
            messageLoop.IsBackground = true;
            messageLoop.Start();
        }

        public static void Enable ()
        {
            _windowReadyEvent.WaitOne();
            if (_messageWindow == null) {
                Diagnostics.Write("TitleBarMover: the message window did not start, middle-click moving is unavailable");
                return;
            }
            Diagnostics.Write("TitleBarMover: ready (hook " + _hook + "), screens=" + Screen.AllScreens.Length);
        }

        // ---- hook ----

        private static IntPtr HookProc (int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0) {
                int message = wParam.ToInt32();

                if (message == WM_MBUTTONDOWN || message == WM_NCMBUTTONDOWN) {
                    MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                    IntPtr hwnd;
                    int targetIndex;
                    if (TryResolveTarget(new Point(data.pt.x, data.pt.y), out hwnd, out targetIndex)) {
                        // Report the decision; the move and the log happen off the hook.
                        PostMessage(_hwnd, WM_TITLEBAR_MIDDLE_CLICK, hwnd, (IntPtr)targetIndex);

                        // Swallow the click only when the window will really move;
                        // with one monitor it has to stay a normal middle-click.
                        if (targetIndex >= 0) {
                            _swallowButtonUp = true;
                            return (IntPtr)1;
                        }
                    }
                }
                else if (message == WM_MBUTTONUP || message == WM_NCMBUTTONUP) {
                    if (_swallowButtonUp) {
                        _swallowButtonUp = false;
                        return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Cheap synchronous hit test: is there a movable window whose caption is under
        /// this point, and which screen would it move to (-1 when there is only one)?
        /// </summary>
        private static bool TryResolveTarget (Point screenPoint, out IntPtr root, out int targetIndex)
        {
            root = IntPtr.Zero;
            targetIndex = -1;

            POINT point = new POINT();
            point.x = screenPoint.X;
            point.y = screenPoint.Y;

            IntPtr hit = WindowFromPoint(point);
            if (hit == IntPtr.Zero) {
                return false;
            }

            IntPtr candidate = GetAncestor(hit, GA_ROOT);
            if (candidate == IntPtr.Zero) {
                candidate = hit;
            }

            if (!IsMovableWindow(candidate)) {
                return false;
            }
            if (!IsPointOnCaption(candidate, screenPoint)) {
                return false;
            }

            Screen[] screens = Screen.AllScreens;
            int sourceIndex = Array.IndexOf(screens, Screen.FromHandle(candidate));
            if (sourceIndex < 0) {
                return false;
            }

            root = candidate;
            targetIndex = screens.Length > 1 ? (sourceIndex + 1) % screens.Length : -1;
            return true;
        }

        private static bool IsMovableWindow (IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == GetDesktopWindow()) {
                return false;
            }
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) {
                return false;
            }

            switch (GetClassNameOf(hwnd)) {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "TaskListThumbnailWnd":
                    return false;
            }

            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId == _ownProcessId) {
                return false;
            }

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) {
                return false;
            }
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }

        private static bool IsPointOnCaption (IntPtr hwnd, Point screenPoint)
        {
            RECT bounds;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf(typeof(RECT))) != 0
                || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) {
                if (!GetWindowRect(hwnd, out bounds)) {
                    return false;
                }
            }

            if (screenPoint.X < bounds.Left || screenPoint.X >= bounds.Right) {
                return false;
            }

            int captionHeight = CaptionHeight(hwnd);
            return screenPoint.Y >= bounds.Top && screenPoint.Y < bounds.Top + captionHeight;
        }

        /// <summary>
        /// Caption height in physical pixels for the DPI of the window's own monitor,
        /// so a mixed-DPI multi-monitor setup hit-tests correctly on every screen.
        /// </summary>
        private static int CaptionHeight (IntPtr hwnd)
        {
            int dpi = 96;
            try {
                dpi = GetDpiForWindow(hwnd);
            }
            catch (EntryPointNotFoundException) {
            }
            if (dpi <= 0) {
                dpi = 96;
            }

            try {
                return GetSystemMetricsForDpi(SM_CYCAPTION, (uint)dpi)
                    + GetSystemMetricsForDpi(SM_CYSIZEFRAME, (uint)dpi)
                    + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, (uint)dpi);
            }
            catch (EntryPointNotFoundException) {
                return GetSystemMetrics(SM_CYCAPTION)
                    + GetSystemMetrics(SM_CYSIZEFRAME)
                    + GetSystemMetrics(SM_CXPADDEDBORDER);
            }
        }

        // ---- move ----

        private static PlannedMove MoveToScreen (IntPtr hwnd, int targetIndex)
        {
            Screen[] screens = Screen.AllScreens;
            if (targetIndex < 0 || targetIndex >= screens.Length) {
                return null;
            }
            if (!IsWindow(hwnd)) {
                return null;
            }

            PlannedMove planned = new PlannedMove();
            planned.Window = hwnd;
            planned.TargetIndex = targetIndex;

            bool wasMaximized = IsZoomed(hwnd);
            Screen source = Screen.FromHandle(hwnd);
            Screen target = screens[targetIndex];
            Rectangle sourceArea = source.WorkingArea;
            Rectangle targetArea = target.WorkingArea;

            // The monitors of a mixed-scale desktop do not share a pixel grid, and the two
            // scale factors are handled in two very different ways depending on the
            // application - so both are measured instead of assumed:
            //
            // A per-monitor-DPI-aware application is sent WM_DPICHANGED by Windows with a
            // rectangle that keeps the size the user sees, and it applies that rectangle
            // (Explorer, Chrome, QQ, 微信 all behave this way). Converting the size here as
            // well applies the factor twice: a 2560 px wide window came back 3413 px wide
            // (= x 4/3) and stuck out of the 3200 px monitor onto the next one
            // (log.txt 2026-10-13 10:37).
            //
            // An application that keeps its own size - NetEase's UU远程 pins its windows to
            // their own aspect ratio - ignores that rectangle and keeps its physical pixels
            // (log.txt 2026-10-13 09:25: 1632x1080 on both monitors). For those the size has
            // to be converted here, and the settle check does that once it has seen that
            // nothing else adjusted the window.
            uint sourceDpi = DpiOfScreen(source);
            uint targetDpi = DpiOfScreen(target);

            // A maximized window ignores SetWindowPos, and it ignores a placement that would
            // move it to another monitor as well: SetWindowPlacement with a restore rectangle
            // on the target monitor returns true and leaves the window exactly where it was
            // (measured on this machine, log.txt 2026-10-13 10:25). Restoring it, moving it
            // and maximizing it again is the only sequence that moves it - and it is the one
            // Windows itself uses for Win+Shift+Arrow.
            if (wasMaximized) {
                ShowWindow(hwnd, SW_RESTORE);
            }

            // The size to carry over is measured here, on the monitor the window really is
            // on, in that monitor's physical pixels. A stored rectangle can belong to the
            // other monitor's scale factor, and scaling such a value again shrinks or grows
            // the window on every move - the 1632x1080 -> 1224x810 -> 960x608 series in
            // log.txt 2026-10-13 10:25 - so it is never trusted.
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) {
                Diagnostics.Write("TitleBarMover: " + Describe(hwnd) + " has no readable rectangle, not moved");
                return null;
            }

            int sourceWidth = rect.Right - rect.Left;
            int sourceHeight = rect.Bottom - rect.Top;
            if (sourceWidth <= 0 || sourceHeight <= 0) {
                return null;
            }

            // The size that is carried over is the measured one, untouched. Only a move
            // between monitors with the same scale factor gets the fit clamp, because only
            // there is the size not going to be adjusted by anybody else.
            bool sameScale = sourceDpi == targetDpi;
            int width = sameScale ? Math.Min(sourceWidth, targetArea.Width) : sourceWidth;
            int height = sameScale ? Math.Min(sourceHeight, targetArea.Height) : sourceHeight;

            // The size that keeps what the user sees, used when nothing else adjusts the
            // window - see the settle check. Never larger than the monitor it is going to:
            // a position clamp alone would leave the far edge off that screen.
            int wantedWidth = Math.Min(ScaleToDpi(sourceWidth, sourceDpi, targetDpi), targetArea.Width);
            int wantedHeight = Math.Min(ScaleToDpi(sourceHeight, sourceDpi, targetDpi), targetArea.Height);

            // Keep the relative position inside the source work area, like Win+Shift+Arrow does.
            double relativeX = sourceArea.Width > 0 ? (rect.Left - sourceArea.Left) / (double)sourceArea.Width : 0.0;
            double relativeY = sourceArea.Height > 0 ? (rect.Top - sourceArea.Top) / (double)sourceArea.Height : 0.0;

            int x = targetArea.Left + (int)Math.Round(relativeX * targetArea.Width);
            int y = targetArea.Top + (int)Math.Round(relativeY * targetArea.Height);
            x = Math.Max(targetArea.Left, Math.Min(x, targetArea.Right - width));
            y = Math.Max(targetArea.Top, Math.Min(y, targetArea.Bottom - height));

            bool moved = SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

            string note = moved ? "" : "  [SetWindowPos failed - an elevated window cannot be moved from here]";

            if (wasMaximized) {
                ShowWindow(hwnd, SW_MAXIMIZE);
            }

            // Nothing is read back here for a maximized window: while the maximize animation
            // runs, Windows reports the rectangle it is currently drawing, and acting on that
            // reading - as an earlier version did - is what shrinks such windows. The outcome
            // is read once the window has settled, by the settle check in MessageWindow.
            planned.Maximized = wasMaximized;
            planned.AskedWidth = width;
            planned.AskedHeight = height;
            planned.WantedWidth = wantedWidth;
            planned.WantedHeight = wantedHeight;

            Diagnostics.Write("TitleBarMover: " + Describe(hwnd)
                + (wasMaximized ? " (was maximized, kept maximized)" : "")
                + " screen " + Array.IndexOf(screens, source) + " -> " + targetIndex
                + " dpi " + sourceDpi + " -> " + targetDpi
                + " size " + sourceWidth + "x" + sourceHeight + " -> " + width + "x" + height
                + (width == wantedWidth && height == wantedHeight
                    ? ""
                    : " converted=" + wantedWidth + "x" + wantedHeight)
                + " at " + x + "," + y
                + " target=" + targetArea.Left + "," + targetArea.Top + " " + targetArea.Width + "x" + targetArea.Height
                + (wasMaximized || width != wantedWidth || height != wantedHeight
                    ? " (outcome follows in the settled line)"
                    : " result=" + DescribeRect(hwnd) + " zoomed=" + IsZoomed(hwnd))
                + note);

            return planned;
        }

        /// <summary>
        /// What one move asked for, kept until the settle check reads back what really
        /// happened. "Asked" is the rectangle handed to the window, "wanted" the rectangle
        /// that keeps the size the user sees; the two differ exactly when the move crosses
        /// two monitors with different scale factors.
        /// </summary>
        private class PlannedMove
        {
            public IntPtr Window;
            public int TargetIndex;
            public bool Maximized;
            public int AskedWidth;
            public int AskedHeight;
            public int WantedWidth;
            public int WantedHeight;

            public bool NeedsSizeConversion {
                get { return WantedWidth != AskedWidth || WantedHeight != AskedHeight; }
            }
        }

        /// <summary>
        /// True when the window's frame covers the monitor's work area. Diagnostic only: it
        /// turns "the move did not do what was asked" into a line in the log instead of a
        /// guess. A maximized window is exactly the work area, or a few pixels larger when
        /// the application draws its own border - never smaller.
        /// </summary>
        private static bool CoversWorkArea (IntPtr hwnd, Rectangle workArea)
        {
            const int Tolerance = 2;

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) {
                return true; // Nothing to judge when the window cannot be measured.
            }

            return rect.Left <= workArea.Left + Tolerance
                && rect.Top <= workArea.Top + Tolerance
                && rect.Right >= workArea.Right - Tolerance
                && rect.Bottom >= workArea.Bottom - Tolerance;
        }

        /// <summary>
        /// Physical pixels that keep a size looking the same when a window crosses monitors
        /// with different scale factors, so a 1000x700 window stays 1000x700 to the user
        /// instead of turning into 750x525 or 1333x933.
        /// </summary>
        private static int ScaleToDpi (int pixels, uint fromDpi, uint toDpi)
        {
            if (fromDpi == 0 || toDpi == 0 || fromDpi == toDpi) {
                return pixels;
            }
            return (int)Math.Round(pixels * (double)toDpi / fromDpi);
        }

        /// <summary>
        /// Scale factor of a monitor in DPI. The monitor is what the user sees, so this is
        /// the value the conversion has to use - and it is the same value for an
        /// application that scales itself per monitor and for one that lets Windows scale
        /// it.
        /// </summary>
        private static uint DpiOfScreen (Screen screen)
        {
            try {
                Rectangle bounds = screen.Bounds;
                POINT center = new POINT();
                center.x = bounds.Left + bounds.Width / 2;
                center.y = bounds.Top + bounds.Height / 2;

                IntPtr monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) {
                    return DefaultDpi;
                }

                uint dpiX;
                uint dpiY;
                if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0 || dpiX == 0) {
                    return DefaultDpi;
                }
                return dpiX;
            }
            catch (EntryPointNotFoundException) {
                // Older than Windows 8.1: one scale factor for the whole desktop, so there
                // is nothing to convert between the monitors.
                return DefaultDpi;
            }
            catch (DllNotFoundException) {
                return DefaultDpi;
            }
        }

        private static string DescribeRect (IntPtr hwnd)
        {
            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) {
                return "unknown";
            }
            return rect.Left + "," + rect.Top + " "
                + (rect.Right - rect.Left) + "x" + (rect.Bottom - rect.Top);
        }

        private static string Describe (IntPtr hwnd)
        {
            StringBuilder title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            return "hwnd=0x" + hwnd.ToInt64().ToString("X") + " class=" + GetClassNameOf(hwnd)
                + " title=\"" + title + "\"";
        }

        private static string GetClassNameOf (IntPtr hwnd)
        {
            StringBuilder name = new StringBuilder(256);
            GetClassName(hwnd, name, name.Capacity);
            return name.ToString();
        }

        // ---- win32 ----

        private class MessageWindow : Form
        {
            // Reads back the outcome of a maximized move: the move itself cannot report it,
            // because Windows animates the maximize and hands out the rectangle it is drawing
            // at that moment. Runs on this window's thread, which pumps messages.
            private readonly System.Windows.Forms.Timer _settleTimer = new System.Windows.Forms.Timer();
            private PlannedMove _settled;

            public MessageWindow ()
            {
                _messageWindow = this;
                try {
                    _settleTimer.Interval = SettleMilliseconds;
                    _settleTimer.Tick += delegate {
                        _settleTimer.Stop();
                        ReportSettled();
                    };
                    _hwnd = this.Handle;
                }
                finally {
                    _windowReadyEvent.Set();
                }
            }

            protected override void OnHandleCreated (EventArgs e)
            {
                base.OnHandleCreated(e);
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProcDelegate, GetModuleHandle(null), 0);
                int lastError = Marshal.GetLastWin32Error();
                if (_hook == IntPtr.Zero) {
                    // Unlike the hot key hook this one is an extra, so a failure is
                    // reported but must not take the application down.
                    Diagnostics.Write("TitleBarMover: SetWindowsHookEx(WH_MOUSE_LL) failed, Win32 error "
                        + lastError + " - middle-click moving is unavailable");
                }
                else {
                    Diagnostics.Write("TitleBarMover: WH_MOUSE_LL installed, Win32 error " + lastError);
                }
            }

            protected override void OnHandleDestroyed (EventArgs e)
            {
                if (_hook != IntPtr.Zero) {
                    UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                base.OnHandleDestroyed(e);
            }

            protected override void WndProc (ref Message m)
            {
                if (m.Msg == WM_TITLEBAR_MIDDLE_CLICK) {
                    HandlePossibleMove(m.WParam, m.LParam.ToInt32());
                }
                base.WndProc(ref m);
            }

            private void HandlePossibleMove (IntPtr hwnd, int targetIndex)
            {
                try {
                    if (targetIndex < 0) {
                        Diagnostics.Write("TitleBarMover: middle-click on the title bar of " + Describe(hwnd)
                            + " but there is only one screen - nothing to do, the click passed through");
                        return;
                    }
                    PlannedMove planned = MoveToScreen(hwnd, targetIndex);
                    if (planned != null) {
                        _settled = planned;
                        _settleTimer.Stop();
                        _settleTimer.Start();
                    }
                }
                catch (Exception ex) {
                    Diagnostics.Write("TitleBarMover: handling the middle-click failed", ex);
                }
            }

            /// <summary>
            /// One line about what the moved window really looks like once the animation is
            /// over: which monitor it ended up on, its rectangle and whether it fills that
            /// monitor's work area.
            ///
            /// It also converts the size for the applications that do not do it themselves: a
            /// window whose size is still exactly the one that was asked for was not touched by
            /// anybody, so Windows' proposed rectangle for the new scale factor was ignored and
            /// the window keeps its old physical pixels. Those are the ones where the size the
            /// user sees has to be restored from here. A window that came back with a different
            /// size was adjusted by its application or clamps its own size, and is left alone.
            ///
            /// A window that is maximized and still does not cover the monitor it was moved to
            /// gets that monitor's work area, which is the one thing the user cannot repair by
            /// hand because the window already counts as maximized.
            /// </summary>
            private void ReportSettled ()
            {
                PlannedMove planned = _settled;
                _settled = null;
                if (planned == null || !IsWindow(planned.Window)) {
                    Diagnostics.Write("TitleBarMover: settled check skipped, the window is gone");
                    return;
                }

                IntPtr hwnd = planned.Window;
                string note = "";

                if (!planned.Maximized && planned.NeedsSizeConversion) {
                    RECT current;
                    if (GetWindowRect(hwnd, out current)
                        && Math.Abs((current.Right - current.Left) - planned.AskedWidth) <= SizeTolerance
                        && Math.Abs((current.Bottom - current.Top) - planned.AskedHeight) <= SizeTolerance) {
                        SetWindowPos(hwnd, IntPtr.Zero, current.Left, current.Top,
                            planned.WantedWidth, planned.WantedHeight, SWP_NOZORDER | SWP_NOACTIVATE);
                        note = "  [nothing adjusted the size for the new scale factor, converted it here]";
                    }
                }

                Screen[] screens = Screen.AllScreens;
                int index = Array.IndexOf(screens, Screen.FromHandle(hwnd));
                if (planned.TargetIndex >= 0 && planned.TargetIndex < screens.Length) {
                    Rectangle targetArea = screens[planned.TargetIndex].WorkingArea;
                    if (index != planned.TargetIndex) {
                        note += "  [did not end up on the target monitor]";
                    }
                    else if (IsZoomed(hwnd) && !CoversWorkArea(hwnd, targetArea)) {
                        // Maximized and yet smaller than the monitor it was moved to: the user
                        // cannot fix that by maximizing again, because it already is maximized
                        // (UU远程's session window ends up like this, log.txt 2026-10-13 10:50).
                        // Handing it the target work area now is safe - the animation is over -
                        // and an application that keeps its own frame shows up in this line
                        // instead of leaving the window unusable.
                        SetWindowPos(hwnd, IntPtr.Zero, targetArea.Left, targetArea.Top,
                            targetArea.Width, targetArea.Height, SWP_NOZORDER | SWP_NOACTIVATE);
                        note += CoversWorkArea(hwnd, targetArea)
                            ? "  [maximized but not covering the target work area, filled it here]"
                            : "  [maximized but not covering the target work area, and the application keeps its own frame]";
                    }
                }

                Diagnostics.Write("TitleBarMover: settled " + Describe(hwnd)
                    + " on screen " + index + " (wanted " + planned.TargetIndex + ")"
                    + " result=" + DescribeRect(hwnd) + " zoomed=" + IsZoomed(hwnd) + note);
            }

            protected override void SetVisibleCore (bool value)
            {
                base.SetVisibleCore(false);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx (int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx (IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx (IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle (string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId ();

        [DllImport("user32.dll")]
        private static extern bool PostMessage (IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint (POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor (IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow ();

        [DllImport("user32.dll")]
        private static extern bool IsWindow (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId (IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect (IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos (IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow (IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics (int index);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi (int index, uint dpi);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint (POINT point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor (IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName (IntPtr hwnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText (IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute (IntPtr hwnd, int attribute, out RECT value, int size);
    }
}
