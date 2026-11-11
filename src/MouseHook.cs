using System;
using System.Runtime.InteropServices;

namespace NailongClipPet
{
    /// <summary>
    /// 全局低级鼠标钩子，只取「右键按下」和「左键按下」两个时机，不做任何耗时操作。
    /// 用来解决右键菜单粘贴抓不到的问题：
    /// 右键 -> 菜单弹出 -> 用户左键点菜单项，这一串动作可以被完整观察到。
    /// </summary>
    public class MouseHook : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        IntPtr _hook = IntPtr.Zero;
        LowLevelMouseProc _proc;

        // 最近一次右键按下的时刻，用来在右键菜单弹出时把奶龙调淡，免得挡菜单
        public static long LastRightDownTick = 0;

        public event Action<int, int> RightDown;
        public event Action<int, int> LeftDown;

        public void Install()
        {
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    int msg = (int)wParam;
                    if (msg == WM_LBUTTONDOWN)
                    {
                        var h = LeftDown;
                        if (h != null) h(m.pt.x, m.pt.y);
                    }
                    else if (msg == WM_RBUTTONDOWN)
                    {
                        LastRightDownTick = Environment.TickCount;
                        var h = RightDown;
                        if (h != null) h(m.pt.x, m.pt.y);
                    }
                }
            }
            catch { }
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
