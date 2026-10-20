using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdbController
{
    /// <summary>
    /// Диалог удаления пакета для текущего пользователя.
    /// Галочка «сохранить данные» включена по умолчанию: тогда удаление
    /// полностью обратимо — приложение вернётся вместе со своими данными.
    /// </summary>
    public class RemovePackageDialog : Window
    {
        private readonly CheckBox _keepData;

        public RemovePackageDialog(string package, string title, int packageCount)
        {
            Title = "Удаление для пользователя";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(32, 34, 44));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };

            root.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            });

            root.Children.Add(new TextBlock
            {
                Text = packageCount > 1
                    ? $"Будет удалено пакетов: {packageCount}"
                    : package,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 210, 225)),
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            });

            root.Children.Add(new TextBlock
            {
                Text = "Приложение исчезнет с устройства. Система и раздел /system не затрагиваются — " +
                       "пакет можно вернуть кнопкой «Восстановить».",
                Foreground = new SolidColorBrush(Color.FromRgb(160, 165, 180)),
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            });

            _keepData = new CheckBox
            {
                Content = "Сохранить данные приложения (можно вернуть без потерь)",
                IsChecked = true,
                Margin = new Thickness(0, 14, 0, 0),
                Foreground = Brushes.White
            };
            root.Children.Add(_keepData);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var ok = MakeButton("Удалить", 110);
            ok.IsDefault = true;
            ok.Click += (_, _) => { DialogResult = true; Close(); };

            var cancel = MakeButton("Отмена", 100);
            cancel.IsCancel = true;
            cancel.Click += (_, _) => { DialogResult = false; Close(); };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(32, 34, 44)),
                CornerRadius = new CornerRadius(10),
                Child = root
            };
        }

        /// <summary>true — данные приложения сохраняются (pm uninstall -k).</summary>
        public bool KeepData => _keepData.IsChecked == true;

        private static Button MakeButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromRgb(52, 56, 70)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 120)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }
    }
}
