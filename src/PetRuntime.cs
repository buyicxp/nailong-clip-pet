using System;
using System.Drawing;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace NailongClipPet
{
    /// <summary>
    /// 整个程序的调度中心：剪贴板事件 -> 状态机 -> 奶龙窗口。
    /// </summary>
    public class PetRuntime
    {
        public static readonly PetRuntime Instance = new PetRuntime();

        PetWindow _win;
        ClipboardMonitor _clip;
        KeyHook _hook;
        MouseHook _mouse;
        DispatcherTimer _releaseTimer;
        DispatcherTimer _rumbleTimer;
        DispatcherTimer _idleTimer;

        PetState _state = PetState.Idle;
        ClipKind _kind = ClipKind.None;
        int _count = 0;
        bool _isCut = false;
        bool _hasStash = false;   // 粘贴后仍保留记忆，可再次粘贴
        long _holdStartMs = 0;
        long _rightDownMs = -99999;
        int _rightX = 0, _rightY = 0;

        double _releaseStartMs = 0;
        double _releaseDurationMs = 400;

        WinForms.NotifyIcon _tray;
        PanelWindow _panel;

        public bool RumbleEnabled = true;
        public bool AutoHideEnabled = true;
        public int AutoHideSeconds = 12;

        string _assetsDir;

        public void Start()
        {
            Log.Init(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(PetRuntime).Assembly.Location), "debug.log"));
            _assetsDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(PetRuntime).Assembly.Location), "Assets");
            Audio.Init(_assetsDir);

            _win = new PetWindow();
            _win.Show();
            _win.Hide();

            _clip = new ClipboardMonitor();
            _clip.ClipboardChanged += OnClipboardChanged;

            _hook = new KeyHook();
            _hook.CopyPressed += delegate { OnCopyHotkey(); };
            _hook.CutPressed += delegate { OnCutHotkey(); };
            _hook.PastePressed += delegate { OnPaste(); };
            _hook.EscapePressed += delegate { OnEscape(); };
            _hook.Install();

            _releaseTimer = new DispatcherTimer();
            _releaseTimer.Interval = TimeSpan.FromMilliseconds(16);
            _releaseTimer.Tick += OnReleaseTick;

            // 持有时长兜底：到期没等到粘贴就自己走人
            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromSeconds(AutoHideSeconds);
            _idleTimer.Tick += OnIdleTimeout;

            // 右键菜单粘贴：靠鼠标钩子捕捉「右键->菜单->左键点菜单项」这一串动作
            _mouse = new MouseHook();
            _mouse.RightDown += OnRightDown;
            _mouse.LeftDown += OnLeftDown;
            _mouse.Install();
            Log.W("start");

            _rumbleTimer = new DispatcherTimer();
            _rumbleTimer.Interval = TimeSpan.FromSeconds(4.5);
            _rumbleTimer.Tick += delegate
            {
                if (RumbleEnabled && _state == PetState.CutHold) Audio.Play("belly_rumble");
            };
            _rumbleTimer.Start();

            CreateTray();

            // 桌面右上角的「运行中」面板：唯一可以点、可以关的窗口
            _panel = new PanelWindow(_assetsDir);
            _panel.Show();
            UpdatePanelStatus();
        }

        // ---------------- 事件入口 ----------------

        void OnCopyHotkey()
        {
            // 钩子只用来确认"刚按了复制"，真正的接管等剪贴板变化
        }

        void OnCutHotkey()
        {
        }

        void OnClipboardChanged(ClipKind kind, int count, bool isCut)
        {
            Log.W("clip kind=" + kind + " count=" + count + " cut=" + isCut);
            if (_releaseTimer.IsEnabled) return;   // 正在播放释放动画，忽略

            _kind = kind;
            _count = count;
            _isCut = isCut;
            _hasStash = true;
            _holdStartMs = Environment.TickCount;

            SetState(isCut ? PetState.CutHold : PetState.CopyHold, true);
            Audio.Play(isCut ? "cut_swallow" : "copy_pop");

            if (AutoHideEnabled)
            {
                _idleTimer.Interval = TimeSpan.FromSeconds(AutoHideSeconds);
                _idleTimer.Stop();
                _idleTimer.Start();
            }
        }

        void OnPaste()
        {
            if (_releaseTimer.IsEnabled) return;

            PetState st = _state;
            if (st == PetState.Idle)
            {
                // 空闲时按粘贴：如果剪贴板里还是刚才那个内容，就再演一次
                if (!_hasStash || _kind == ClipKind.None) return;
                BeginRelease(_isCut ? PetState.PasteCut : PetState.PasteCopy);
                return;
            }
            if (st == PetState.CopyHold)
                BeginRelease(PetState.PasteCopy);
            else if (st == PetState.CutHold)
                BeginRelease(PetState.PasteCut);
        }

        void OnEscape()
        {
            if (_state == PetState.Idle) return;
            if (_releaseTimer.IsEnabled) return;
            BeginRelease(PetState.Cancel);
        }

        /// <summary>
        /// 右键按下：用户很可能在调出粘贴菜单，记下时间和位置。
        /// </summary>
        void OnRightDown(int x, int y)
        {
            if (_releaseTimer.IsEnabled) return;
            if (_state != PetState.CopyHold && _state != PetState.CutHold) return;
            _rightDownMs = Environment.TickCount;
            _rightX = x;
            _rightY = y;
            Log.W("rightDown at " + x + "," + y);
        }

        /// <summary>
        /// 左键按下：紧跟在刚才右键之后、且位置就在菜单附近，
        /// 说明用户是在右键菜单里点了一项——粘贴绝大多数就是这么触发的。
        /// 这是右键菜单粘贴唯一可靠、且零延迟的信号。
        /// </summary>
        void OnLeftDown(int x, int y)
        {
            if (_releaseTimer.IsEnabled) return;
            if (_state != PetState.CopyHold && _state != PetState.CutHold) return;

            long dt = Environment.TickCount - _rightDownMs;
            if (dt <= 0 || dt > 8000) return;

            int dx = x - _rightX, dy = y - _rightY;
            if (dx * dx + dy * dy > 600 * 600) return;

            Log.W("leftDown in menu -> release");
            _rightDownMs = -99999;
            BeginRelease(_state == PetState.CutHold ? PetState.PasteCut : PetState.PasteCopy);
        }

        void OnIdleTimeout(object sender, EventArgs e)
        {
            _idleTimer.Stop();
            if (_releaseTimer.IsEnabled) return;
            if (_state != PetState.CopyHold && _state != PetState.CutHold) return;
            BeginRelease(PetState.Cancel);
        }

        // ---------------- 状态切换 ----------------

        void SetState(PetState st, bool showPet)
        {
            _state = st;
            SyncCanvas();
            if (showPet || st != PetState.Idle)
            {
                _win.SetPetVisible(true);
                _win.SetCursorHidden(PetWindow.HideSystemCursor &&
                    (st == PetState.CopyHold || st == PetState.CutHold));
            }
        }

        void BeginRelease(PetState releaseState)
        {
            Log.W("beginRelease " + releaseState);
            _state = releaseState;
            _releaseStartMs = Environment.TickCount;
            _releaseProgress = 0;
            if (_idleTimer != null) _idleTimer.Stop();
            switch (releaseState)
            {
                case PetState.PasteCopy:
                    _releaseDurationMs = 520;
                    Audio.Play("paste_thud");
                    break;
                case PetState.PasteCut:
                    _releaseDurationMs = 620;
                    Audio.Play("cut_spit");
                    break;
                default:
                    _releaseDurationMs = 330;
                    Audio.Play("cancel_whoosh");
                    break;
            }
            SyncCanvas();
            _win.SetPetVisible(true);
            // 一开始放下 / 吐出来就把光标还给用户，让他能继续点击
            _win.SetCursorHidden(false);
            _releaseTimer.Start();
        }

        double _releaseProgress;

        void OnReleaseTick(object sender, EventArgs e)
        {
            double elapsed = Environment.TickCount - _releaseStartMs;
            _releaseProgress = Math.Min(1.0, elapsed / _releaseDurationMs);
            _win.Canvas.ReleaseProgress = _releaseProgress;

            if (_releaseProgress >= 1.0)
            {
                _releaseTimer.Stop();
                _releaseProgress = 0;
                _win.Canvas.ReleaseProgress = 0;
                _state = PetState.Idle;
                SyncCanvas();
                _win.SetCursorHidden(false);
                _win.SetPetVisible(false);
            }
        }

        void SyncCanvas()
        {
            if (_win == null) return;
            var c = _win.Canvas;
            c.State = _state;
            c.Kind = _kind;
            c.Count = _count;
            c.StateStartTick = Environment.TickCount;
            c.StateTime = 0;
            UpdatePanelStatus();
        }

        /// <summary>把右上角面板显示出来并顶到最前（重复启动 exe 时用）</summary>
        public void ShowPanel()
        {
            if (_panel == null) return;
            _panel.Show();
            _panel.Topmost = false;
            _panel.Topmost = true;
            _panel.Activate();
        }

        /// <summary>把当前状态推到右上角面板上，让用户一眼看到它在干嘛</summary>
        void UpdatePanelStatus()
        {
            if (_panel == null) return;
            string what;
            switch (_kind)
            {
                case ClipKind.Files: what = _count + " 个文件"; break;
                case ClipKind.Image: what = "一张图片"; break;
                case ClipKind.Text: what = "一段文字"; break;
                default: what = "内容"; break;
            }
            string text;
            switch (_state)
            {
                case PetState.CopyHold: text = "运行中 · 抱着 " + what; break;
                case PetState.CutHold: text = "运行中 · 肚子里装着 " + what; break;
                case PetState.PasteCopy: text = "运行中 · 正在放下"; break;
                case PetState.PasteCut: text = "运行中 · 正在吐出"; break;
                case PetState.Cancel: text = "运行中 · 正在收起"; break;
                default: text = "运行中 · 空闲"; break;
            }
            _panel.SetStatus(text);
        }

        // ---------------- 托盘 ----------------

        void CreateTray()
        {
            try
            {
                _tray = new WinForms.NotifyIcon();
                _tray.Icon = RenderTrayIcon();
                _tray.Text = "奶龙剪贴宠物";
                _tray.Visible = true;

                var menu = new WinForms.ContextMenuStrip();
                var miSound = new WinForms.ToolStripMenuItem("音效开关");
                miSound.Checked = true;
                miSound.Click += delegate
                {
                    Audio.Enabled = !Audio.Enabled;
                    miSound.Checked = Audio.Enabled;
                };
                var miRumble = new WinForms.ToolStripMenuItem("剪切咕咕声");
                miRumble.Checked = true;
                miRumble.Click += delegate
                {
                    RumbleEnabled = !RumbleEnabled;
                    miRumble.Checked = RumbleEnabled;
                };
                var miHide = new WinForms.ToolStripMenuItem("复制时用奶龙替代光标");
                miHide.Checked = PetWindow.HideSystemCursor;
                miHide.Click += delegate
                {
                    PetWindow.HideSystemCursor = !PetWindow.HideSystemCursor;
                    miHide.Checked = PetWindow.HideSystemCursor;
                    if (_state == PetState.CopyHold || _state == PetState.CutHold)
                        _win.SetCursorHidden(PetWindow.HideSystemCursor);
                };
                var miAuto = new WinForms.ToolStripMenuItem("无粘贴自动收起（12秒）");
                miAuto.Checked = AutoHideEnabled;
                miAuto.Click += delegate
                {
                    AutoHideEnabled = !AutoHideEnabled;
                    miAuto.Checked = AutoHideEnabled;
                    if (!AutoHideEnabled) _idleTimer.Stop();
                };

                // 奶龙大小：只改 PetWindow.Zoom 一个数，画布内所有锚点等比跟着换算
                var miSize = new WinForms.ToolStripMenuItem("奶龙大小");
                var sizes = new object[,] { { "大（100%）", 1.0 }, { "中（75%）", 0.75 }, { "小（60%）", 0.6 }, { "迷你（50%）", 0.5 } };
                for (int i = 0; i < sizes.GetLength(0); i++)
                {
                    double v = (double)sizes[i, 1];
                    var item = new WinForms.ToolStripMenuItem((string)sizes[i, 0]);
                    item.Checked = Math.Abs(PetWindow.Zoom - v) < 0.001;
                    item.Click += delegate
                    {
                        PetWindow.Zoom = v;
                        _win.ApplyZoom();
                        for (int j = 0; j < miSize.DropDownItems.Count; j++)
                            ((WinForms.ToolStripMenuItem)miSize.DropDownItems[j]).Checked =
                                Math.Abs(((double)sizes[j, 1]) - PetWindow.Zoom) < 0.001;
                    };
                    miSize.DropDownItems.Add(item);
                }
                var miExit = new WinForms.ToolStripMenuItem("退出");
                miExit.Click += delegate { ExitApp(); };

                menu.Items.Add(miSound);
                menu.Items.Add(miRumble);
                menu.Items.Add(miHide);
                menu.Items.Add(miAuto);
                menu.Items.Add(miSize);
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add(miExit);
                _tray.ContextMenuStrip = menu;
                _tray.DoubleClick += delegate { ExitApp(); };
            }
            catch { }
        }

        Icon RenderTrayIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(Color.FromArgb(0xF7, 0xD6, 0x54)), 3, 8, 26, 22);
                g.FillEllipse(new SolidBrush(Color.FromArgb(0xF7, 0xD6, 0x54)), 9, 1, 15, 15);
                g.FillEllipse(Brushes.Black, 13, 7, 3, 2);
                g.FillEllipse(Brushes.Black, 19, 7, 3, 2);
            }
            IntPtr h = bmp.GetHicon();
            return Icon.FromHandle(h);
        }

        public void ExitApp()
        {
            try
            {
                if (_releaseTimer != null) _releaseTimer.Stop();
                if (_rumbleTimer != null) _rumbleTimer.Stop();
                if (_idleTimer != null) _idleTimer.Stop();
                if (_mouse != null) _mouse.Dispose();
                if (_win != null)
                {
                    _win.SetCursorHidden(false);
                    _win.SetPetVisible(false);
                    _win.Hide();
                }
                if (_hook != null) _hook.Dispose();
                if (_clip != null) _clip.Dispose();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                if (_panel != null) { _panel.Hide(); _panel.Close(); }
                // 关掉程序就把系统箭头无条件装回来，不依赖状态判断
                PetWindow.RestoreArrowCursor();
                Log.W("exit");
            }
            catch { }
            System.Windows.Application.Current.Shutdown();
        }
    }
}
