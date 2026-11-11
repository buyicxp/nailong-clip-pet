using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NailongClipPet
{
    /// <summary>
    /// 奶龙渲染层 —— 逐帧表演。
    /// 6 张姿势帧都来自用户参考图里的同一只角色（抬手 / 握物 / 手到嘴边 / 张嘴 / 大肚子 / 鼓肚张嘴），
    /// 全部做过统一归一化（同高度、同基线、同水平中心），所以切帧时不会跳。
    /// 手里的文件、纸条、照片是实时矢量画的，因为每次复制的内容都不一样。
    /// </summary>
    public static class NailongArt
    {
        public const double CanvasSize = 260.0;

        // 与 tools/build_frames.py 里的映射保持一致（帧 900x1100，角色高 1000）
        const double SpriteBottom = 248.0;
        const double SpriteHeight = 212.0;
        const double AnchorX = 130.0;
        const double FrameW = 190.8;
        const double FrameH = 233.2;
        const double FrameLeft = 34.6;
        const double FrameTop = 14.8;

        // 手势锚点（画布坐标，从 frames_grid.png 上量的）
        static readonly Point HandUp = new Point(92, 124);    // 抬手空手
        static readonly Point HandGrip = new Point(124, 114); // 握物
        static readonly Point HandMouth = new Point(151, 103);// 手在嘴边
        static readonly Point Mouth = new Point(157, 104);    // 嘴的位置

        enum F { HandUp = 0, Grip, ToMouth, Open, Big, BigOpen }

        static readonly string[] FrameFiles = { "a_handup", "b_grip", "c_mouth", "d_open", "e_big", "f_bigopen" };

        static BitmapSource[] _frames;
        static BitmapSource _single;
        static bool _loadTried;

        static readonly Brush PaperFill = Fz(new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC)));
        static readonly Pen PaperEdge = Fz(new Pen(new SolidColorBrush(Color.FromRgb(0xCB, 0xCB, 0xCB)), 1.3));
        static readonly Brush FoldFill = Fz(new SolidColorBrush(Color.FromRgb(0xE2, 0xE2, 0xE2)));
        static readonly Pen LinePen = Fz(new Pen(new SolidColorBrush(Color.FromRgb(0xB6, 0xB6, 0xB6)), 2.0));
        static readonly Brush PhotoFill = Fz(new SolidColorBrush(Color.FromRgb(0xE9, 0xF4, 0xFB)));
        static readonly Pen PhotoFrame = Fz(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x9F, 0xD4)), 3.0));
        static readonly Brush PhotoHill = Fz(new SolidColorBrush(Color.FromRgb(0x6F, 0xC0, 0x8B)));
        static readonly Brush PhotoSun = Fz(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4D)));
        static readonly Brush BadgeFill = Fz(new SolidColorBrush(Color.FromRgb(0xFF, 0x62, 0x62)));
        static readonly Brush White = Fz(new SolidColorBrush(Colors.White));
        static readonly Brush Glow = Fz(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)));
        static readonly Brush BellyGlass = Fz(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xF6, 0xE2)));
        static readonly Brush BellyRim = Fz(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)));
        static readonly Brush FallbackBody = Fz(new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x5A)));

        static T Fz<T>(T f) where T : Freezable
        {
            if (f != null && f.CanFreeze) f.Freeze();
            return f;
        }

        static BitmapSource Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        public static BitmapSource Frame(int i)
        {
            if (!_loadTried)
            {
                _loadTried = true;
                string art = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "art");
                _frames = new BitmapSource[FrameFiles.Length];
                bool all = true;
                for (int k = 0; k < FrameFiles.Length; k++)
                {
                    _frames[k] = Load(Path.Combine(art, "anim", FrameFiles[k] + ".png"));
                    if (_frames[k] == null) all = false;
                }
                if (!all)
                {
                    // 有任何一帧缺失就退回单张图
                    _single = Load(Path.Combine(art, "nailong.png"));
                    _frames = null;
                }
            }
            return _frames != null ? _frames[i] : _single;
        }

        static double EaseOutBack(double s)
        {
            if (s <= 0) return 0;
            if (s >= 1) return 1;
            double t = s - 1;
            return 1 + 2.70158 * t * t * t + 1.70158 * t * t;
        }

        static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        static Point Lerp(Point a, Point b, double k)
        {
            return new Point(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k);
        }

        /// <param name="t">全局时间（秒），用于呼吸与晃动</param>
        /// <param name="stateTime">进入当前状态后的秒数，用于播放逐帧动作</param>
        public static void Draw(DrawingContext dc, PetState state, ClipKind kind,
            double t, double tilt, double releaseProgress, int itemCount, double stateTime)
        {
            bool copyHold = state == PetState.CopyHold;
            bool cutHold = state == PetState.CutHold;
            bool pasteCopy = state == PetState.PasteCopy;
            bool pasteCut = state == PetState.PasteCut;

            if (state == PetState.Idle) return;

            // ---------- 选帧 ----------
            F frame;
            double alpha = 1.0;
            Point itemPos = new Point(0, 0);
            double itemScale = 1.0;
            double itemAlpha = 0.0;
            bool showBelly = false;

            if (copyHold)
            {
                // 0 ~ 0.20s 空手抬起，0.20s 起文件"啵"地出现在手上
                frame = stateTime < 0.26 ? F.HandUp : F.Grip;
                if (stateTime >= 0.26)
                {
                    itemPos = HandGrip;
                    double sp = Clamp01((stateTime - 0.26) / 0.20);
                    itemScale = 0.42 + 0.33 * EaseOutBack(sp);
                    itemAlpha = Clamp01(sp * 3.0);
                }
            }
            else if (cutHold)
            {
                if (stateTime < 0.36)
                {
                    frame = F.Grip;
                    itemPos = HandGrip;
                    itemScale = 0.75;
                    itemAlpha = 1.0;
                }
                else if (stateTime < 0.66)
                {
                    // 手举到嘴边
                    frame = F.ToMouth;
                    double k = Clamp01((stateTime - 0.36) / 0.30);
                    itemPos = Lerp(HandGrip, new Point(152, 100), k);
                    itemScale = 0.75 - 0.27 * k;
                    itemAlpha = 1.0;
                }
                else if (stateTime < 0.90)
                {
                    // 张嘴，文件缩进嘴里
                    frame = F.ToMouth;
                    itemPos = new Point(152, 100);
                    double k = Clamp01((stateTime - 0.66) / 0.24);
                    itemScale = 0.48 - 0.40 * k;
                    itemAlpha = 1.0 - k;
                    if (k > 0.7) frame = F.Open;
                }
                else
                {
                    // 吞下去了：嘴闭上、肚子鼓起来
                    frame = F.Big;
                    showBelly = true;
                }
            }
            else if (pasteCopy)
            {
                double p = releaseProgress;
                frame = p < 0.34 ? F.Grip : F.HandUp;
                if (p < 0.34)
                {
                    itemPos = HandGrip;
                    itemScale = 0.75;
                    itemAlpha = 1.0;
                }
                else if (p < 0.80)
                {
                    double k = Clamp01((p - 0.34) / 0.46);
                    itemPos = Lerp(HandGrip, new Point(170, 200), k);
                    itemScale = 0.75 - 0.20 * k;
                    itemAlpha = 1.0 - 1.25 * k;
                }
                alpha = p < 0.72 ? 1.0 : Math.Max(0.0, 1.0 - (p - 0.72) / 0.28);
            }
            else if (pasteCut)
            {
                double p = releaseProgress;
                if (p < 0.16)
                {
                    frame = F.Big;
                    showBelly = true;
                }
                else if (p < 0.40)
                {
                    // 张嘴，东西从嘴里冒出来
                    frame = F.BigOpen;
                    double k = Clamp01((p - 0.16) / 0.24);
                    itemPos = Mouth;
                    itemScale = 0.30 + 0.70 * k;
                    itemAlpha = 0.45 + 0.55 * k;
                }
                else if (p < 0.80)
                {
                    frame = F.Open;
                    double k = Clamp01((p - 0.40) / 0.40);
                    itemPos = Lerp(Mouth, new Point(196, 58), k);
                    itemScale = 1.0 - 0.25 * k;
                    itemAlpha = 1.0 - k;
                }
                else
                {
                    frame = F.HandUp;
                }
                alpha = p < 0.75 ? 1.0 : Math.Max(0.0, 1.0 - (p - 0.75) / 0.25);
            }
            else
            {
                // Cancel
                frame = F.HandUp;
                alpha = Math.Max(0.0, 1.0 - releaseProgress * 1.15);
                if (kind != ClipKind.None && releaseProgress < 0.3)
                {
                    itemPos = HandGrip;
                    itemAlpha = 1.0 - releaseProgress * 3.0;
                }
            }

            if (alpha <= 0.01) return;

            // ---------- 姿态 ----------
            double bob;
            if (copyHold) bob = Math.Sin(t * 2.2) * 2.6;
            else if (cutHold) bob = Math.Sin(t * 1.7) * 1.8;
            else bob = 0;

            double rotate = tilt;
            if (cutHold) rotate += Math.Sin(t * 4.6) * 1.0;
            else if (copyHold) rotate += Math.Sin(t * 2.2) * 1.0;

            // 吞下去的瞬间来一下"顶一下"的弹跳，配合肚子变大
            double squash = 1.0;
            if (cutHold && stateTime > 0.88 && stateTime < 1.30)
            {
                double k = (stateTime - 0.88) / 0.42;
                squash = 1.0 + 0.04 * Math.Sin(k * Math.PI);
                bob += 3.0 * Math.Sin(k * Math.PI);
            }
            if (pasteCopy)
                squash = 1.0 - 0.04 * Math.Sin(releaseProgress * Math.PI);

            dc.PushOpacity(alpha);
            dc.PushTransform(new RotateTransform(rotate, AnchorX, SpriteBottom - 60));
            dc.PushTransform(new TranslateTransform(0, bob));
            dc.PushTransform(new ScaleTransform(2.0 - squash, squash, AnchorX, SpriteBottom));

            DrawFrame(dc, frame);

            // ---------- 肚子里的东西 ----------
            if (showBelly && kind != ClipKind.None)
            {
                DrawBelly(dc, kind, itemCount, t);
            }

            // ---------- 手上的 / 嘴边的东西 ----------
            if (kind != ClipKind.None && itemAlpha > 0.02 && itemPos.X > 0)
            {
                dc.PushOpacity(Clamp01(itemAlpha));
                DrawContent(dc, kind, itemCount, itemPos, itemScale, false);
                dc.Pop();
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        static void DrawFrame(DrawingContext dc, F f)
        {
            var sp = Frame((int)f);
            if (sp != null)
            {
                var rect = new Rect(FrameLeft, FrameTop, FrameW, FrameH);
                dc.DrawImage(sp, rect);
            }
            else
            {
                dc.DrawEllipse(FallbackBody, null, new Point(AnchorX, 170), 78, 76);
                dc.DrawEllipse(FallbackBody, null, new Point(AnchorX, 98), 56, 60);
            }
        }

        static void DrawBelly(DrawingContext dc, ClipKind kind, int count, double t)
        {
            double pulse = 1.0 + 0.03 * Math.Sin(t * 3.0);
            double bx = 141, by = 176, brx = 45 * pulse, bry = 35 * pulse;

            dc.DrawEllipse(BellyGlass, null, new Point(bx, by), brx, bry);
            // 腹腔边缘画得很淡，不抢戏

            dc.PushOpacity(0.40);
            DrawContent(dc, kind, count, new Point(bx, by + 2), 0.70, true);
            dc.Pop();

            // 绕圈的光点
            for (int i = 0; i < 3; i++)
            {
                double a = t * 1.8 + i * 2.0944;
                double px = bx + Math.Cos(a) * brx * 0.62;
                double py = by + Math.Sin(a) * bry * 0.48;
                double r = 3.0 + Math.Sin(a * 2.0) * 1.0;
                dc.DrawEllipse(Glow, null, new Point(px, py), r, r);
            }
        }

        static void DrawContent(DrawingContext dc, ClipKind kind, int count, Point center, double scale, bool inBelly)
        {
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            dc.PushTransform(new ScaleTransform(scale, scale));

            if (kind == ClipKind.Files) DrawFileStack(dc, count, inBelly);
            else if (kind == ClipKind.Image) DrawPhoto(dc);
            else if (kind == ClipKind.Text) DrawTextStrip(dc);

            dc.Pop();
            dc.Pop();
        }

        static void DrawFileStack(DrawingContext dc, int count, bool inBelly)
        {
            int layers = count >= 3 ? 3 : (count < 1 ? 1 : count);
            double w = 48, h = 58;
            for (int i = layers - 1; i >= 0; i--)
            {
                double dx = i * 5.0 - 5.0, dy = i * 4.0 - 4.0;
                DrawDocIcon(dc, new Point(dx, dy), w, h);
            }
            if (count > 1 && !inBelly) DrawBadge(dc, count, new Point(26, -18));
        }

        static void DrawDocIcon(DrawingContext dc, Point c, double w, double h)
        {
            var r = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
            dc.DrawRectangle(PaperFill, PaperEdge, r);

            double fold = 13;
            var fg = new PathGeometry();
            var f = new PathFigure();
            f.StartPoint = new Point(r.Right - fold, r.Top);
            f.Segments.Add(new LineSegment(new Point(r.Right, r.Top + fold), true));
            f.Segments.Add(new LineSegment(new Point(r.Right - fold, r.Top + fold), true));
            f.Segments.Add(new LineSegment(new Point(r.Right - fold, r.Top), true));
            fg.Figures.Add(f);
            fg.Freeze();
            dc.DrawGeometry(FoldFill, null, fg);

            for (int i = 0; i < 4; i++)
            {
                double y = r.Top + h * 0.34 + i * 8.0;
                double right = r.Right - 9 - (i % 2 == 1 ? 7 : 0);
                dc.DrawLine(LinePen, new Point(r.Left + 8, y), new Point(right, y));
            }
        }

        static void DrawBadge(DrawingContext dc, int count, Point c)
        {
            string label = count > 99 ? "99+" : ("+" + count);
            var ft = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                13.0, White, 1.0);

            double bw = Math.Max(26.0, ft.Width + 12.0), bh = 21.0;
            var rect = new Rect(c.X - bw / 2, c.Y - bh / 2, bw, bh);
            dc.DrawRoundedRectangle(BadgeFill, null, rect, 10.5, 10.5);
            dc.DrawText(ft, new Point(rect.X + (bw - ft.Width) / 2, rect.Y + (bh - ft.Height) / 2));
        }

        static void DrawPhoto(DrawingContext dc)
        {
            double w = 56, h = 44;
            dc.DrawRoundedRectangle(PhotoFill, PhotoFrame, new Rect(-w / 2, -h / 2, w, h), 4, 4);
            dc.DrawEllipse(PhotoSun, null, new Point(-w / 2 + 15, -h / 2 + 13), 5, 5);

            var hill = new PathGeometry();
            var f = new PathFigure();
            f.StartPoint = new Point(-w / 2 + 4, h / 2 - 4);
            f.Segments.Add(new LineSegment(new Point(0, 0), true));
            f.Segments.Add(new LineSegment(new Point(w / 2 - 12, h / 2 - 4), true));
            hill.Figures.Add(f);
            var f2 = new PathFigure();
            f2.StartPoint = new Point(2, h / 2 - 4);
            f2.Segments.Add(new LineSegment(new Point(13, 5), true));
            f2.Segments.Add(new LineSegment(new Point(w / 2 - 4, h / 2 - 4), true));
            hill.Figures.Add(f2);
            hill.Freeze();
            dc.DrawGeometry(PhotoHill, null, hill);
        }

        static void DrawTextStrip(DrawingContext dc)
        {
            double w = 52, h = 36;
            dc.DrawRoundedRectangle(PaperFill, PaperEdge, new Rect(-w / 2, -h / 2, w, h), 5, 5);
            for (int i = 0; i < 3; i++)
            {
                double y = -h / 2 + 11 + i * 9;
                double right = w / 2 - 8 - (i == 2 ? 15 : 0);
                dc.DrawLine(LinePen, new Point(-w / 2 + 7, y), new Point(right, y));
            }
        }
    }
}
