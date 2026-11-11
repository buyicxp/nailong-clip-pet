using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace NailongClipPet
{
    /// <summary>
    /// 桌面右上角的「运行中」控制面板：显示程序在跑、当前手里拿着什么，右上角 ✕ 直接退出程序。
    /// 这个窗口是唯一会「点得到」的窗口——宠物本体是全透明且鼠标穿透的，靠它才有地方关掉程序。
    /// </summary>
    public class PanelWindow : Window
    {
        // 窗口标题要唯一且稳定，第二个实例靠 FindWindow 靠它找到已运行的实例
        public const string WindowTitle = "奶龙剪贴宠物 · 运行中";
        // 第二个实例启动时给已运行的实例发这个消息，让它把面板顶到前面来
        public const int WmShowPanel = 0x8001;

        readonly TextBlock _status;
        bool _placed;

        public PanelWindow(string assetsDir)
        {
            Title = WindowTitle;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;

            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 6, 10)
            };
            border.Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.18,
                Color = Colors.Black
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 左边：奶龙小头像
            var img = new Image { Height = 42, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            try
            {
                img.Source = new BitmapImage(new Uri(System.IO.Path.Combine(assetsDir, "art", "nailong.png")));
            }
            catch { }
            Grid.SetColumn(img, 0);
            Grid.SetRowSpan(img, 2);

            // 中间：标题 + 状态
            var title = new TextBlock
            {
                Text = "奶龙剪贴宠物",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
            };
            _status = new TextBlock
            {
                Text = "运行中 · 空闲",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 3, 0, 0)
            };
            var texts = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(title);
            texts.Children.Add(_status);
            Grid.SetColumn(texts, 1);
            Grid.SetRowSpan(texts, 2);

            // 右边：✕ 关闭
            var cross = new TextBlock
            {
                Text = "✕",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var close = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
                Child = cross
            };
            close.MouseLeftButtonDown += delegate { PetRuntime.Instance.ExitApp(); };
            close.MouseEnter += delegate
            {
                close.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE4, 0xE4));
                cross.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0x3C, 0x3C));
            };
            close.MouseLeave += delegate
            {
                close.Background = Brushes.Transparent;
                cross.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));
            };
            Grid.SetColumn(close, 2);
            Grid.SetRowSpan(close, 2);

            grid.Children.Add(img);
            grid.Children.Add(texts);
            grid.Children.Add(close);
            border.Child = grid;

            border.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                try { DragMove(); } catch { }
            };

            Content = border;

            Loaded += delegate { PlaceTopRight(); };
        }

        /// <summary>摆到工作区右上角（只摆一次，之后用户可以自己拖动）</summary>
        void PlaceTopRight()
        {
            if (_placed) return;
            _placed = true;
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth - 24;
            Top = wa.Top + 24;
        }

        public void SetStatus(string text)
        {
            if (text != null && _status != null) _status.Text = text;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = PresentationSource.FromVisual(this) as HwndSource;
            if (src != null) src.AddHook(OnWndProc);
        }

        IntPtr OnWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmShowPanel)
            {
                // 又双击了一次 exe：把面板显示出来并顶到最前
                Show();
                Topmost = false;
                Topmost = true;
                Activate();
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
