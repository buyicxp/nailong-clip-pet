using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NailongClipPet
{
    /// <summary>
    /// 离屏渲染测试：
    ///   RenderTest.exe &lt;outDir&gt;           单帧检查图
    ///   RenderTest.exe &lt;outDir&gt; strip     逐帧序列，用来合成 GIF 预览
    /// </summary>
    public static class TestRender
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : @"D:\Projects\NailongClipPet\preview";
            Directory.CreateDirectory(outDir);

            if (args.Length > 1 && args[1] == "strip")
            {
                Strip(outDir);
                Console.WriteLine("strip done");
                return;
            }

            Shot(outDir, "c0_000", PetState.CopyHold, ClipKind.Files, 3, 0.00, 0, 0.00, true);
            Shot(outDir, "c1_025", PetState.CopyHold, ClipKind.Files, 3, 0.20, 0, 0.20, true);
            Shot(outDir, "c2_060", PetState.CopyHold, ClipKind.Files, 3, 0.60, 0, 0.60, true);

            Shot(outDir, "p1_040", PetState.PasteCopy, ClipKind.Files, 3, 0.60, 0.40, 0.6, true);
            Shot(outDir, "p2_070", PetState.PasteCopy, ClipKind.Files, 3, 0.60, 0.70, 0.6, true);

            Shot(outDir, "k0_010", PetState.CutHold, ClipKind.Files, 3, 0.10, 0, 0.10, true);
            Shot(outDir, "k1_038", PetState.CutHold, ClipKind.Files, 3, 0.38, 0, 0.38, true);
            Shot(outDir, "k2_058", PetState.CutHold, ClipKind.Files, 3, 0.58, 0, 0.58, true);
            Shot(outDir, "k3_090", PetState.CutHold, ClipKind.Files, 3, 0.90, 0, 0.90, true);

            Shot(outDir, "s1_028", PetState.PasteCut, ClipKind.Files, 3, 0.50, 0.28, 0.6, true);
            Shot(outDir, "s2_055", PetState.PasteCut, ClipKind.Files, 3, 0.50, 0.55, 0.6, true);

            Shot(outDir, "x_text", PetState.CopyHold, ClipKind.Text, 1, 0.60, 0, 0.60, true);
            Shot(outDir, "x_image", PetState.CopyHold, ClipKind.Image, 1, 0.60, 0, 0.60, true);

            Console.WriteLine("done -> " + outDir);
        }

        static void Strip(string outDir)
        {
            string baseDir = Path.Combine(outDir, "strip");
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);

            // 复制：空手抬起 -> 文件凭空出现
            RenderSeq(baseDir, "copy", PetState.CopyHold, 0.0, 0.55, 0.025, 0, t => 0);

            // 剪切：拿稳 -> 送到嘴边 -> 塞进嘴 -> 肚子鼓
            RenderSeq(baseDir, "cut", PetState.CutHold, 0.0, 1.35, 0.03, 0, t => 0);

            // 复制后粘贴：文件从手里放下去
            RenderSeq(baseDir, "pastecopy", PetState.PasteCopy, 0.0, 1.0, 0.04, 0.6, t => t);

            // 剪切后粘贴：从肚子里吐出来
            RenderSeq(baseDir, "pastecut", PetState.PasteCut, 0.0, 1.0, 0.04, 0.6, t => t);
        }

        static void RenderSeq(string baseDir, string name, PetState st,
            double from, double to, double step, double stateTime, Func<double, double> progress)
        {
            string dir = Path.Combine(baseDir, name);
            Directory.CreateDirectory(dir);
            int idx = 0;
            for (double v = from; v <= to + 1e-9; v += step)
            {
                Shot(dir, string.Format("f{0:000}", idx), st, ClipKind.Files, 3,
                    v, progress(v), stateTime, false);
                idx++;
            }
        }

        static void Shot(string dir, string name, PetState st, ClipKind kind, int count,
            double t, double rp, double stateTime, bool checker)
        {
            var canvas = new PetCanvas();
            canvas.State = st;
            canvas.Kind = kind;
            canvas.Count = count;
            canvas.Time = t;
            canvas.ReleaseProgress = rp;
            canvas.Tilt = 0;
            canvas.StateTime = stateTime;

            canvas.Measure(new Size(260, 260));
            canvas.Arrange(new Rect(0, 0, 260, 260));
            canvas.UpdateLayout();

            int scale = 2;
            int w = 260 * scale, h = 260 * scale;
            var rtb = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (checker)
                {
                    for (int y = 0; y < h; y += 20)
                        for (int x = 0; x < w; x += 20)
                        {
                            var c = (((x / 20) + (y / 20)) % 2 == 0)
                                ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0xDC, 0xDC, 0xDC);
                            dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(x, y, 20, 20));
                        }
                }
                else
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)), null,
                        new Rect(0, 0, w, h));
                }
            }
            rtb.Render(dv);
            rtb.Render(canvas);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(Path.Combine(dir, name + ".png")))
                enc.Save(fs);
        }
    }
}
