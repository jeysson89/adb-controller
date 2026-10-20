using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AdbController
{
    public class ToastWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public ToastWindow(string title, string message, double left, double top, int durationMs = 3000)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Width = 340;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 32, 34, 44)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 120)),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.5
                }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                FontSize = 13
            });
            stack.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 210, 225)),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            border.Child = stack;
            Content = border;
            Left = left;
            Top = top;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
            Loaded += (_, _) => _timer.Start();
        }
    }
}
