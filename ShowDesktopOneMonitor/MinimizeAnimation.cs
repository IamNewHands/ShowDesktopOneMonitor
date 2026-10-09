using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Aims the minimize/restore animation of the toggled windows at the bottom edge
    /// of the monitor the toggle runs on.
    ///
    /// Windows asks every registered shell hook window where the animation of a
    /// window should start and end (HSHELL_GETMINRECT). When nobody answers, the shell
    /// uses that window's taskbar button - and on a multi-monitor setup that button can
    /// sit on another monitor, so the animation visibly slides sideways across the
    /// desktop instead of dropping down onto the taskbar of the window's own monitor.
    ///
    /// The redirect is armed only while our own toggle runs, so minimizing a window by
    /// hand keeps the native animation.
    /// </summary>
    internal static class MinimizeAnimation
    {
        // The shell hook code used to ask for the animation rectangle. It is not part
        // of the documented enum, but it is what third-party taskbars answer to.
        private const int HSHELL_GETMINRECT = 5;
        private const int ShellHookCodeMask = 0x7FFF;

        // The answer is a thin strip at the bottom edge of the monitor, so the window
        // shrinks into the taskbar area instead of flying off to another monitor.
        private const int TargetStripHeight = 2;

        // Layout of SHELLHOOKINFO: the HWND first, then the rect as four SHORTs (not
        // the four LONGs of a real RECT). Coordinates are physical pixels.
        private const int HandleOffset = 0;
        private const int RectOffset = 8;

        // How long an armed redirect stays armed. The shell asks while the ShowWindow
        // call that starts the animation is still running, but a slow application can
        // start its animation later than that, so this is deliberately generous: an ask
        // that arrives after the window has expired would fall back to the shell's own
        // rectangle and slide across the desktop again.
        private const int RedirectMilliseconds = 5000;

        private static uint _shellHookMessage;
        private static ShellHookWindow _window;

        // Written by the message loop thread right before it toggles, read by the UI
        // thread while it answers the shell. Reference and int writes are atomic, so no
        // lock is needed; the deadline is written last and checked first.
        private static volatile Screen _targetScreen;
        private static volatile int _redirectUntil;

        /// <summary>
        /// Installs the shell hook. Must be called from a thread that pumps messages and
        /// keeps pumping while a toggle runs - the UI thread - because the shell asks for
        /// the animation rectangle while the message loop thread is inside ShowWindow.
        /// </summary>
        public static void Enable ()
        {
            try {
                _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
                if (_shellHookMessage == 0) {
                    Diagnostics.Write("MinimizeAnimation: RegisterWindowMessage(SHELLHOOK) failed");
                    return;
                }

                ShellHookWindow window = new ShellHookWindow();
                CreateParams parameters = new CreateParams();
                parameters.Caption = "ShowDesktopOneMonitor shell hook";
                parameters.X = -32000;
                parameters.Y = -32000;
                parameters.Width = 1;
                parameters.Height = 1;
                window.CreateHandle(parameters);

                bool registered = RegisterShellHookWindow(window.Handle);
                int lastError = Marshal.GetLastWin32Error();
                if (!registered) {
                    window.DestroyHandle();
                    Diagnostics.Write("MinimizeAnimation: RegisterShellHookWindow failed (Win32 error "
                        + lastError + ")");
                    return;
                }

                _window = window;
                Diagnostics.Write("MinimizeAnimation: shell hook installed, hwnd=0x"
                    + window.Handle.ToInt64().ToString("X"));
            }
            catch (Exception ex) {
                Diagnostics.Write("MinimizeAnimation: Enable failed", ex);
                _window = null;
            }
        }

        public static void Disable ()
        {
            try {
                if (_window != null) {
                    DeregisterShellHookWindow(_window.Handle);
                    _window.DestroyHandle();
                    _window = null;
                }
            }
            catch (Exception ex) {
                Diagnostics.Write("MinimizeAnimation: Disable failed", ex);
            }
        }

        /// <summary>
        /// Arms the redirect: windows toggled now animate to the bottom edge of
        /// <paramref name="screen"/> instead of to the shell's taskbar button.
        /// </summary>
        public static void RedirectFor (Screen screen)
        {
            if (_window == null || screen == null) {
                return;
            }

            _targetScreen = screen;
            _redirectUntil = Environment.TickCount + RedirectMilliseconds;
        }

        private static bool IsArmed ()
        {
            int until = _redirectUntil;
            // Wrap-safe comparison: TickCount rolls over every ~24 days.
            return until != 0 && unchecked(until - Environment.TickCount) > 0;
        }

        /// <summary>
        /// One line describing a window, for the toggle log: the class is what identifies
        /// a window whose title is empty (several applications keep their frame untitled).
        /// </summary>
        public static string DescribeWindow (IntPtr hwnd)
        {
            StringBuilder className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            StringBuilder title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            return "hwnd=0x" + hwnd.ToInt64().ToString("X")
                + " class=" + className + " title=\"" + title + "\"";
        }

        /// <summary>
        /// Rewrites the rectangle the shell is about to animate to. Returns false to
        /// leave the shell's own answer alone.
        /// </summary>
        private static bool AimAtTaskbar (IntPtr shellHookInfo)
        {
            if (shellHookInfo == IntPtr.Zero || !IsArmed()) {
                return false;
            }

            Screen screen = _targetScreen;
            if (screen == null) {
                return false;
            }

            IntPtr hwnd = Marshal.ReadIntPtr(shellHookInfo, HandleOffset);
            Rectangle bounds = screen.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) {
                return false;
            }

            // The shell's own rectangle: the taskbar button it would use.
            short shellLeft = Marshal.ReadInt16(shellHookInfo, RectOffset);
            short shellTop = Marshal.ReadInt16(shellHookInfo, RectOffset + 2);
            short shellRight = Marshal.ReadInt16(shellHookInfo, RectOffset + 4);
            short shellBottom = Marshal.ReadInt16(shellHookInfo, RectOffset + 6);

            int centerX = bounds.Left + bounds.Width / 2;
            Rectangle window;
            if (TryGetWindowRect(hwnd, out window) && window.Right > window.Left) {
                int windowCenter = window.Left + (window.Right - window.Left) / 2;
                if (windowCenter >= bounds.Left && windowCenter < bounds.Right) {
                    // The window is visible on this monitor: drop straight down from it.
                    centerX = windowCenter;
                }
            }

            int left = Math.Max(bounds.Left, Math.Min(centerX, bounds.Right - 1));
            int top = Math.Max(bounds.Top, bounds.Bottom - TargetStripHeight);
            int right = Math.Min(left + TargetStripHeight, bounds.Right);

            Marshal.WriteInt16(shellHookInfo, RectOffset, ClampToShort(left));
            Marshal.WriteInt16(shellHookInfo, RectOffset + 2, ClampToShort(top));
            Marshal.WriteInt16(shellHookInfo, RectOffset + 4, ClampToShort(right));
            Marshal.WriteInt16(shellHookInfo, RectOffset + 6, ClampToShort(bounds.Bottom));

            Diagnostics.Write("MinimizeAnimation: hwnd=0x" + hwnd.ToInt64().ToString("X")
                + " shell rect=" + shellLeft + "," + shellTop + "," + shellRight + "," + shellBottom
                + " -> " + screen.DeviceName + " bottom " + left + "," + top + "," + right + "," + bounds.Bottom);
            return true;
        }

        private static short ClampToShort (int value)
        {
            if (value > short.MaxValue) return short.MaxValue;
            if (value < short.MinValue) return short.MinValue;
            return (short)value;
        }

        private static bool TryGetWindowRect (IntPtr hwnd, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (hwnd == IntPtr.Zero) {
                return false;
            }

            RECT native;
            if (!GetWindowRect(hwnd, out native)) {
                return false;
            }

            rect = new Rectangle(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
            return true;
        }

        private class ShellHookWindow : NativeWindow
        {
            protected override void WndProc (ref Message m)
            {
                try {
                    if (m.Msg == (int)_shellHookMessage
                        && (int)(m.WParam.ToInt64() & ShellHookCodeMask) == HSHELL_GETMINRECT
                        && AimAtTaskbar(m.LParam)) {
                        // A non-zero result tells the shell to use the rect we wrote.
                        m.Result = new IntPtr(1);
                        return;
                    }
                }
                catch (Exception ex) {
                    Diagnostics.Write("MinimizeAnimation: answering the shell hook failed", ex);
                }

                base.WndProc(ref m);
            }
        }

        // ---- win32 ----

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage (string message);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterShellHookWindow (IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DeregisterShellHookWindow (IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect (IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName (IntPtr hwnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText (IntPtr hwnd, StringBuilder text, int maxCount);
    }
}
