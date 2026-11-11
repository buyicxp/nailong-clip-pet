using System;
using System.Windows;
using System.Windows.Media;

namespace NailongClipPet
{
    /// <summary>
    /// 负责把奶龙画到窗口上的绘制层。
    /// </summary>
    public class PetCanvas : FrameworkElement
    {
        public PetState State = PetState.Idle;
        public ClipKind Kind = ClipKind.None;
        public int Count = 0;
        public double Tilt = 0.0;
        public double Time = 0.0;
        public double ReleaseProgress = 0.0;
        public double StateTime = 0.0;
        public long StateStartTick = 0;

        public PetCanvas()
        {
            // 精灵图要缩到 200 像素左右，必须用高质量插值，否则边缘会锯齿
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
        }

        protected override void OnRender(DrawingContext dc)
        {
            NailongArt.Draw(dc, State, Kind, Time, Tilt, ReleaseProgress, Count, StateTime);

            // 替代光标时，在真实光标（= 奶龙手上）画一个小瞄点
            if (PetWindow.HideSystemCursor &&
                (State == PetState.CopyHold || State == PetState.CutHold))
            {
                double px = PetWindow.HotspotX;
                double py = PetWindow.HotspotY;
                dc.DrawEllipse(Brushes.Black, null, new System.Windows.Point(px, py), 5.5, 5.5);
                dc.DrawEllipse(Brushes.White, null, new System.Windows.Point(px, py), 3.6, 3.6);
            }
        }
    }
}
