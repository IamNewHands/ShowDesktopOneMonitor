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
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // The show command a WINDOWPLACEMENT carries when a window is to be put into the
        // maximized state. Same value as the SW_MAXIMIZE command of ShowWindow, but a
        // different parameter: a placement's showCmd, not a ShowWindow command.
        private const int SW_SHOWMAXIMIZED = 3;

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

        private static void MoveToScreen (IntPtr hwnd, int targetIndex)
        {
            Screen[] screens = Screen.AllScreens;
            if (targetIndex < 0 || targetIndex >= screens.Length) {
                return;
            }
            if (!IsWindow(hwnd)) {
                return;
            }

            bool wasMaximized = IsZoomed(hwnd);
            Screen source = Screen.FromHandle(hwnd);
            Screen target = screens[targetIndex];
            Rectangle sourceArea = source.WorkingArea;
            Rectangle targetArea = target.WorkingArea;

            // The monitors of a mixed-scale desktop do not share a pixel grid: the same
            // window has to be given a different number of physical pixels on each of them
            // to keep the size the user sees. Carried over at its old physical size the
            // window changes its apparent size by the ratio of the two scale factors - by
            // 4/3 between a 150% and a 200% monitor - which is what makes it come out tiny
            // on the monitor with the higher scale factor and oversized on the other one.
            uint sourceDpi = DpiOfScreen(source);
            uint targetDpi = DpiOfScreen(target);

            // The rectangle to carry over is the window's restored one: while a window is
            // maximized its frame covers a whole screen, which says nothing about the size
            // the user gave the window, and the restored rectangle is what a later restore
            // uses. Reading it out of the placement avoids restoring the window to measure
            // it, which an application can answer with a resize of its own - NetEase's
            // UU远程 session window came back as 265x56 and shrank further with every move
            // (log.txt, 2026-10-13 10:00).
            RECT rect;
            if (!GetRestoredRect(hwnd, wasMaximized, out rect)) {
                Diagnostics.Write("TitleBarMover: " + Describe(hwnd) + " has no readable rectangle, not moved");
                return;
            }

            int sourceWidth = rect.Right - rect.Left;
            int sourceHeight = rect.Bottom - rect.Top;
            if (sourceWidth <= 0 || sourceHeight <= 0) {
                return;
            }

            // Keep the apparent size, and never hand over a window larger than the monitor
            // it is going to: clamping the position alone would leave its right or bottom
            // edge off that screen, where it stays cut off.
            int width = Math.Min(ScaleToDpi(sourceWidth, sourceDpi, targetDpi), targetArea.Width);
            int height = Math.Min(ScaleToDpi(sourceHeight, sourceDpi, targetDpi), targetArea.Height);

            // Keep the relative position inside the source work area, like Win+Shift+Arrow does.
            double relativeX = sourceArea.Width > 0 ? (rect.Left - sourceArea.Left) / (double)sourceArea.Width : 0.0;
            double relativeY = sourceArea.Height > 0 ? (rect.Top - sourceArea.Top) / (double)sourceArea.Height : 0.0;

            int x = targetArea.Left + (int)Math.Round(relativeX * targetArea.Width);
            int y = targetArea.Top + (int)Math.Round(relativeY * targetArea.Height);
            x = Math.Max(targetArea.Left, Math.Min(x, targetArea.Right - width));
            y = Math.Max(targetArea.Top, Math.Min(y, targetArea.Bottom - height));

            // A maximized window is moved by asking Windows for a new placement instead of
            // by restoring, positioning and maximizing it: that round trip hands the
            // application a restored window on another monitor in the middle of a DPI
            // change, and an application that insists on its own geometry answers with a
            // window of its own, small and already maximized.
            bool moved = wasMaximized
                ? PlaceMaximizedWindow(hwnd, x, y, width, height)
                : SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

            string note = moved ? "" : "  [the window refused the move - an elevated window cannot be moved from here, and an application can reject its own placement]";

            Diagnostics.Write("TitleBarMover: " + Describe(hwnd)
                + (wasMaximized ? " (was maximized, kept maximized)" : "")
                + " screen " + Array.IndexOf(screens, source) + " -> " + targetIndex
                + " dpi " + sourceDpi + " -> " + targetDpi
                + " size " + sourceWidth + "x" + sourceHeight + " -> " + width + "x" + height
                + " at " + x + "," + y
                + " target=" + targetArea.Left + "," + targetArea.Top + " " + targetArea.Width + "x" + targetArea.Height
                + " result=" + DescribeRect(hwnd) + " zoomed=" + IsZoomed(hwnd) + note);
        }

        /// <summary>
        /// The rectangle a window is restored to: its frame while it is not maximized, and
        /// the normal position out of its placement while it is, so a maximized window can be
        /// measured without taking it out of the maximized state.
        /// </summary>
        private static bool GetRestoredRect (IntPtr hwnd, bool isMaximized, out RECT rect)
        {
            rect = new RECT();
            if (!isMaximized) {
                return GetWindowRect(hwnd, out rect);
            }

            WINDOWPLACEMENT placement;
            if (!TryGetPlacement(hwnd, out placement)) {
                return false;
            }

            Point workspace = WorkspaceOrigin();
            rect.Left = placement.rcNormalPosition.Left + workspace.X;
            rect.Top = placement.rcNormalPosition.Top + workspace.Y;
            rect.Right = placement.rcNormalPosition.Right + workspace.X;
            rect.Bottom = placement.rcNormalPosition.Bottom + workspace.Y;
            return rect.Right > rect.Left && rect.Bottom > rect.Top;
        }

        /// <summary>
        /// Moves a maximized window to another monitor without taking it out of the
        /// maximized state. The restored rectangle decides which monitor the window belongs
        /// to, and asking for SW_SHOWMAXIMIZED in the same call lets Windows maximize it
        /// there - no restore, no second maximize, nothing for the application to
        /// misinterpret.
        /// </summary>
        private static bool PlaceMaximizedWindow (IntPtr hwnd, int x, int y, int width, int height)
        {
            WINDOWPLACEMENT placement;
            if (!TryGetPlacement(hwnd, out placement)) {
                return false;
            }

            Point workspace = WorkspaceOrigin();
            int left = x - workspace.X;
            int top = y - workspace.Y;

            placement.rcNormalPosition.Left = left;
            placement.rcNormalPosition.Top = top;
            placement.rcNormalPosition.Right = left + width;
            placement.rcNormalPosition.Bottom = top + height;
            placement.showCmd = SW_SHOWMAXIMIZED;

            return SetWindowPlacement(hwnd, ref placement);
        }

        private static bool TryGetPlacement (IntPtr hwnd, out WINDOWPLACEMENT placement)
        {
            placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            return GetWindowPlacement(hwnd, ref placement);
        }

        /// <summary>
        /// Origin of workspace coordinates, the coordinate system WINDOWPLACEMENT uses: it
        /// starts where the primary monitor's work area starts. Zero with a taskbar at the
        /// bottom of the primary monitor, and an offset when that taskbar sits at the top or
        /// on the left.
        /// </summary>
        private static Point WorkspaceOrigin ()
        {
            Screen primary = Screen.PrimaryScreen;
            Rectangle area = primary != null ? primary.WorkingArea : Rectangle.Empty;
            return new Point(area.Left, area.Top);
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
            public MessageWindow ()
            {
                _messageWindow = this;
                try {
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

            private static void HandlePossibleMove (IntPtr hwnd, int targetIndex)
            {
                try {
                    if (targetIndex < 0) {
                        Diagnostics.Write("TitleBarMover: middle-click on the title bar of " + Describe(hwnd)
                            + " but there is only one screen - nothing to do, the click passed through");
                        return;
                    }
                    MoveToScreen(hwnd, targetIndex);
                }
                catch (Exception ex) {
                    Diagnostics.Write("TitleBarMover: handling the middle-click failed", ex);
                }
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

        // The window's state and its restored rectangle, as Windows stores them. The
        // coordinates are workspace coordinates, and the two point fields are reserved.
        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
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
        private static extern bool GetWindowPlacement (IntPtr hwnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPlacement (IntPtr hwnd, ref WINDOWPLACEMENT placement);

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
