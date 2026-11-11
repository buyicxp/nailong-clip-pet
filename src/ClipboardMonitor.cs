using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NailongClipPet
{
    /// <summary>
    /// 监听系统剪贴板变化，识别内容类型，并尽力判断是"复制"还是"剪切"。
    /// 判断剪切的关键：Windows 会在剪贴板里写入 "Preferred DropEffect"
    /// 格式，值 2 = 移动(剪切)，1 = 复制。右键菜单的剪切也能被识别到。
    /// </summary>
    public class ClipboardMonitor : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        const int WM_CLIPBOARDUPDATE = 0x031D;
        const int HWND_MESSAGE = -3;

        HwndSource _source;
        IntPtr _hwnd;
        bool _disposed;

        public event Action<ClipKind, int, bool> ClipboardChanged; // kind, count, isCut

        public ClipboardMonitor()
        {
            var p = new HwndSourceParameters("NailongClipListener");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = 0;
            p.ParentWindow = new IntPtr(HWND_MESSAGE);
            _source = new HwndSource(p);
            _hwnd = _source.Handle;
            _source.AddHook(WndProc);
            AddClipboardFormatListener(_hwnd);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // 延后一点读，给写入方留出完成时间
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(delegate
                    {
                        var t = new DispatcherTimer();
                        t.Interval = TimeSpan.FromMilliseconds(110);
                        t.Tick += delegate(object s, EventArgs e)
                        {
                            t.Stop();
                            ReadClipboard();
                        };
                        t.Start();
                    }));
            }
            return IntPtr.Zero;
        }

        void ReadClipboard()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return;

                ClipKind kind = ClipKind.None;
                int count = 0;

                string[] formats = data.GetFormats();
                if (formats == null || formats.Length == 0) return;

                if (Array.IndexOf(formats, DataFormats.FileDrop) >= 0)
                {
                    object o = data.GetData(DataFormats.FileDrop, true);
                    string[] files = o as string[];
                    if (files != null && files.Length > 0)
                    {
                        kind = ClipKind.Files;
                        count = files.Length;
                    }
                }

                if (kind == ClipKind.None &&
                    (Array.IndexOf(formats, DataFormats.Bitmap) >= 0 ||
                     Array.IndexOf(formats, "PNG") >= 0 ||
                     Array.IndexOf(formats, "image/png") >= 0 ||
                     Array.IndexOf(formats, "DeviceIndependentBitmap") >= 0))
                {
                    kind = ClipKind.Image;
                    count = 1;
                }

                if (kind == ClipKind.None &&
                    (Array.IndexOf(formats, DataFormats.UnicodeText) >= 0 ||
                     Array.IndexOf(formats, DataFormats.Text) >= 0))
                {
                    kind = ClipKind.Text;
                    count = 1;
                }

                if (kind == ClipKind.None) return; // HTML / 未知格式 -> 不召唤奶龙

                bool isCut = DetectCut(data, formats);

                Raise(kind, count, isCut);
            }
            catch
            {
                // 剪贴板被占用时忽略本次
            }
        }

        bool DetectCut(IDataObject data, string[] formats)
        {
            try
            {
                // 标准做法：Preferred DropEffect
                if (Array.IndexOf(formats, "Preferred DropEffect") >= 0)
                {
                    object raw = data.GetData("Preferred DropEffect", true);
                    if (raw != null)
                    {
                        byte[] bytes = raw as byte[];
                        if (bytes == null)
                        {
                            var ms = raw as System.IO.MemoryStream;
                            if (ms != null) bytes = ms.ToArray();
                        }
                        if (bytes != null && bytes.Length >= 4)
                        {
                            int effect = BitConverter.ToInt32(bytes, 0);
                            if (effect == 2) return true;  // DROPEFFECT_MOVE
                            if (effect == 1) return false; // DROPEFFECT_COPY
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        void Raise(ClipKind kind, int count, bool isCut)
        {
            var h = ClipboardChanged;
            if (h != null) h(kind, count, isCut);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_hwnd != IntPtr.Zero) RemoveClipboardFormatListener(_hwnd);
                if (_source != null) { _source.RemoveHook(WndProc); _source.Dispose(); }
            }
            catch { }
        }
    }
}
