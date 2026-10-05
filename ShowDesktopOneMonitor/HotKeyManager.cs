using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ShowDesktopOneMonitor
{
    /// <summary>
    /// Detects the hot key ourselves with a low-level keyboard hook (WH_KEYBOARD_LL)
    /// instead of RegisterHotKey.
    ///
    /// Why RegisterHotKey is not used:
    /// when a hot key registered with MOD_WIN fires, the OS swallows the Win key-up
    /// (its way of preventing the Start menu from opening). The Win key therefore
    /// stays logically "down" for the rest of the session, so a later plain D press
    /// is delivered as Win+D. That is OS level behaviour and post-hoc key-up repair
    /// can never cover every timing.
    ///
    /// The hook removes the problem at the root:
    /// - Win down/up pass through untouched, so the system key state can never desync;
    /// - the combo is detected here, and only the hot key's own down/up is swallowed;
    /// - while the combo fires a harmless dummy key (vk 0xE8) is injected so the shell
    ///   does not treat the Win release as "open Start menu".
    /// </summary>
    public static class HotKeyManager
    {
        public static event EventHandler<HotKeyEventArgs> HotKeyPressed;

        public static int RegisterHotKey (Keys key, KeyModifiers modifiers)
        {
            _windowReadyEvent.WaitOne();
            int id = Interlocked.Increment(ref _id);
            _wnd.Invoke(new RegisterHotKeyDelegate(RegisterHotKeyInternal), id, key, modifiers);
            return id;
        }

        public static void UnregisterHotKey (int id)
        {
            _windowReadyEvent.WaitOne();
            _wnd.Invoke(new UnRegisterHotKeyDelegate(UnRegisterHotKeyInternal), id);
        }

        private delegate void RegisterHotKeyDelegate (int id, Keys key, KeyModifiers modifiers);
        private delegate void UnRegisterHotKeyDelegate (int id);

        // Only touched on the message loop thread (the hook callback and every
        // register/unregister are marshalled there by _wnd.Invoke).
        private static readonly Dictionary<int, HotKeyCombo> _combos = new Dictionary<int, HotKeyCombo>();

        private sealed class HotKeyCombo
        {
            public Keys Key;
            public KeyModifiers Modifiers;
            public bool WaitingKeyUp; // key-down was swallowed, waiting to swallow its key-up
        }

        private static void RegisterHotKeyInternal (int id, Keys key, KeyModifiers modifiers)
        {
            HotKeyCombo combo = new HotKeyCombo { Key = key, Modifiers = modifiers & ~KeyModifiers.NoRepeat };
            _combos[id] = combo;
            Diagnostics.Write("HotKeyManager: combo registered id=" + id
                + " key=" + combo.Key + " modifiers=" + combo.Modifiers
                + " (combos now " + _combos.Count + ")");
        }

        private static void UnRegisterHotKeyInternal (int id)
        {
            _combos.Remove(id);
        }

        private static void OnHotKeyPressed (HotKeyEventArgs e)
        {
            EventHandler<HotKeyEventArgs> handler = HotKeyManager.HotKeyPressed;
            if (handler != null) {
                handler(null, e);
            }
        }

        // ---- Modifier state ----
        //
        // Query the real-time state (GetAsyncKeyState) instead of caching booleans.
        //
        // Why: a low-level keyboard hook receives no key events while a secure
        // desktop is up (Win+L lock, UAC elevation, Ctrl+Alt+Del). With Win+L the
        // Win-down is seen but the Win-up lands on the secure desktop and never
        // reaches the hook, so a cached "Win is down" flag stays stuck after
        // unlocking - a plain D press then matches Win+D and fires "show desktop".
        // Querying the live state self-heals across any dropped event; injected keys
        // (e.g. remappers) are reflected by GetAsyncKeyState too, so behaviour for
        // them is unchanged.
        private static bool IsKeyDown (uint vk)
        {
            return (GetAsyncKeyState((int)vk) & 0x8000) != 0;
        }

        private static bool IsWinDown { get { return IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN); } }
        // The generic modifier codes (VK_SHIFT/VK_CONTROL/VK_MENU) are checked as well
        // as the left/right specific ones: the hook reports the specific codes, but
        // GetAsyncKeyState is not guaranteed to report them for every input source.
        private static bool IsShiftDown { get { return IsKeyDown(VK_SHIFT) || IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT); } }
        private static bool IsCtrlDown { get { return IsKeyDown(VK_CONTROL) || IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL); } }
        private static bool IsAltDown { get { return IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU); } }

        /// <summary>Modifier keys themselves always pass through and never act as the hot key.</summary>
        private static bool IsModifierKey (uint vk)
        {
            switch (vk) {
                case VK_SHIFT: case VK_CONTROL: case VK_MENU:
                case VK_LWIN: case VK_RWIN:
                case VK_LSHIFT: case VK_RSHIFT:
                case VK_LCONTROL: case VK_RCONTROL:
                case VK_LMENU: case VK_RMENU:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Modifiers must match exactly, like RegisterHotKey does.</summary>
        private static bool ModifiersMatch (KeyModifiers modifiers)
        {
            if (((modifiers & KeyModifiers.Windows) != 0) != IsWinDown) return false;
            if (((modifiers & KeyModifiers.Shift) != 0) != IsShiftDown) return false;
            if (((modifiers & KeyModifiers.Control) != 0) != IsCtrlDown) return false;
            if (((modifiers & KeyModifiers.Alt) != 0) != IsAltDown) return false;
            return true;
        }

        // ---- Low-level keyboard hook ----

        private static IntPtr HookProc (int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Counter only - never do I/O inside the hook callback.
            Interlocked.Increment(ref _hookEventCount);

            if (nCode >= 0) {
                KBDLLHOOKSTRUCT kbd = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                // Skip only our own injected dummy key (identified by dwExtraInfo).
                // Injected events from other tools must still take part in detection,
                // otherwise a remapping layer that forwards Win+D would stop working.
                bool isOwnDummy = (kbd.flags & LLKHF_INJECTED) != 0
                                  && kbd.dwExtraInfo == DummyKeyExtraInfo;
                if (!isOwnDummy) {
                    int msg = wParam.ToInt32();
                    bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

                    if ((isDown || isUp) && !IsModifierKey(kbd.vkCode)) {
                        if (isDown) {
                            // Take the first matching combo only, so several combos
                            // bound to the same key cannot double-fire.
                            HotKeyCombo matched = null;
                            foreach (HotKeyCombo combo in _combos.Values) {
                                if ((uint)combo.Key == kbd.vkCode && ModifiersMatch(combo.Modifiers)) {
                                    matched = combo;
                                    break;
                                }
                            }
                            if (matched != null) {
                                if (!matched.WaitingKeyUp) {
                                    matched.WaitingKeyUp = true;
                                    // Win combos: inject the dummy key while Win is still
                                    // held, so releasing Win does not pop the Start menu.
                                    if ((matched.Modifiers & KeyModifiers.Windows) != 0) {
                                        InjectDummyKey();
                                    }
                                    // Notify the message window asynchronously - heavy work
                                    // inside the hook callback would make Windows silently
                                    // drop the hook on timeout.
                                    PostMessage(_hwnd, WM_HOTKEY, IntPtr.Zero,
                                        MakeHotKeyLParam(matched.Key, matched.Modifiers));
                                }
                                // Swallow the hot key down (including auto-repeat).
                                return (IntPtr)1;
                            }
                        }
                        else { // isUp
                            foreach (HotKeyCombo combo in _combos.Values) {
                                if ((uint)combo.Key == kbd.vkCode && combo.WaitingKeyUp) {
                                    combo.WaitingKeyUp = false;
                                    // Swallow the key-up that pairs with the swallowed down.
                                    return (IntPtr)1;
                                }
                            }
                        }
                    }
                }
            }
            return CallNextHookEx(_hHook, nCode, wParam, lParam);
        }

        private static IntPtr MakeHotKeyLParam (Keys key, KeyModifiers modifiers)
        {
            // Same layout as the WM_HOTKEY lParam produced by RegisterHotKey:
            // low word = modifiers, high word = virtual key.
            return (IntPtr)(unchecked((int)((uint)modifiers | ((uint)key << 16))));
        }

        /// <summary>
        /// Injects a no-op key (vk 0xE8, unassigned). The shell only needs to see some
        /// other key pressed while Win is held to suppress the Start menu on release.
        /// </summary>
        private static void InjectDummyKey ()
        {
            INPUT[] inputs = new INPUT[]
            {
                MakeKeyInput(VK_DUMMY, 0),
                MakeKeyInput(VK_DUMMY, KEYEVENTF_KEYUP),
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT MakeKeyInput (uint vk, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.dwFlags = flags;
            input.u.ki.dwExtraInfo = DummyKeyExtraInfo;
            return input;
        }

        // ---- Win32 ----

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_HOTKEY = 0x0312;
        private const uint LLKHF_INJECTED = 0x00000010;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint INPUT_KEYBOARD = 1;
        private const uint VK_SHIFT = 0x10;
        private const uint VK_CONTROL = 0x11;
        private const uint VK_MENU = 0x12;
        private const uint VK_LWIN = 0x5B;
        private const uint VK_RWIN = 0x5C;
        private const uint VK_LSHIFT = 0xA0;
        private const uint VK_RSHIFT = 0xA1;
        private const uint VK_LCONTROL = 0xA2;
        private const uint VK_RCONTROL = 0xA3;
        private const uint VK_LMENU = 0xA4;
        private const uint VK_RMENU = 0xA5;
        private const uint VK_DUMMY = 0xE8;
        private static readonly IntPtr DummyKeyExtraInfo = new IntPtr(0x5344574E); // "SDWN" - tags the keys we inject

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            // MOUSEINPUT is the largest union member and must stay, otherwise
            // sizeof(INPUT) is wrong and SendInput fails on cbSize mismatch.
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc (int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx (int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx (IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx (IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle (string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput (uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool PostMessage (IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState (int vKey);

        // Must be kept alive: a collected delegate would leave the hook pointing at freed memory.
        private static readonly LowLevelKeyboardProc _hookProcDelegate = HookProc;
        private static IntPtr _hHook = IntPtr.Zero;

        // Diagnostics: proves the hook is alive and actually receiving key events.
        private static int _hookEventCount;
        private static int _reportedHookEventCount;

        private static volatile MessageWindow _wnd;
        private static volatile IntPtr _hwnd;
        private static ManualResetEvent _windowReadyEvent = new ManualResetEvent(false);

        static HotKeyManager ()
        {
            Thread messageLoop = new Thread(delegate ()
            {
                Application.Run(new MessageWindow());
            });
            messageLoop.Name = "MessageLoopThread";
            messageLoop.IsBackground = true;
            messageLoop.Start();
        }

        /// <summary>
        /// Reports hook liveness from the message loop that already exists.
        ///
        /// A WinForms timer is used instead of System.Threading.Timer on purpose: the
        /// thread-pool variant wakes the process every 30 s, and because the pool retires
        /// its idle threads in between, every tick forces the CLR to create a fresh worker
        /// thread. That create/retire cycle was leaking roughly five kernel handles per
        /// tick (measured: +18 handles in 125 s at idle, private memory flat). Timer
        /// messages go through the existing message queue and cost nothing but a WM_TIMER.
        /// </summary>
        private static void ReportHookLiveness ()
        {
            int current = _hookEventCount;
            if (current != _reportedHookEventCount) {
                _reportedHookEventCount = current;
                Diagnostics.Write("HotKeyManager: hook has received " + current + " key event(s)");
            }
        }

        private class MessageWindow : Form
        {
            private System.Windows.Forms.Timer _livenessTimer;

            public MessageWindow ()
            {
                _wnd = this;
                try {
                    // Forces handle creation, which installs the hook in OnHandleCreated.
                    _hwnd = this.Handle;
                }
                finally {
                    // Never leave the caller blocked in _windowReadyEvent.WaitOne()
                    // if hook installation failed.
                    _windowReadyEvent.Set();
                }
            }

            protected override void OnHandleCreated (EventArgs e)
            {
                base.OnHandleCreated(e);
                // Install the global low-level keyboard hook on the message loop thread.
                _hHook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProcDelegate, GetModuleHandle(null), 0);
                int lastError = Marshal.GetLastWin32Error();
                Diagnostics.Write("HotKeyManager: SetWindowsHookEx -> " + _hHook + " (Win32 error " + lastError + ")");
                if (_hHook == IntPtr.Zero) {
                    throw new Win32Exception(lastError,
                        "SetWindowsHookEx failed, the hot key is unavailable");
                }

                _livenessTimer = new System.Windows.Forms.Timer();
                _livenessTimer.Interval = 30000;
                _livenessTimer.Tick += delegate (object sender, EventArgs eventArgs) { ReportHookLiveness(); };
                _livenessTimer.Start();
            }

            protected override void OnHandleDestroyed (EventArgs e)
            {
                if (_livenessTimer != null) {
                    _livenessTimer.Stop();
                    _livenessTimer.Dispose();
                    _livenessTimer = null;
                }
                if (_hHook != IntPtr.Zero) {
                    Diagnostics.Write("HotKeyManager: unhooking, hook received " + _hookEventCount + " key event(s) total");
                    UnhookWindowsHookEx(_hHook);
                    _hHook = IntPtr.Zero;
                }
                base.OnHandleDestroyed(e);
            }

            protected override void WndProc (ref Message m)
            {
                if (m.Msg == WM_HOTKEY) {
                    HotKeyEventArgs e = new HotKeyEventArgs(m.LParam);
                    HotKeyManager.OnHotKeyPressed(e);
                }

                base.WndProc(ref m);
            }

            protected override void SetVisibleCore (bool value)
            {
                // Ensure the window never becomes visible
                base.SetVisibleCore(false);
            }
        }

        private static int _id = 0;
    }


    public class HotKeyEventArgs : EventArgs
    {
        public readonly Keys Key;
        public readonly KeyModifiers Modifiers;

        public HotKeyEventArgs(Keys key, KeyModifiers modifiers)
        {
            this.Key = key;
            this.Modifiers = modifiers;
        }

        public HotKeyEventArgs(IntPtr hotKeyParam)
        {
            uint param = (uint)hotKeyParam.ToInt64();
            Key = (Keys)((param & 0xffff0000) >> 16);
            Modifiers = (KeyModifiers)(param & 0x0000ffff);
        }
    }

    [Flags]
    public enum KeyModifiers
    {
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8,
        NoRepeat = 0x4000
    }
}
