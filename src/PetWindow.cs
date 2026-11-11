using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace NailongClipPet
{
    public class PetWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern void SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern IntPtr SetCursor(IntPtr hCursor);

        [DllImport("user32.dll")]
        static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadCursorFromFile(string path);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr h);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr dc, IntPtr o);

        [DllImport("gdi32.dll")]
        static extern IntPtr GetStockObject(int i);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr dc);

        [DllImport("user32.dll")]
        static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        const uint OCR_NORMAL = 32512;
        const uint SPI_SETCURSORS = 0x0057;

        [DllImport("user32.dll")]
        static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bits, IntPtr bitsPtr);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        const int IDC_ARROW = 32512;

        static IntPtr _blankCursor = IntPtr.Zero;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        // 奶龙握住东西的那只手在画布里的位置：替代光标时，这个点就压在光标热点上
        public static double HotspotX = 124;
        public static double HotspotY = 114;
        // 老姿态：奶龙跟在光标右下方（不替代光标时用）
        public static double OffsetX = 108;
        public static double OffsetY = 96;
        // 复制/剪切期间是否用奶龙替代系统光标（箭头隐藏，热点落在奶龙手上）
        public static bool HideSystemCursor = true;
        // 整体缩放：内部所有坐标都按 260 画布算，这里只是把画面等比缩小，改这一个数就能换大小
        public static double Zoom = 0.5;

        readonly PetCanvas _canvas = new PetCanvas();
        readonly DispatcherTimer _timer = new DispatcherTimer();

        IntPtr _hwnd;
        double _dpiScale = 1.0;
        double _startTick;

        // 鼠标速度 -> 倾斜
        double _lastX, _lastY;
        double _tilt, _tiltVel;

        bool _visibleCursorHidden = false;

        public PetCanvas Canvas { get { return _canvas; } }

        public PetWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            IsHitTestVisible = false;
            Focusable = false;
            Content = _canvas;
            _canvas.HorizontalAlignment = HorizontalAlignment.Left;
            _canvas.VerticalAlignment = VerticalAlignment.Top;
            ApplyZoom();

            SourceInitialized += OnSourceInit;
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += OnTimer;
            _startTick = Environment.TickCount;
            _timer.Start();
        }

        void OnSourceInit(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE,
                style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null)
                _dpiScale = src.CompositionTarget.TransformToDevice.M11;

            // 初始位置放到屏幕外，避免启动闪一下
            Left = -1000; Top = -1000;
        }

        void OnTimer(object sender, EventArgs e)
        {
            POINT p;
            if (!GetCursorPos(out p)) return;

            double cx = p.X / _dpiScale;
            double cy = p.Y / _dpiScale;

            // 鼠标速度驱动倾斜
            double dx = cx - _lastX;
            double dy = cy - _lastY;
            _lastX = cx; _lastY = cy;

            double targetTilt = 0;
            if (_canvas.State == PetState.CopyHold || _canvas.State == PetState.CutHold ||
                _canvas.State == PetState.PasteCopy || _canvas.State == PetState.PasteCut)
            {
                targetTilt = Math.Max(-14, Math.Min(14, dx * 1.2)) * 0.9 + Math.Max(-8, Math.Min(8, dy * 0.5));
            }
            // 弹簧回正
            _tiltVel += (targetTilt - _tilt) * 0.22;
            _tiltVel *= 0.78;
            _tilt += _tiltVel;
            _canvas.Tilt = _tilt;

            _canvas.Time = (Environment.TickCount - _startTick) / 1000.0;
            _canvas.StateTime = (Environment.TickCount - _canvas.StateStartTick) / 1000.0;

            // 替代光标期间如果刚按了右键（大概率是弹出右键菜单准备粘贴），把奶龙调淡让菜单透出来。
            // 注意：只有「进入持有状态之后」按的右键才算——用右键菜单复制/剪切时，右键就在持有之前，
            // 那种情况不该变淡（否则右键复制完眼前就一片半透明）。
            bool replacing = HideSystemCursor &&
                (_canvas.State == PetState.CopyHold || _canvas.State == PetState.CutHold);
            long sinceRight = Environment.TickCount - MouseHook.LastRightDownTick;
            double wantOpacity = (replacing && MouseHook.LastRightDownTick > _canvas.StateStartTick &&
                                  sinceRight < 6000) ? 0.42 : 1.0;
            if (Math.Abs(_canvas.Opacity - wantOpacity) > 0.01)
                _canvas.Opacity = wantOpacity;

            // 释放动画进度由 Runtime 推进，这里只负责重绘
            if (IsVisible)
                _canvas.InvalidateVisual();

            UpdatePosition((int)p.X, (int)p.Y);

        }

        void UpdatePosition(int px, int py)
        {
            if (_hwnd == IntPtr.Zero) return;
            try
            {
                // 窗口原点（左上角）相对热点偏移：隐藏光标时热点在奶龙手上，否则在右下方的老姿态点
                double hx = HideSystemCursor ? HotspotX : (NailongArt.CanvasSize / 2 - OffsetX);
                double hy = HideSystemCursor ? HotspotY : (NailongArt.CanvasSize / 2 - OffsetY);
                Left = px / _dpiScale - hx * Zoom;
                Top = py / _dpiScale - hy * Zoom;
            }
            catch { }
        }

        /// <summary>
        /// 应用整体缩放：窗口尺寸和各锚点都按 Zoom 等比换算。
        /// 用 LayoutTransform 而不是 RenderTransform——后者只改变绘制结果，
        /// 元素布局尺寸还是 260，会被 195 的窗口直接裁掉下半身（腿就是这么没的）。
        /// </summary>
        public void ApplyZoom()
        {
            _canvas.LayoutTransform = new ScaleTransform(Zoom, Zoom);
            _canvas.InvalidateMeasure();
            _canvas.InvalidateVisual();
            Width = NailongArt.CanvasSize * Zoom;
            Height = NailongArt.CanvasSize * Zoom;
            UpdatePosition(0, 0);
        }

        /// <summary>
        /// 隐藏 / 恢复系统鼠标光标（引用计数安全配对）。
        /// </summary>
        public void SetCursorHidden(bool hide)
        {
            if (hide == _visibleCursorHidden) return;
            if (hide)
            {
                _visibleCursorHidden = true;
                // 把系统箭头整体换成透明光标（SetSystemCursor 会接管句柄，所以每次现做一份新的）
                try
                {
                    IntPtr h = BlankCursor();
                    if (h != IntPtr.Zero)
                    {
                        SetSystemCursor(h, OCR_NORMAL);
                    }
                    else Log.W("blank cursor create failed");
                }
                catch (Exception ex) { Log.W("cursor hide EX " + ex.Message); }
            }
            else
            {
                _visibleCursorHidden = false;
                RestoreArrowCursor();
            }
        }

        /// <summary>
        /// 把系统箭头换回真正的箭头。SPI_SETCURSORS 在部分机器上无效，
        /// 所以直接从注册表里读用户当前的箭头文件再装回去。
        /// </summary>
        public static void RestoreArrowCursor()
        {
            try
            {
                string path = null;
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors"))
                {
                    if (k != null)
                    {
                        object o = k.GetValue("Arrow");
                        if (o is string) path = Environment.ExpandEnvironmentVariables((string)o);
                    }
                }
                if (path == null || path.Length == 0)
                    path = @"C:\Windows\Cursors\aero_arrow.cur";

                IntPtr h = LoadCursorFromFile(path);
                Log.W("restore path=" + path + " h=" + h);
                if (h != IntPtr.Zero)
                {
                    bool ok = SetSystemCursor(h, OCR_NORMAL);   // 会接管句柄，不用自己释放
                    Log.W("restore ok=" + ok);
                }
                else
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                }
                SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW));
            }
            catch (Exception ex) { Log.W("restore EX " + ex.Message); }
        }

        /// <summary>
        /// 生成一个 32x32 全透明光标，用 SetCursor 顶替系统箭头。
        /// </summary>
        static IntPtr BlankCursor()
        {
            // 注意：SetSystemCursor 会销毁传入的光标句柄，所以每次都要新建一份，不能缓存
            var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            IntPtr hColor = bmp.GetHbitmap(System.Drawing.Color.FromArgb(0));
            IntPtr hMask = CreateBitmap(32, 32, 1, 1, IntPtr.Zero);
            // 掩码全填 1：“AND 掩码=1”表示原样透出背景，光标就彻底看不见了
            IntPtr mdc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldSel = SelectObject(mdc, hMask);
            RECT r = new RECT();
            r.Right = 32; r.Bottom = 32;
            FillRect(mdc, ref r, GetStockObject(0));   // WHITE_BRUSH
            SelectObject(mdc, oldSel);
            DeleteDC(mdc);
            ICONINFO ii = new ICONINFO();
            ii.fIcon = false;
            ii.xHotspot = 0;
            ii.yHotspot = 0;
            ii.hbmMask = hMask;
            ii.hbmColor = hColor;
            _blankCursor = CreateIconIndirect(ref ii);
            DeleteObject(hColor);
            DeleteObject(hMask);
            bmp.Dispose();
            return _blankCursor;
        }

        /// <summary>
        /// 奶龙是否应该出现在屏幕上。
        /// </summary>
        public void SetPetVisible(bool visible)
        {
            if (visible)
            {
                if (!IsVisible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            }
            else
            {
                SetCursorHidden(false);
                if (IsVisible) Hide();
            }
        }
    }
}
