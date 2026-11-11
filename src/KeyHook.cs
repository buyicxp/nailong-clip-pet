using System;
using System.Runtime.InteropServices;

namespace NailongClipPet
{
    /// <summary>
    /// 全局低级键盘钩子：捕获 Ctrl+C / Ctrl+X / Ctrl+V / ESC。
    /// 单次热键类型的钩子会尽力配合，但不吞键，保证系统原功能不受影响。
    /// </summary>
    public class KeyHook : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int VK_CONTROL = 0x11;
        const int VK_ESCAPE = 0x1B;
        const int VK_C = 0x43;
        const int VK_X = 0x58;
        const int VK_V = 0x56;

        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        IntPtr _hook = IntPtr.Zero;
        LowLevelKeyboardProc _proc; // 必须保持引用，否则被 GC 回收后崩溃

        public event Action CopyPressed;
        public event Action CutPressed;
        public event Action PastePressed;
        public event Action EscapePressed;

        public void Install()
        {
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    KBDLLHOOKSTRUCT k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                    if (k.vkCode == VK_ESCAPE)
                    {
                        var e1 = EscapePressed;
                        if (e1 != null) e1();
                    }
                    else if (ctrl)
                    {
                        if (k.vkCode == VK_C)
                        {
                            var c1 = CopyPressed;
                            if (c1 != null) c1();
                        }
                        else if (k.vkCode == VK_X)
                        {
                            var x1 = CutPressed;
                            if (x1 != null) x1();
                        }
                        else if (k.vkCode == VK_V)
                        {
                            var v1 = PastePressed;
                            if (v1 != null) v1();
                        }
                    }
                }
            }
            catch { }
            // 永远放行，绝不拦截系统原有按键行为
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
