using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AdbController
{
    public class FileEntry
    {
        public string Name { get; set; } = "";
        public bool IsDir { get; set; }
        public long Size { get; set; }
        public string Display
        {
            get
            {
                if (IsDir) return "\U0001F4C1 " + Name;
                string s;
                if (Size >= 1024L * 1024 * 1024) s = $"{Size / 1024.0 / 1024 / 1024:0.##} GB";
                else if (Size >= 1024 * 1024) s = $"{Size / 1024.0 / 1024:0.##} MB";
                else if (Size >= 1024) s = $"{Size / 1024.0:0.##} KB";
                else s = $"{Size} B";
                return "\U0001F4C4 " + Name + "   (" + s + ")";
            }
        }
    }

        public class PackageEntry
    {
        public string Name { get; set; } = "";
        public bool IsFrozen { get; set; }
        public bool IsSuspicious { get; set; }

        /// <summary>Пакет удалён для текущего пользователя (pm uninstall --user 0).</summary>
        public bool IsRemoved { get; set; }

        public string Display
        {
            get
            {
                var prefix = IsRemoved ? "\U0001F5D1 " : (IsFrozen ? "\u2744 \U0001F4E6 " : "\U0001F4E6 ");
                var suffix = IsSuspicious ? "   \u26A0" : "";
                return prefix + Name + suffix;
            }
        }
    }

    public class ApkFileEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
        public string Display
        {
            get
            {
                string s;
                if (Size >= 1024L * 1024 * 1024) s = $"{Size / 1024.0 / 1024 / 1024:0.##} GB";
                else if (Size >= 1024 * 1024) s = $"{Size / 1024.0 / 1024:0.##} MB";
                else if (Size >= 1024) s = $"{Size / 1024.0:0.##} KB";
                else s = $"{Size} B";
                return "\U0001F4E6 " + Name + "   (" + s + ")";
            }
        }
    }
    public class TopEntry
    {
        public string Rank { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class WatchEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public bool IsFlashing { get; set; }

        public string Badge => IsFlashing ? $"\U0001F525 {Count}\u00D7" : $"{Count}\u00D7";

        public System.Windows.Media.Brush BadgeColor => IsFlashing
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 57, 53))
            : System.Windows.Media.Brushes.Gray;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public void NotifyChanged()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFlashing)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Badge)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BadgeColor)));
        }
    }

    public partial class MainWindow : Window
    {
        private System.Windows.Threading.DispatcherTimer? _screenTimer;
        private byte[]? _lastScreenshot;
        private bool _screenBusy;

        private int _deviceWidth = 1080;
        private int _deviceHeight = 2400;
        private double _zoomScale = 0.3;

        private Point? _mouseDownPos;
        private DateTime _mouseDownTime;

        private string _localPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private string _remotePath = "/sdcard/";

        public MainWindow()
        {
            InitializeComponent();

            LstWatchDetected.ItemsSource = _watchDetected;
            LstBloat.ItemsSource = _bloatItems;
            SldZoom.ValueChanged += SldZoom_Changed;

            ApplySettings();

            Loaded += async (_, _) =>
            {
                try
                {
                    await RefreshDevicesAsync();
                    RefreshLocal();
                    await RefreshMonitorAsync();

                    // Восстанавливаем галочки «Авто» после загрузки окна,
                    // чтобы таймеры стартовали корректно
                    if (ChkMonAuto != null && _settings.MonAuto)
                        ChkMonAuto.IsChecked = true;
                    if (ChkAutoRefresh != null && _settings.ScreenAuto)
                        ChkAutoRefresh.IsChecked = true;
                }
                catch (Exception ex)
                {
                    TxtStatus.Text = "Ошибка при загрузке: " + ex.Message;
                }
            };

            Closing += (_, _) => SaveSettings();
        }

        private void ApplySettings()
        {
            _isDark = _settings.IsDark;
            var uri = _isDark
                ? new Uri("Themes/Dark.xaml", UriKind.Relative)
                : new Uri("Themes/Light.xaml", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
            if (BtnTheme != null)
                BtnTheme.Content = _isDark ? "☀ Светлая" : "🌙 Тёмная";

            _zoomScale = _settings.ZoomScale;
            if (SldZoom != null) SldZoom.Value = _zoomScale;

            if (TxtRefreshMs != null) TxtRefreshMs.Text = _settings.ScreenRefreshMs.ToString();
            if (TxtMonInterval != null) TxtMonInterval.Text = _settings.MonitorIntervalMs.ToString();
            if (TxtWatchInterval != null) TxtWatchInterval.Text = _settings.WatchIntervalMs.ToString();
            if (ChkWatchNotify != null) ChkWatchNotify.IsChecked = _settings.WatchNotify;

            if (TxtWifiIp != null && !string.IsNullOrEmpty(_settings.WifiIp))
                TxtWifiIp.Text = _settings.WifiIp;
            if (TxtWifiPort != null && !string.IsNullOrEmpty(_settings.WifiPort))
                TxtWifiPort.Text = _settings.WifiPort;

            if (TxtApkFolder != null && !string.IsNullOrEmpty(_settings.LastApkFolder))
                TxtApkFolder.Text = _settings.LastApkFolder;

            if (_settings.WinWidth >= 900) Width = _settings.WinWidth;
            if (_settings.WinHeight >= 600) Height = _settings.WinHeight;
        }

        private void SaveSettings()
        {
            _settings.IsDark = _isDark;
            _settings.ZoomScale = _zoomScale;
            if (TxtRefreshMs != null && int.TryParse(TxtRefreshMs.Text, out var r1)) _settings.ScreenRefreshMs = r1;
            if (TxtMonInterval != null && int.TryParse(TxtMonInterval.Text, out var r2)) _settings.MonitorIntervalMs = r2;
            if (TxtWatchInterval != null && int.TryParse(TxtWatchInterval.Text, out var r3)) _settings.WatchIntervalMs = r3;
            if (ChkWatchNotify != null) _settings.WatchNotify = ChkWatchNotify.IsChecked == true;
            _settings.MonAuto = ChkMonAuto?.IsChecked == true;
            _settings.ScreenAuto = ChkAutoRefresh?.IsChecked == true;
            if (TxtWifiIp != null) _settings.WifiIp = TxtWifiIp.Text;
            if (TxtWifiPort != null) _settings.WifiPort = TxtWifiPort.Text;
            if (TxtApkFolder != null) _settings.LastApkFolder = TxtApkFolder.Text;

            // Серийник не затираем, если сейчас ничего не выбрано: устройство может
            // быть временно отключено, и при следующем запуске его нужно восстановить.
            _settings.LastDeviceSerial = SelectedDevice ?? _settings.LastDeviceSerial;

            _settings.WinWidth = Width;
            _settings.WinHeight = Height;

            SettingsManager.Save(_settings);
        }

        // =============== APPWATCH ===============
        private System.Windows.Threading.DispatcherTimer? _watchTimer;
        private readonly AppSettings _settings = SettingsManager.Load();
        private string? _lastForegroundPkg;
        private DateTime _lastSwitchTime = DateTime.Now;
        private readonly System.Collections.ObjectModel.ObservableCollection<WatchEntry> _watchDetected = new();
        private readonly System.Collections.Generic.Dictionary<string, WatchEntry> _watchMap = new();
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DateTime>> _watchTimestamps = new();
        private void BtnWatchToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_watchTimer == null) StartWatch();
            else StopWatch();
        }

        private void BtnWatchApply_Click(object sender, RoutedEventArgs e)
        {
            if (_watchTimer != null) { StopWatch(); StartWatch(); }
        }

        private void StartWatch()
        {
            if (SelectedDevice == null) { MessageBox.Show("Выберите устройство"); return; }

            int interval = 500;
            if (int.TryParse(TxtWatchInterval.Text, out var v) && v >= 200 && v <= 10000)
                interval = v;

            _watchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(interval)
            };
            _watchTimer.Tick += WatchTimer_Tick;
            _watchTimer.Start();

            BtnWatchToggle.Content = "⏸ Остановить";
            TxtWatchStatus.Text = $"каждые {interval} мс";
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ▶ Старт (интервал {interval} мс)\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        private void StopWatch()
        {
            _watchTimer?.Stop();
            _watchTimer = null;
            BtnWatchToggle.Content = "▶ Начать отслеживание";
            TxtWatchStatus.Text = "";
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ⏸ Стоп\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        // Пока предыдущий опрос не завершился, следующий не запускаем —
        // иначе на медленном устройстве adb-процессы накапливаются.
        private bool _watchBusy;

        private async void WatchTimer_Tick(object? sender, EventArgs e)
        {
            if (_watchBusy) return;
            if (SelectedDevice == null) return;
            var device = SelectedDevice;
            _watchBusy = true;

            try
            {
                var pkg = await Task.Run(() => GetForegroundPackage(device));

                if (string.IsNullOrEmpty(pkg) || pkg == "null") return;

                TxtWatchCurrent.Text = pkg;

                if (pkg != _lastForegroundPkg)
                {
                    // Считаем, сколько предыдущий пакет был активным
                    var prevPkg = _lastForegroundPkg;
                    var elapsed = DateTime.Now - _lastSwitchTime;
                    var elapsedText = FormatDuration(elapsed);

                    if (prevPkg != null && elapsed.TotalSeconds >= 1)
                    {
                        TxtWatchLog.AppendText(
                            $"[{DateTime.Now:HH:mm:ss}] → {pkg}    (пред. {elapsedText})\r\n");
                    }
                    else
                    {
                        TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] → {pkg}\r\n");
                    }
                    TxtWatchLog.ScrollToEnd();

                    _lastForegroundPkg = pkg;
                    _lastSwitchTime = DateTime.Now;

                    // Регистрируем появление в счётчике
                    RegisterWatchAppearance(pkg);

                    // Одно уведомление — только для не-системных пакетов
                    if (ChkWatchNotify != null && ChkWatchNotify.IsChecked == true &&
                        !IsSystemPackageForNotify(pkg))
                    {
                        ShowToast("Детектор", pkg);
                    }

                    // Ограничение журнала — последние 500 строк
                    if (TxtWatchLog.LineCount > 500)
                    {
                        var lines = TxtWatchLog.Text.Split('\n');
                        var tail = lines.Skip(Math.Max(0, lines.Length - 400)).ToArray();
                        TxtWatchLog.Text = string.Join("\n", tail);
                        TxtWatchLog.ScrollToEnd();
                    }
                }
            }
            finally
            {
                _watchBusy = false;
            }
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
            if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m{t.Seconds:00}s";
            return $"{(int)t.TotalHours}h{t.Minutes:00}m";
        }


        // Запоминаем последнюю сработавшую команду — при следующем запросе пробуем её первой
        private string? _lastWorkingCmd = null;

        private void ShowToast(string title, string message)
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                double w = 340, h = 80;
                double x = workArea.Right - w - 16;
                double y = workArea.Bottom - h - 16;

                var toast = new ToastWindow(title, message, x, y);
                toast.Show();
            }
            catch { }
        }

        private static bool IsSystemPackageForNotify(string pkg)
        {
            if (string.IsNullOrEmpty(pkg)) return true;
            var lower = pkg.ToLowerInvariant();
            return lower == "com.android.launcher3"
                || lower.StartsWith("com.android.launcher")
                || lower == "com.android.systemui"
                || lower.StartsWith("com.android.inputmethod")
                || lower.StartsWith("com.google.android.inputmethod")
                || lower.StartsWith("android")
                || lower.StartsWith("com.android.providers.")
                || lower.StartsWith("com.android.server")
                || lower.Contains("launcher");
        }
        private string? GetForegroundPackage(string device)
        {
            // Быстрые и надёжные команды. Порядок: сначала пробуем ту, что сработала в прошлый раз
            var fastCommands = new (string cmd, Func<string, string?> extractor)[]
            {
                ("shell dumpsys activity activities", t => TryExtractPackage(t,
                    @"topResumedActivity=ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"mResumedActivity:?\s+ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"ResumedActivity:\s+ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"realActivity=([a-zA-Z0-9_.]+)/")),

                ("shell dumpsys window", t => TryExtractPackage(t,
                    @"mCurrentFocus=Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"mFocusedApp=ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"mFocusedWindow=Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"Window\{[0-9a-f]+\su\d+\s+([a-zA-Z0-9_.]+)/")),

                ("shell dumpsys window displays", t => TryExtractPackage(t,
                    @"mCurrentFocus=Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
                    @"mFocusedApp=ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/")),

                ("shell dumpsys activity recents", t => TryExtractPackage(t,
                    @"Recent #0:.*?\bA=\d+:([a-zA-Z0-9_.]+)",
                    @"A=\d+:([a-zA-Z0-9_.]+)[\s/}]",
                    @"Recent #0:.*?\bcmp=([a-zA-Z0-9_.]+)/")),
            };

            // Сначала пробуем команду, которая сработала в прошлый раз
            if (_lastWorkingCmd != null)
            {
                foreach (var fc in fastCommands)
                {
                    if (fc.cmd != _lastWorkingCmd) continue;
                    var outText = RunAdbOnDevice(device, fc.cmd, 4000);
                    var pkg = fc.extractor(outText ?? "");
                    if (pkg != null) return pkg;
                    _lastWorkingCmd = null;
                    break;
                }
            }

            // Перебираем остальные
            foreach (var fc in fastCommands)
            {
                if (fc.cmd == _lastWorkingCmd) continue;
                var outText = RunAdbOnDevice(device, fc.cmd, 4000);
                var pkg = fc.extractor(outText ?? "");
                if (pkg != null)
                {
                    _lastWorkingCmd = fc.cmd;
                    return pkg;
                }
            }

            return null;
        }

        /// <summary>
        /// Перебирает регулярки по очереди и возвращает первое совпадение.
        /// Отсекает типичные ложные срабатывания (system_server, launcher/systemui при рекламе и т.п.).
        /// </summary>
        private static string? TryExtractPackage(string text, params string[] patterns)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var pattern in patterns)
            {
                foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Multiline))
                {
                    if (!m.Success) continue;
                    var pkg = m.Groups[1].Value.Trim();
                    if (IsValidPackageName(pkg))
                        return pkg;
                }
            }
            return null;
        }

        private static bool IsValidPackageName(string pkg)
        {
            if (string.IsNullOrWhiteSpace(pkg)) return false;
            if (pkg.Length < 3) return false;
            if (pkg == "android" || pkg == "null") return false;

            // Должен содержать минимум одну точку и начинаться с буквы
            if (!char.IsLetter(pkg[0])) return false;
            if (!pkg.Contains('.')) return false;

            // Только допустимые символы
            foreach (var c in pkg)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_')
                    return false;

            // Отсекаем мусорные строки, которые иногда попадают в regex
            var lower = pkg.ToLowerInvariant();
            var skip = new[] {
                "window", "activity", "task", "process", "service",
                "intent", "com.android.internal", "com.android.server",
                "java.", "javax.", "android.", "dalvik."
            };
            foreach (var s in skip)
                if (lower.StartsWith(s)) return false;

            // Должен иметь минимум 3 части через точку (например, com.x.y)
            if (pkg.Count(c => c == '.') < 2) return false;

            return true;
        }

        // В списке лежат объекты WatchEntry, а не строки — прежний cast в string
        // всегда давал null, и кнопки заморозки/удаления не работали вообще.
        private WatchEntry? GetSelectedWatchPkg() => LstWatchDetected.SelectedItem as WatchEntry;

        private async void BtnWatchFreeze_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedWatchPkg();
            if (entry == null) { MessageBox.Show("Выберите приложение из списка"); return; }
            if (SelectedDevice == null) return;
            var pkg = entry.Name;
            if (MessageBox.Show($"Заморозить \"{pkg}\"?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            var device = SelectedDevice;
            var r = await RunAdbOnDeviceAsync(device, $"shell pm disable-user --user 0 {pkg}");
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ❄ Заморозка {pkg}: {r.Text.Trim()}\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        private async void BtnWatchUnfreeze_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedWatchPkg();
            if (entry == null) { MessageBox.Show("Выберите приложение из списка"); return; }
            if (SelectedDevice == null) return;
            var pkg = entry.Name;

            var device = SelectedDevice;
            var r = await RunAdbOnDeviceAsync(device, $"shell pm enable {pkg}");
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ☀ Разморозка {pkg}: {r.Text.Trim()}\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        private async void BtnWatchUninstall_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedWatchPkg();
            if (entry == null) { MessageBox.Show("Выберите приложение из списка"); return; }
            if (SelectedDevice == null) return;
            var pkg = entry.Name;
            if (MessageBox.Show($"Удалить \"{pkg}\" полностью? Данные будут потеряны.",
                "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            var device = SelectedDevice;
            var r = await RunAdbOnDeviceAsync(device, $"uninstall {pkg}");
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] 🗑 Удаление {pkg}: {r.Text.Trim()}\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        private async void BtnWatchInfo_Click(object sender, RoutedEventArgs e)
        {
            var entry = GetSelectedWatchPkg();
            if (entry == null) { MessageBox.Show("Выберите приложение из списка"); return; }
            if (SelectedDevice == null) return;

            var info = await RunAdbOnDeviceAsync(SelectedDevice, $"shell dumpsys package {entry.Name}");
            var text = info.Text;
            TxtWatchLog.AppendText($"[{DateTime.Now:HH:mm:ss}] ℹ Инфо {entry.Name}:\r\n");
            TxtWatchLog.AppendText(text.Length > 2000 ? text.Substring(0, 2000) + "\r\n..." : text + "\r\n");
            TxtWatchLog.ScrollToEnd();
        }

        // ===== Экстракторы для каждой команды (используются и в основном методе, и в диагностике) =====
        private static string? ExtractFromActivities(string t) => TryExtractPackage(t,
            @"topResumedActivity=ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"mResumedActivity:?\s+ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"ResumedActivity:\s+ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"realActivity=([a-zA-Z0-9_.]+)/",
            @"\* Task\{[^}]*?\s+([a-zA-Z0-9_.]+)/");

        private static string? ExtractFromWindow(string t) => TryExtractPackage(t,
            @"mCurrentFocus=Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"mFocusedApp=ActivityRecord\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"mFocusedWindow=Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"imeLayeringTarget in display# \d+ Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"imeInputTarget in display# \d+ Window\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"mCurrentFocus=[^ ]*\s+([a-zA-Z0-9_.]+)/",
            @"mFocusedApp=[^ ]*\s+([a-zA-Z0-9_.]+)/",
            @"mFocusedWindow=[^ ]*\s+([a-zA-Z0-9_.]+)/",
            @"Window\{[0-9a-f]+\su\d+\s+([a-zA-Z0-9_.]+)/");

        private static string? ExtractFromRecents(string t) => TryExtractPackage(t,
            @"A=\d+:([a-zA-Z0-9_.]+)[\s/}]",
            @"Recent #0:.*?\bA=\d+:([a-zA-Z0-9_.]+)",
            @"Recent #0:.*?\bcmp=([a-zA-Z0-9_.]+)/",
            @"Recent #0:.*?\bbaseIntent=Intent\s+\{[^}]*?\s+([a-zA-Z0-9_.]+)/",
            @"Recent #0:.*?\brealActivity=([a-zA-Z0-9_.]+)/");

        private static string? ExtractFromProcesses(string t) => TryExtractPackage(t,
            @"ProcessRecord\{[0-9a-f]+\s+\d+:([a-zA-Z0-9_.]+)/",
            @"ProcessRecord\{[0-9a-f]+\s+\d+:([a-zA-Z0-9_.]+)\s",
            @"ProcessRecord\{[^}]*?\s+\d+:([a-zA-Z0-9_.]+)/",
            @"top-activity.*?\s+([a-zA-Z0-9_.]+)/[a-zA-Z0-9_.$]+");

        private static string? ExtractFromTop(string t) => TryExtractPackage(t,
            @"ACTIVITY\s+([a-zA-Z0-9_.]+)/",
            @"^\s*ACTIVITY.*?([a-zA-Z0-9_.]+)/",
            @"TASK\s+([a-zA-Z0-9_.]+):");

        private async void BtnWatchDiag_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDevice == null) { MessageBox.Show("Выберите устройство"); return; }
            var device = SelectedDevice;

            TxtWatchLog.AppendText($"\r\n[{DateTime.Now:HH:mm:ss}] === ДИАГНОСТИКА ДЕТЕКТОРА ===\r\n");
            TxtWatchLog.AppendText($"Устройство: {device}\r\n");

            var tests = new (string name, string cmd, Func<string, string?> extractor)[]
            {
                ("dumpsys activity activities", "shell dumpsys activity activities", ExtractFromActivities),
                ("dumpsys window",              "shell dumpsys window",              ExtractFromWindow),
                ("dumpsys window windows",      "shell dumpsys window windows",      ExtractFromWindow),
                ("dumpsys window displays",     "shell dumpsys window displays",     ExtractFromWindow),
                ("dumpsys activity recents",    "shell dumpsys activity recents",    ExtractFromRecents),
            };

            // Каждую команду выполняем ровно один раз: результат используется
            // и для отчёта, и для сохранения сырого вывода в файл.
            var rawOutputs = new Dictionary<string, string>();
            foreach (var t in tests)
            {
                var res = await RunAdbOnDeviceAsync(device, t.cmd, 4000);
                var outText = res.Output;
                rawOutputs[t.name] = outText;
                var pkg = t.extractor(outText ?? "");
                var status = pkg != null ? $"✔ {pkg}" : "✖ (не распознано)";
                var size = outText?.Length ?? 0;
                TxtWatchLog.AppendText($"  {t.name,-30} → {status}  [{size} байт]\r\n");
            }

            TxtWatchLog.AppendText($"Проверка универсальным методом: ");
            var final = await Task.Run(() => GetForegroundPackage(device));
            TxtWatchLog.AppendText((final ?? "(null)") + "\r\n");

            // Сохраняем сырые выводы — на случай, если что-то не подхватилось
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "AdbWatchDiag");
                Directory.CreateDirectory(dir);

                foreach (var t in tests)
                {
                    var fname = t.name.Replace(" ", "_").Replace("dumpsys_", "") + ".txt";
                    File.WriteAllText(Path.Combine(dir, fname), rawOutputs.TryGetValue(t.name, out var raw) ? raw : "");
                }

                var summary =
                    $"Устройство: {device}\r\n" +
                    $"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                    $"Итог: {final ?? "(null)"}\r\n";
                File.WriteAllText(Path.Combine(dir, "_summary.txt"), summary);

                TxtWatchLog.AppendText($"\r\n📁 Файлы сохранены: {dir}\r\n");
            }
            catch (Exception ex)
            {
                TxtWatchLog.AppendText($"⚠ Не удалось сохранить файлы: {ex.Message}\r\n");
            }

            TxtWatchLog.ScrollToEnd();
        }
        private void RegisterWatchAppearance(string pkg)
        {
            var now = DateTime.Now;

            // 1. Счётчик суммарный
            if (!_watchMap.TryGetValue(pkg, out var entry))
            {
                entry = new WatchEntry { Name = pkg, Count = 0 };
                _watchMap[pkg] = entry;
                _watchDetected.Insert(0, entry);
            }
            entry.Count++;

            // 2. Список времени появлений (для 🔥)
            if (!_watchTimestamps.TryGetValue(pkg, out var times))
            {
                times = new System.Collections.Generic.List<DateTime>();
                _watchTimestamps[pkg] = times;
            }
            times.Add(now);

            // Оставляем только события за последнюю минуту
            var cutoff = now.AddMinutes(-1);
            times.RemoveAll(t => t < cutoff);

            // 3. Помечаем 🔥, если за минуту 5+ появлений
            entry.IsFlashing = times.Count >= 5;

            entry.NotifyChanged();

            // 4. Пересортировка: сначала мигающие, потом по количеству
            var sorted = _watchDetected
                .OrderByDescending(x => x.IsFlashing)
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.Name)
                .ToList();

            // Проверяем, нужно ли пересортировать (сравниваем порядок)
            bool changed = false;
            for (int i = 0; i < sorted.Count && i < _watchDetected.Count; i++)
            {
                if (sorted[i] != _watchDetected[i]) { changed = true; break; }
            }

            if (changed)
            {
                // Пересборка коллекции сбрасывает выделение, поэтому запоминаем
                // выбранный пакет и восстанавливаем его после сортировки.
                var selectedName = (LstWatchDetected.SelectedItem as WatchEntry)?.Name;

                _watchDetected.Clear();
                foreach (var it in sorted) _watchDetected.Add(it);

                if (selectedName != null && _watchMap.TryGetValue(selectedName, out var restored))
                    LstWatchDetected.SelectedItem = restored;
            }
        }

        private void BtnWatchClear_Click(object sender, RoutedEventArgs e)
        {
            _watchDetected.Clear();
            _watchMap.Clear();
            _watchTimestamps.Clear();
            TxtWatchLog.Text = $"[{DateTime.Now:HH:mm:ss}] История очищена\r\n";
            _lastForegroundPkg = null;
            TxtWatchCurrent.Text = "(не запущено)";
        }
        // =============== ТЕМА ===============
        private bool _isDark = true;

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDark = !_isDark;
            var uri = _isDark
                ? new Uri("Themes/Dark.xaml", UriKind.Relative)
                : new Uri("Themes/Light.xaml", UriKind.Relative);
            var dict = new ResourceDictionary { Source = uri };
            Application.Current.Resources.MergedDictionaries[0] = dict;
            BtnTheme.Content = _isDark ? "☀ Светлая" : "🌙 Тёмная";
        }
        // =============== ADB ===============
        // Вся работа с adb вынесена в AdbRunner: он ищет adb.exe, читает stdout/stderr
        // параллельно, реально соблюдает таймаут и не роняет приложение, если adb нет в системе.

        private static Task<AdbResult> RunAdbAsync(string args, int timeoutMs = 30000)
            => AdbRunner.RunAsync(args, timeoutMs);

        private static string RunAdbOnDevice(string s, string a, int timeoutMs = 30000)
            => AdbRunner.Run($"-s {s} {a}", timeoutMs).Text;

        private static Task<AdbResult> RunAdbOnDeviceAsync(string s, string a, int timeoutMs = 30000)
            => AdbRunner.RunAsync($"-s {s} {a}", timeoutMs);

        private string? SelectedDevice => LstDevices.SelectedItem as string;

        // =============== УСТРОЙСТВА ===============
        private async void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
            => await RefreshDevicesAsync(showErrors: true);

        private async Task RefreshDevicesAsync(bool showErrors = false)
        {
            var adbRes = await RunAdbAsync("devices");

            if (adbRes.AdbNotFound)
            {
                LstDevices.Items.Clear();
                SetDeviceStatus("adb не найден");
                if (showErrors)
                    MessageBox.Show(AdbResult.AdbMissingMessage, "adb не найден",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outText = adbRes.Output;
            var lines = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var current = SelectedDevice;
            LstDevices.Items.Clear();
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("List of")) continue;
                if (t.StartsWith("*")) continue;
                var parts = t.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[1].Trim() == "device")
                    LstDevices.Items.Add(parts[0].Trim());
            }

            // Приоритет выбора:
            // 1) Устройство, выбранное пользователем прямо сейчас
            // 2) Устройство из прошлой сессии (сохраняется в настройках)
            // 3) Если устройство одно в списке — выбрать автоматически
            // 4) Если несколько — не выбирать, пользователь выберет вручную
            var preferred = current ?? _settings.LastDeviceSerial;
            if (!string.IsNullOrEmpty(preferred) && LstDevices.Items.Contains(preferred))
            {
                LstDevices.SelectedItem = preferred;
                SetDeviceStatus($"Найдено устройств: {LstDevices.Items.Count}. Активное: {preferred}");
            }
            else if (LstDevices.Items.Count == 1)
            {
                LstDevices.SelectedIndex = 0;
                SetDeviceStatus($"Найдено устройств: 1 (выбрано автоматически)");
            }
            else if (LstDevices.Items.Count > 1)
            {
                SetDeviceStatus($"Найдено устройств: {LstDevices.Items.Count}. Выберите нужное из списка.");
            }
            else
            {
                SetDeviceStatus("Устройств не найдено");
            }
        }

        private void SetDeviceStatus(string text)
        {
            TxtStatus.Text = text;
            if (TxtHeaderStatus != null) TxtHeaderStatus.Text = text;
        }

        private async void LstDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var device = SelectedDevice;
            var st = device == null
                ? "Устройство не выбрано"
                : $"Активное: {device}";
            TxtStatus.Text = st;
            if (TxtHeaderStatus != null) TxtHeaderStatus.Text = st;

            if (device == null) return;

            // Серийный номер запоминаем сразу, а не только при закрытии окна —
            // иначе после аварийного вылета приложение забудет последнее устройство.
            if (_settings.LastDeviceSerial != device)
            {
                _settings.LastDeviceSerial = device;
                SaveSettings();
            }

            await UpdateDeviceResolutionAsync();

            // Панели обновляем параллельно: суммарно это один round-trip к устройству,
            // а не четыре последовательных.
            var tasks = new List<Task>
            {
                RefreshRemoteAsync(),
                RefreshMassPackagesAsync(),
                RefreshMonitorAsync(),
                RefreshBloatAsync()
            };
            if (ChkAutoLoadPackages?.IsChecked == true)
                tasks.Add(RefreshPackagesAsync());

            await Task.WhenAll(tasks);
        }

        private async Task UpdateDeviceResolutionAsync()
        {
            var device = SelectedDevice;
            if (device == null) return;
            var res = await RunAdbOnDeviceAsync(device, "shell wm size", 5000);
            var text = res.Output;
            var ov = Regex.Match(text, @"Override size:\s*(\d+)x(\d+)");
            var ph = Regex.Match(text, @"Physical size:\s*(\d+)x(\d+)");
            var m = ov.Success ? ov : ph;
            if (m.Success)
            {
                _deviceWidth = int.Parse(m.Groups[1].Value);
                _deviceHeight = int.Parse(m.Groups[2].Value);
                TxtScreenSize.Text = $"{_deviceWidth}×{_deviceHeight}";
            }
        }

        private async void BtnTcpip_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Сначала выберите USB-устройство"); return; }
            var res = await RunAdbOnDeviceAsync(device, "tcpip 5555");
            MessageBox.Show("tcpip 5555:\n" + res.Text);
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            var ip = TxtWifiIp.Text.Trim();
            var port = TxtWifiPort.Text.Trim();
            var res = await RunAdbAsync($"connect {ip}:{port}");
            MessageBox.Show(res.Text);
            await RefreshDevicesAsync();
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            var ip = TxtWifiIp.Text.Trim();
            var port = TxtWifiPort.Text.Trim();
            var res = await RunAdbAsync($"disconnect {ip}:{port}");
            MessageBox.Show(res.Text);
            await RefreshDevicesAsync();
        }

        // =============== ИНФО ===============
        // =============== ХЕЛПЕР ДЛЯ NTP В КОНСОЛЬ ===============
        private void AppendNtpToConsole(string text)
        {
            if (TxtConsole == null) return;
            if (!string.IsNullOrEmpty(TxtConsole.Text) && !TxtConsole.Text.EndsWith("\n"))
                TxtConsole.AppendText("\r\n");
            TxtConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] [NTP]\r\n");
            TxtConsole.AppendText(text);
            if (!text.EndsWith("\n")) TxtConsole.AppendText("\r\n");
            TxtConsole.ScrollToEnd();
        }

        // =============== МОНИТОР СИСТЕМЫ ===============
        private System.Windows.Threading.DispatcherTimer? _monTimer;
        private readonly System.Collections.Generic.List<double> _ramHistory = new();
        private readonly System.Collections.Generic.List<double> _cpuHistory = new();
        private const int SparkMaxPoints = 60;
        private long _lastNetRx = -1;
        private long _lastNetTx = -1;
        private DateTime _lastNetTime = DateTime.MinValue;

        private async void BtnMonRefresh_Click(object sender, RoutedEventArgs e) => await RefreshMonitorAsync();

        private void BtnMonApply_Click(object sender, RoutedEventArgs e)
        {
            if (ChkMonAuto.IsChecked == true) StartMonTimer();
        }

        private void ChkMonAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ChkMonAuto.IsChecked == true) StartMonTimer();
            else StopMonTimer();
        }

        private void StartMonTimer()
        {
            _monTimer?.Stop();
            int ms = 5000;
            if (int.TryParse(TxtMonInterval.Text, out var v) && v >= 1000 && v <= 60000)
                ms = v;
            _monTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ms)
            };
            _monTimer.Tick += async (_, _) => await RefreshMonitorAsync();
            _monTimer.Start();
        }

        private void StopMonTimer()
        {
            _monTimer?.Stop();
            _monTimer = null;
        }

        // Защита от наложения обновлений: пока предыдущий запрос не завершился,
        // новый не запускается — иначе на медленном устройстве adb-процессы
        // накапливаются и забивают канал.
        private bool _monBusy;

        private async Task RefreshMonitorAsync()
        {
            if (_monBusy) return;

            if (SelectedDevice == null)
            {
                TxtMonModel.Text = "Выберите устройство";
                TxtMonAndroid.Text = "—";
                return;
            }

            var device = SelectedDevice;
            _monBusy = true;

            try
            {
                var modelTask   = RunAdbOnDeviceAsync(device, "shell getprop ro.product.model", 4000);
                var androidTask = RunAdbOnDeviceAsync(device, "shell getprop ro.build.version.release", 4000);
                var sdkTask     = RunAdbOnDeviceAsync(device, "shell getprop ro.build.version.sdk", 4000);
                var abiTask     = RunAdbOnDeviceAsync(device, "shell getprop ro.product.cpu.abi", 4000);
                var batTask     = RunAdbOnDeviceAsync(device, "shell dumpsys battery", 5000);
                var memTask     = RunAdbOnDeviceAsync(device, "shell dumpsys meminfo", 15000);
                var cpuTask     = RunAdbOnDeviceAsync(device, "shell dumpsys cpuinfo", 5000);
                var diskTask    = RunAdbOnDeviceAsync(device, "shell df /data", 4000);
                var netTask     = RunAdbOnDeviceAsync(device, "shell cat /proc/net/dev", 4000);
                var uptimeTask  = RunAdbOnDeviceAsync(device, "shell cat /proc/uptime", 4000);
                var coresTask   = RunAdbOnDeviceAsync(device, "shell nproc", 3000);
                var coresAltTask = RunAdbOnDeviceAsync(device,
                    "shell 'cat /proc/cpuinfo | grep -c processor'", 3000);

                await Task.WhenAll(modelTask, androidTask, sdkTask, abiTask, batTask,
                                   memTask, cpuTask, diskTask, netTask, uptimeTask,
                                   coresTask, coresAltTask);

                if (SelectedDevice != device) return;

                var model = modelTask.Result.Output.Trim();
                var android = androidTask.Result.Output.Trim();
                var sdk = sdkTask.Result.Output.Trim();
                var abi = abiTask.Result.Output.Trim();

                TxtMonModel.Text = string.IsNullOrWhiteSpace(model) ? "—" : model;
                TxtMonAndroid.Text = $"Android {android} (SDK {sdk}) · {abi}";

                var bat = batTask.Result.Output;
                TxtDumpBattery.Text = bat;
                ParseBattery(bat);

                ParseUptime(uptimeTask.Result.Output);

                var mem = memTask.Result.Output;
                TxtDumpMeminfo.Text = mem;
                var ramPct = ParseMemory(mem);
                AddHistory(_ramHistory, ramPct);
                DrawSpark(CvsRamSpark, PlRamSpark, _ramHistory);

                var cpu = cpuTask.Result.Output;
                TxtDumpCpuinfo.Text = cpu;
                var cpuPct = ParseCpu(cpu, ParseCores(coresTask.Result.Output, coresAltTask.Result.Output));
                AddHistory(_cpuHistory, cpuPct);
                DrawSpark(CvsCpuSpark, PlCpuSpark, _cpuHistory);

                ParseTopCpu(cpu, mem);

                var disk = diskTask.Result.Output;
                TxtDumpDiskstats.Text = disk;
                ParseDisk(disk);

                var net = netTask.Result.Output;
                TxtDumpNetstats.Text = net;
                ParseNet(net);

                TxtMonUpdated.Text = $"⏱ Обновлено {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                TxtMonUpdated.Text = "⏱ Ошибка: " + ex.Message;
            }
            finally
            {
                _monBusy = false;
            }
        }

        private void ParseUptime(string text)
        {
            // /proc/uptime: "123456.78 98765.43" — первое число секунды
            var t = text.Trim();
            if (string.IsNullOrEmpty(t)) { TxtMonUptime.Text = "⏱ Uptime: —"; return; }
            var parts = t.Split(' ');
            if (parts.Length < 1) { TxtMonUptime.Text = "⏱ Uptime: —"; return; }
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sec)) return;

            var span = TimeSpan.FromSeconds(sec);
            string s;
            if (span.TotalDays >= 1) s = $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
            else if (span.TotalHours >= 1) s = $"{(int)span.TotalHours}h {span.Minutes}m";
            else s = $"{span.Minutes}m {span.Seconds}s";

            TxtMonUptime.Text = $"⏱ Uptime: {s}";
        }

        private static void AddHistory(System.Collections.Generic.List<double> list, double v)
        {
            list.Add(v);
            if (list.Count > SparkMaxPoints)
                list.RemoveAt(0);
        }

        private static void DrawSpark(System.Windows.Controls.Canvas canvas,
                                       System.Windows.Shapes.Polyline line,
                                       System.Collections.Generic.List<double> data)
        {
            if (canvas == null || line == null) return;
            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 200;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 32;
            if (w <= 0 || h <= 0 || data.Count < 2)
            {
                line.Points = new System.Windows.Media.PointCollection();
                return;
            }

            double stepX = w / (SparkMaxPoints - 1);
            double offsetX = w - stepX * (data.Count - 1);

            var pts = new System.Windows.Media.PointCollection();
            for (int i = 0; i < data.Count; i++)
            {
                double x = offsetX + i * stepX;
                double y = h - Math.Clamp(data[i], 0, 100) / 100.0 * (h - 2) - 1;
                pts.Add(new System.Windows.Point(x, y));
            }
            line.Points = pts;
        }

        private void ParseTopCpu(string cpuText, string memText)
        {
            // === CPU ===
            var cpuList = new System.Collections.Generic.List<TopEntry>();
            foreach (var line in cpuText.Split('\n'))
            {
                var t = line.TrimStart();
                var m = Regex.Match(t, @"^([\d.]+)%\s+\d+/([a-zA-Z0-9_.]+)");
                if (!m.Success) continue;
                var pctStr = m.Groups[1].Value;
                var name = m.Groups[2].Value;
                if (!double.TryParse(pctStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct)) continue;
                if (name.StartsWith(".")) continue;
                cpuList.Add(new TopEntry { Name = name, Value = $"{pct:0.0}%", Rank = "" });
            }
            var topCpu = cpuList
                .OrderByDescending(x => double.TryParse(x.Value.TrimEnd('%'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0)
                .Take(5).ToList();
            for (int i = 0; i < topCpu.Count; i++) topCpu[i].Rank = $"{i + 1}.";
            LstTopCpu.ItemsSource = topCpu;

            // === RAM — дедупликация по имени ===
            var memDict = new System.Collections.Generic.Dictionary<string, long>();
            foreach (var line in memText.Split('\n'))
            {
                var m = Regex.Match(line, @"^\s*([\d,]+)K:\s+([a-zA-Z0-9_.]+)\s+\(pid");
                if (!m.Success) continue;
                var kbStr = m.Groups[1].Value.Replace(",", "");
                var name = m.Groups[2].Value;
                if (!long.TryParse(kbStr, out var kb)) continue;
                if (memDict.TryGetValue(name, out var exist))
                {
                    if (kb > exist) memDict[name] = kb;
                }
                else
                {
                    memDict[name] = kb;
                }
            }
            var topMem = memDict
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select((kv, i) => new TopEntry
                {
                    Rank = $"{i + 1}.",
                    Name = kv.Key,
                    Value = kv.Value >= 1024 ? $"{kv.Value / 1024.0:0.0} MB" : $"{kv.Value} KB"
                })
                .ToList();
            LstTopMem.ItemsSource = topMem;
        }

        private void ParseBattery(string text)
        {
            int level = -1, temp = -1, status = -1, voltageMv = -1;
            long chargeUah = -1;

            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("level:")) int.TryParse(t.Substring(6).Trim(), out level);
                else if (t.StartsWith("temperature:")) int.TryParse(t.Substring(12).Trim(), out temp);
                else if (t.StartsWith("status:")) int.TryParse(t.Substring(7).Trim(), out status);
                else if (t.StartsWith("voltage:")) int.TryParse(t.Substring(8).Trim(), out voltageMv);
                else if (t.StartsWith("Charge counter:"))
                    long.TryParse(t.Substring(15).Trim(), out chargeUah);
            }

            string statusText = status switch
            {
                1 => "🔌 Не заряжается",
                2 => "⚡ Заряжается",
                3 => "🔋 Разряжается",
                4 => "✅ Полностью заряжен",
                5 => "⚠️ Не заряжается (нет питания)",
                _ => "—"
            };

            string icon = level switch
            {
                < 0 => "—",
                <= 15 => "🪫",
                _ => "🔋"
            };

            TxtMonBatteryIcon.Text = icon;
            TxtMonBatteryLine.Text = $"🔋 {level}%  ·  🌡 {temp / 10.0:0.0}°C  ·  {statusText}";
            PrgBattery.Value = Math.Clamp(level, 0, 100);

            // Детали: мАч + напряжение
            var parts = new System.Collections.Generic.List<string>();
            if (chargeUah > 0)
                parts.Add($"🔋 {chargeUah / 1000} мАч");
            if (voltageMv >= 100)
                parts.Add($"⚡ {voltageMv / 1000.0:0.00} V");
            else if (voltageMv >= 1 && voltageMv <= 10)
                parts.Add($"⚡ {voltageMv:0.00} V");

            TxtMonBatteryDetails.Text = parts.Count > 0
                ? string.Join("  ·  ", parts)
                : "—";
        }

                private double ParseMemory(string text)
        {
            long total = 0, used = 0;
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Total RAM:") && total == 0)
                {
                    var m = Regex.Match(t, @"\d[\d,]*");
                    if (m.Success) long.TryParse(m.Value.Replace(",", ""), out total);
                }
                else if (t.StartsWith("Used RAM:") && used == 0)
                {
                    var m = Regex.Match(t, @"\d[\d,]*");
                    if (m.Success) long.TryParse(m.Value.Replace(",", ""), out used);
                }
            }

            if (total == 0)
            {
                TxtRam.Text = "—";
                TxtRamDetail.Text = "нет данных";
                PrgRam.Value = 0;
                return 0;
            }

            double totalGb = total / 1024.0 / 1024;
            double usedGb = used / 1024.0 / 1024;
            double freeGb = totalGb - usedGb;
            double pct = usedGb > 0 ? usedGb / totalGb * 100 : 0;

            TxtRam.Text = $"{usedGb:0.0} / {totalGb:0.0} GB";
            TxtRamDetail.Text = $"Свободно: {freeGb:0.0} GB";
            PrgRam.Value = Math.Clamp(pct, 0, 100);
            return pct;
        }

        /// <summary>
        /// Число ядер устройства. Берётся из вывода nproc, при неудаче — из подсчёта
        /// processor в /proc/cpuinfo. Оба ответа уже получены асинхронно, поэтому
        /// UI-поток не блокируется.
        /// </summary>
        private static int ParseCores(string nprocOutput, string fallbackOutput = "")
        {
            int cores = 0;
            int.TryParse(nprocOutput.Trim(), out cores);
            if (cores <= 0)
                int.TryParse(fallbackOutput.Trim(), out cores);
            if (cores <= 0) cores = Environment.ProcessorCount;
            return cores;
        }

        private double ParseCpu(string text, int cores)
        {
            double totalPct = -1;
            double load1 = 0;

            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.Contains("TOTAL:"))
                {
                    var m = Regex.Match(t, @"^([\d.,]+)%");
                    if (m.Success)
                    {
                        var s = m.Groups[1].Value.Replace(",", ".");
                        double.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out totalPct);
                    }
                }
                if (t.StartsWith("Load:"))
                {
                    var parts = t.Substring(5).Trim().Split('/');
                    if (parts.Length >= 1)
                        double.TryParse(parts[0].Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out load1);
                }
            }

            double displayPct;
            string detail;

            if (totalPct >= 0)
            {
                displayPct = totalPct;
                detail = $"Загрузка CPU · {cores} ядер";
            }
            else
            {
                displayPct = Math.Min(100, load1 / cores * 100);
                detail = $"Load {load1:0.00} · {cores} ядер";
            }

            TxtCpu.Text = $"{displayPct:0.0}%";
            TxtCpuDetail.Text = detail;
            PrgCpu.Value = Math.Clamp(displayPct, 0, 100);
            return displayPct;
        }

        private void ParseDisk(string text)
        {
            // Filesystem 1K-blocks Used Available Use% Mounted on
            // /dev/block/dm-XX 12345678 4567890 7654321 38% /data
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Filesystem") || t.Length == 0) continue;
                var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;
                if (!long.TryParse(parts[1], out var totalK)) continue;
                if (!long.TryParse(parts[2], out var usedK)) continue;

                double totalGb = totalK / 1024.0 / 1024;
                double usedGb = usedK / 1024.0 / 1024;
                double freeGb = totalGb - usedGb;
                double pct = totalGb > 0 ? usedGb / totalGb * 100 : 0;

                TxtDisk.Text = $"{usedGb:0.0} / {totalGb:0.0} GB";
                TxtDiskDetail.Text = $"Свободно: {freeGb:0.0} GB";
                PrgDisk.Value = Math.Clamp(pct, 0, 100);
                return;
            }
            TxtDisk.Text = "—";
            TxtDiskDetail.Text = "—";
        }

        private void ParseNet(string text)
        {
            long rx = 0, tx = 0;
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (!t.Contains(":")) continue;
                var idx = t.IndexOf(':');
                if (idx < 1) continue;
                var iface = t.Substring(0, idx).Trim();
                if (iface == "lo") continue;
                var rest = t.Substring(idx + 1).Trim();
                var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) continue;
                if (long.TryParse(parts[0], out var rxBytes)) rx += rxBytes;
                if (long.TryParse(parts[8], out var txBytes)) tx += txBytes;
            }

            TxtNetRx.Text = "↓ " + FormatBytes(rx);
            TxtNetTx.Text = "↑ " + FormatBytes(tx);

            // Расчёт скорости относительно прошлого замера
            var now = DateTime.Now;
            if (_lastNetRx >= 0 && _lastNetTx >= 0 && _lastNetTime != DateTime.MinValue)
            {
                double dt = (now - _lastNetTime).TotalSeconds;
                if (dt > 0.1)
                {
                    long drx = rx - _lastNetRx;
                    long dtx = tx - _lastNetTx;
                    if (drx < 0) drx = 0;
                    if (dtx < 0) dtx = 0;

                    double rxSpeed = drx / dt;
                    double txSpeed = dtx / dt;

                    TxtNetRate.Text = $"↓ {FormatBytes((long)rxSpeed)}/s  ↑ {FormatBytes((long)txSpeed)}/s";
                }
            }
            else
            {
                TxtNetRate.Text = "— (первый замер)";
            }

            _lastNetRx = rx;
            _lastNetTx = tx;
            _lastNetTime = now;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.0} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:0.0} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:0.00} GB";
        }

        // =============== NTP ===============
        private async void BtnNtpRefresh_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }

            AppendNtpToConsole("⏳ Принудительная NTP-синхронизация...\r\n");
            var res = (await RunAdbOnDeviceAsync(device,
                "shell cmd network_time_update_service force_refresh")).Output.Trim();

            if (res.Equals("true", StringComparison.OrdinalIgnoreCase))
                AppendNtpToConsole("✔ Синхронизация выполнена\r\n");
            else if (res.Equals("false", StringComparison.OrdinalIgnoreCase))
                AppendNtpToConsole("⚠ Команда отправлена, но система отклонила (подождите минуту и попробуйте снова)\r\n");
            else if (string.IsNullOrWhiteSpace(res))
                AppendNtpToConsole("✔ Команда отправлена\r\n");
            else
                AppendNtpToConsole(res + "\r\n");

            AppendNtpToConsole("— Текущее время устройства:\r\n");
            AppendNtpToConsole((await RunAdbOnDeviceAsync(device, "shell date")).Output);
        }

        private async void BtnNtpList_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }

            var sb = new StringBuilder();
            sb.AppendLine("=== Список NTP-серверов (из системного конфига) ===");
            var dump = (await RunAdbOnDeviceAsync(device, "shell cmd network_time_update_service dump")).Output;

            // Строка getNtpConfig() — массив mServerUris=[...]
            var m = Regex.Match(dump, @"mServerUris=\[([^\]]+)\]");
            if (m.Success)
            {
                var uris = m.Groups[1].Value.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0);
                foreach (var u in uris)
                    sb.AppendLine("  • " + u.Replace("ntp://", ""));
            }
            else
            {
                sb.AppendLine("  (не удалось извлечь список)");
            }

            // Явная настройка settings
            sb.AppendLine();
            sb.AppendLine("=== settings get global ntp_server ===");
            var st = (await RunAdbOnDeviceAsync(device, "shell settings get global ntp_server")).Output.Trim();
            sb.AppendLine("  " + (st == "null" ? "(не задан — используется встроенный список)" : st));

            // Последний успешный сервер
            var ok = Regex.Match(dump, @"mLastSuccessfulNtpServerUri=(\S+)");
            if (ok.Success)
            {
                sb.AppendLine();
                sb.AppendLine("=== Последний успешный сервер ===");
                sb.AppendLine("  " + ok.Groups[1].Value.Replace("ntp://", ""));
            }

            // Интервал опроса
            var norm = Regex.Match(dump, @"mNormalPollingIntervalMillis=(\d+)");
            if (norm.Success && long.TryParse(norm.Groups[1].Value, out var ms))
            {
                sb.AppendLine();
                sb.AppendLine("=== Интервал обычного опроса ===");
                sb.AppendLine($"  {ms} мс  (~{ms / 1000.0 / 3600:0.##} ч)");
            }

            AppendNtpToConsole(sb.ToString());
        }

        private async void BtnNtpSet_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            var server = TxtNtpServer.Text.Trim();
            if (server.Length == 0) { MessageBox.Show("Введите адрес NTP-сервера"); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"=== Установка NTP-сервера: {server} ===");

            var r1 = (await RunAdbOnDeviceAsync(device, $"shell settings put global ntp_server {server}")).Output;
            sb.AppendLine("settings put: " + (string.IsNullOrWhiteSpace(r1) ? "OK" : r1.Trim()));

            var r2 = (await RunAdbOnDeviceAsync(device, $"shell device_config put runtime_native_boot ntp_server {server}")).Output;
            sb.AppendLine("device_config put: " + (string.IsNullOrWhiteSpace(r2) ? "OK" : r2.Trim()));

            var chk = (await RunAdbOnDeviceAsync(device, "shell settings get global ntp_server")).Output.Trim();
            sb.AppendLine();
            sb.AppendLine("Проверка settings: " + chk);
            sb.AppendLine();
            sb.AppendLine("⚠ Многие прошивки (Realme/ColorOS, MIUI, OneUI) игнорируют эту настройку");
            sb.AppendLine("   и используют встроенный список серверов. Проверьте после перезагрузки:");
            sb.AppendLine("   • кнопкой «📋 Список NTP-серверов»");
            sb.AppendLine("   • или: shell cmd network_time_update_service dump");

            AppendNtpToConsole(sb.ToString());
        }

        private async void BtnNtpReset_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            if (MessageBox.Show("Сбросить настройку NTP-сервера до стандартной?",
                "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== Сброс NTP-настройки ===");
            var r1 = (await RunAdbOnDeviceAsync(device, "shell settings delete global ntp_server")).Output;
            sb.AppendLine("settings delete: " + (string.IsNullOrWhiteSpace(r1) ? "OK" : r1.Trim()));

            var r2 = (await RunAdbOnDeviceAsync(device, "shell device_config delete runtime_native_boot ntp_server")).Output;
            sb.AppendLine("device_config delete: " + (string.IsNullOrWhiteSpace(r2) ? "OK" : r2.Trim()));

            var chk = (await RunAdbOnDeviceAsync(device, "shell settings get global ntp_server")).Output.Trim();
            sb.AppendLine();
            sb.AppendLine("Проверка settings: " + chk + "  (должно быть 'null')");

            AppendNtpToConsole(sb.ToString());
        }
        // =============== NTP АВТО-ОБНОВЛЕНИЕ ===============
        private System.Windows.Threading.DispatcherTimer? _ntpTimer;

        private void ChkNtpAuto_Changed(object sender, RoutedEventArgs e)
        {
            StartNtpTimer();
        }

        private void BtnNtpAutoApply_Click(object sender, RoutedEventArgs e)
        {
            if (ChkNtpAuto.IsChecked == true)
                StartNtpTimer();
        }

        private void StartNtpTimer()
        {
            _ntpTimer?.Stop();
            _ntpTimer = null;

            if (ChkNtpAuto.IsChecked != true) { TxtNtpAutoStatus.Text = ""; return; }

            int sec = 3600;
            if (int.TryParse(TxtNtpAutoSec.Text, out var v) && v >= 10 && v <= 86400)
                sec = v;
            else
            {
                TxtNtpAutoStatus.Text = "интервал: 10..86400 сек";
                return;
            }

            TxtNtpAutoStatus.Text = $"каждые {sec} сек";
            _ntpTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(sec)
            };
            _ntpTimer.Tick += async (_, _) =>
            {
                var device = SelectedDevice;
                if (device == null) return;
                var res = await RunAdbOnDeviceAsync(device,
                    "shell cmd network_time_update_service force_refresh");
                TxtConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] force_refresh → {res.Output.Trim()}\r\n");
                TxtConsole.ScrollToEnd();
            };
            _ntpTimer.Start();
        }
        // =============== ЭКРАН + УПРАВЛЕНИЕ ===============
        private async void BtnScreenshot_Click(object sender, RoutedEventArgs e) => await TakeScreenshotAsync();

        private async Task TakeScreenshotAsync()
        {
            // Пока снимок не готов, следующий не начинаем — иначе на медленном
            // устройстве процессы screencap будут накапливаться.
            if (_screenBusy) return;

            var device = SelectedDevice;
            if (device == null) return;
            _screenBusy = true;

            try
            {
                var png = await AdbRunner.RunBinaryAsync($"-s {device} exec-out screencap -p", 15000);
                if (png.Length < 100) return;
                _lastScreenshot = png;
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(png))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                ImgScreen.Source = bmp;
                ApplyZoom();
            }
            catch { }
            finally
            {
                _screenBusy = false;
            }
        }

        private void ApplyZoom()
        {
            if (ImgScreen == null) return;
            if (ImgScreen.Source is BitmapSource bs)
            {
                ImgScreen.Width = bs.PixelWidth * _zoomScale;
                ImgScreen.Height = bs.PixelHeight * _zoomScale;
            }
        }

        private void SldZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _zoomScale = e.NewValue;
            if (TxtZoomValue != null)
                TxtZoomValue.Text = $"{(_zoomScale * 100):0}%";
            ApplyZoom();
        }

        private void BtnZoom100_Click(object sender, RoutedEventArgs e) => SldZoom.Value = 1.0;

        private void BtnZoomFit_Click(object sender, RoutedEventArgs e)
        {
            if (ImgScreen.Source is not BitmapSource bs) return;
            double w = Math.Max(0.05, (ScrollScreen.ActualWidth - 40) / bs.PixelWidth);
            double h = Math.Max(0.05, (ScrollScreen.ActualHeight - 40) / bs.PixelHeight);
            SldZoom.Value = Math.Min(Math.Min(w, h), 1.0);
        }

        private void ScrollScreen_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                SldZoom.Value = Math.Clamp(
                    SldZoom.Value + (e.Delta > 0 ? 0.05 : -0.05), 0.05, 2.0);
                e.Handled = true;
            }
        }

        private void ImgScreen_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _mouseDownPos = e.GetPosition(ImgScreen);
            _mouseDownTime = DateTime.Now;
            ImgScreen.CaptureMouse();
        }

        private void ImgScreen_MouseMove(object sender, MouseEventArgs e)
        {
            if (ImgScreen.Source is not BitmapSource bs || ImgScreen.ActualWidth < 1) return;
            var p = e.GetPosition(ImgScreen);
            double sx = bs.PixelWidth / ImgScreen.ActualWidth;
            double sy = bs.PixelHeight / ImgScreen.ActualHeight;
            TxtMousePos.Text = $"{(int)(p.X * sx)}, {(int)(p.Y * sy)}";
        }

        private async void ImgScreen_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var device = SelectedDevice;
            if (device == null || ImgScreen.Source is not BitmapSource bs) return;
            if (_mouseDownPos == null) { ImgScreen.ReleaseMouseCapture(); return; }

            var downPos = _mouseDownPos.Value;
            var upPos = e.GetPosition(ImgScreen);
            _mouseDownPos = null;
            ImgScreen.ReleaseMouseCapture();

            double sx = bs.PixelWidth / ImgScreen.ActualWidth;
            double sy = bs.PixelHeight / ImgScreen.ActualHeight;

            int x1 = (int)Math.Round(downPos.X * sx);
            int y1 = (int)Math.Round(downPos.Y * sy);
            int x2 = (int)Math.Round(upPos.X * sx);
            int y2 = (int)Math.Round(upPos.Y * sy);

            x1 = Math.Clamp(x1, 0, _deviceWidth - 1);
            y1 = Math.Clamp(y1, 0, _deviceHeight - 1);
            x2 = Math.Clamp(x2, 0, _deviceWidth - 1);
            y2 = Math.Clamp(y2, 0, _deviceHeight - 1);

            double dist = Math.Sqrt(Math.Pow(upPos.X - downPos.X, 2) +
                                    Math.Pow(upPos.Y - downPos.Y, 2));
            int minSwipe = int.TryParse(TxtMinSwipe.Text, out var ms) ? ms : 20;

            if (dist < minSwipe)
                await RunAdbOnDeviceAsync(device, $"shell input tap {x1} {y1}");
            else
            {
                int realMs = (int)(DateTime.Now - _mouseDownTime).TotalMilliseconds;
                int dur = int.TryParse(TxtSwipeMs.Text, out var d) ? d : 300;
                if (realMs > dur && realMs < 3000) dur = realMs;
                await RunAdbOnDeviceAsync(device, $"shell input swipe {x1} {y1} {x2} {y2} {dur}");
            }

            await RefreshAfterInputAsync();
        }

        private async Task RefreshAfterInputAsync()
        {
            await Task.Delay(200);
            await TakeScreenshotAsync();
        }

        private void BtnSavePng_Click(object sender, RoutedEventArgs e)
        {
            if (_lastScreenshot == null) { MessageBox.Show("Сначала сделайте скриншот"); return; }
            var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = "screen.png" };
            if (dlg.ShowDialog() == true)
                File.WriteAllBytes(dlg.FileName, _lastScreenshot);
        }

        private void ChkAutoRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            StartScreenTimer();
        }

        private void BtnApplyDelay_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAutoRefresh.IsChecked == true)
                StartScreenTimer();
        }

        private void StartScreenTimer()
        {
            _screenTimer?.Stop();
            _screenTimer = null;
            if (ChkAutoRefresh.IsChecked != true) return;

            int interval = 1500;
            if (int.TryParse(TxtRefreshMs.Text, out var v) && v >= 200 && v <= 60000)
                interval = v;

            _screenTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(interval)
            };
            _screenTimer.Tick += async (_, _) => await TakeScreenshotAsync();
            _screenTimer.Start();
        }

        private async void BtnInput_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var text = TxtInput.Text.Replace(" ", "%s");
            await RunAdbOnDeviceAsync(device, $"shell input text \"{text}\"");
            await RefreshAfterInputAsync();
        }

        private async void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            await RunAdbOnDeviceAsync(device, "shell input keyevent KEYCODE_HOME");
            await RefreshAfterInputAsync();
        }

        private async void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            await RunAdbOnDeviceAsync(device, "shell input keyevent KEYCODE_BACK");
            await RefreshAfterInputAsync();
        }

        private async void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            await RunAdbOnDeviceAsync(device, "shell input keyevent KEYCODE_MENU");
            await RefreshAfterInputAsync();
        }

        // =============== ФАЙЛЫ (локально + телефон) ===============
        private void RefreshLocal()
        {
            try
            {
                var dir = new DirectoryInfo(_localPath);
                if (!dir.Exists) { _localPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); dir = new DirectoryInfo(_localPath); }
                var items = new List<FileEntry>();
                foreach (var d in dir.GetDirectories())
                {
                    try { items.Add(new FileEntry { Name = d.Name, IsDir = true }); } catch { }
                }
                foreach (var f in dir.GetFiles())
                {
                    try { items.Add(new FileEntry { Name = f.Name, IsDir = false, Size = f.Length }); } catch { }
                }
                items = items.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                LstLocal.ItemsSource = items;
                TxtLocalPath.Text = _localPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка чтения локальной папки: " + ex.Message);
            }
        }

        // Разбор строки вывода "ls -la".
        //
        // Блок даты бывает двух видов, и это ломает разбор по фиксированной позиции:
        //   "drwxr-xr-x  2 root root  4096 Oct 20 22:30 Name"  — три поля даты
        //   "drwxrws--- 3 u0_a2 media_rw 3452 2026-10-16 23:03 Name" — два поля
        // Поэтому длину блока даты определяем по содержимому второго поля.
        private static readonly Regex LsLineRegex = new Regex(
            @"^(?<perms>[-dlbcps][-rwxsStT+@]{9})[+.]?\s+(?<nlink>\d+)\s+(?<owner>\S+)\s+(?<group>\S+)\s+" +
            @"(?<size>\d+)\s+(?<d1>\S+)\s+(?<d2>\S+)(?<rest>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex LsRestRegex = new Regex(
            @"^\s+(?<d3>\S+)\s+(?<name>.*)$", RegexOptions.Compiled);

        /// <summary>
        /// Разбирает строку "ls -la". Возвращает false, если строка не является
        /// элементом листинга.
        /// </summary>
        private static bool TryParseLsLine(string line, out string name, out bool isDir, out long size)
        {
            name = "";
            isDir = false;
            size = 0;

            var m = LsLineRegex.Match(line);
            if (!m.Success) return false;

            long.TryParse(m.Groups["size"].Value, out size);

            var d2 = m.Groups["d2"].Value;
            var rest = m.Groups["rest"].Value;

            // "20" — это день месяца, значит дата занимает три поля ("Oct 20 22:30")
            if (d2.Length is > 0 and <= 2 && d2.All(char.IsDigit))
            {
                var m3 = LsRestRegex.Match(rest);
                if (!m3.Success) return false;
                name = m3.Groups["name"].Value.Trim();
            }
            else
            {
                // "23:03" — это время, дата занимает два поля ("2026-10-16 23:03")
                name = rest.TrimStart();
            }

            if (name.Length == 0 || name == "." || name == "..") return false;
            isDir = m.Groups["perms"].Value.StartsWith("d");
            return true;
        }

        private async Task RefreshRemoteAsync()
        {
            var device = SelectedDevice;
            if (device == null) return;
            try
            {
                var res = await RunAdbOnDeviceAsync(device, $"shell ls -la \"{_remotePath}\"");
                var items = new List<FileEntry>();

                foreach (var line in res.Output.Split('\n'))
                {
                    var t = line.Trim().Replace("\r", "");
                    if (t.Length == 0) continue;
                    if (t.StartsWith("total ")) continue;

                    if (!TryParseLsLine(t, out var name, out var isDir, out var size))
                        continue;

                    items.Add(new FileEntry
                    {
                        Name = name,
                        IsDir = isDir,
                        Size = size
                    });
                }

                // Резервный путь: если формат вывода не распознан, берём имена
                // из "ls -p" — там каждое имя печатается отдельной строкой.
                if (items.Count == 0)
                {
                    var namesRes = await RunAdbOnDeviceAsync(device, $"shell ls -p \"{_remotePath}\"");
                    foreach (var line in namesRes.Output.Split('\n'))
                    {
                        var t = line.Trim().Replace("\r", "");
                        if (t.Length == 0) continue;
                        if (t.Contains("No such file") || t.Contains("Permission denied")) continue;
                        var isDir = t.EndsWith("/");
                        var nm = isDir ? t.TrimEnd('/') : t;
                        if (nm.Length == 0 || nm == "." || nm == "..") continue;
                        items.Add(new FileEntry { Name = nm, IsDir = isDir });
                    }
                }

                items = items.OrderByDescending(x => x.IsDir)
                              .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                LstRemote.ItemsSource = items;
                TxtRemotePath.Text = _remotePath;

                if (items.Count == 0 && res.Output.Contains("No such file"))
                    TxtRemotePath.ToolTip = "Каталог не найден на устройстве";
                else
                    TxtRemotePath.ToolTip = null;
            }
            catch { }
        }

        private void BtnLocalRefresh_Click(object sender, RoutedEventArgs e) => RefreshLocal();
        private async void BtnRemoteRefresh_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

        private void BtnLocalUp_Click(object sender, RoutedEventArgs e)
        {
            var p = Path.GetFullPath(Path.Combine(_localPath, ".."));
            if (Directory.Exists(p)) { _localPath = p; RefreshLocal(); }
        }

        private async void BtnRemoteUp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_remotePath) || _remotePath == "/") return;
            var p = _remotePath.TrimEnd('/');
            var idx = p.LastIndexOf('/');
            _remotePath = idx <= 0 ? "/" : p.Substring(0, idx) + "/";
            await RefreshRemoteAsync();
        }

        private void BtnLocalBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() == true)
            {
                _localPath = dlg.FolderName;
                RefreshLocal();
            }
        }

        private void LstLocal_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstLocal.SelectedItem is not FileEntry fe) return;
            if (fe.IsDir)
            {
                _localPath = Path.Combine(_localPath, fe.Name);
                RefreshLocal();
            }
        }

        private async void LstRemote_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstRemote.SelectedItem is not FileEntry fe) return;
            if (fe.IsDir)
            {
                _remotePath = _remotePath.TrimEnd('/') + "/" + fe.Name + "/";
                await RefreshRemoteAsync();
            }
        }

        private void TxtLocalPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var p = TxtLocalPath.Text.Trim();
            if (Directory.Exists(p)) { _localPath = p; RefreshLocal(); }
        }

        private async void TxtRemotePath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _remotePath = TxtRemotePath.Text.Trim();
            if (!_remotePath.EndsWith("/")) _remotePath += "/";
            await RefreshRemoteAsync();
        }

        private async void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            if (LstLocal.SelectedItem is not FileEntry fe) { MessageBox.Show("Выберите файл в левой панели"); return; }
            if (fe.IsDir) { MessageBox.Show("Папки пока не поддерживаются. Выберите файл."); return; }

            var local = Path.Combine(_localPath, fe.Name);
            var remote = _remotePath.TrimEnd('/') + "/" + fe.Name;
            var res = await RunAdbOnDeviceAsync(device, $"push \"{local}\" \"{remote}\"");
            var text = res.Text;
            MessageBox.Show(text.Length > 500 ? text.Substring(text.Length - 500) : text);
            await RefreshRemoteAsync();
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            if (LstRemote.SelectedItem is not FileEntry fe) { MessageBox.Show("Выберите файл в правой панели"); return; }
            if (fe.IsDir) { MessageBox.Show("Папки пока не поддерживаются. Выберите файл."); return; }

            var remote = _remotePath.TrimEnd('/') + "/" + fe.Name;
            var local = Path.Combine(_localPath, fe.Name);
            var res = await RunAdbOnDeviceAsync(device, $"pull \"{remote}\" \"{local}\"");
            var text = res.Text;
            MessageBox.Show(text.Length > 500 ? text.Substring(text.Length - 500) : text);
            RefreshLocal();
        }

        private async void BtnRemoteDelete_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            if (LstRemote.SelectedItem is not FileEntry fe) return;
            if (MessageBox.Show($"Удалить \"{fe.Name}\" на телефоне?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var remote = _remotePath.TrimEnd('/') + "/" + fe.Name;
            var flag = fe.IsDir ? "-rf" : "";
            await RunAdbOnDeviceAsync(device, $"shell rm {flag} \"{remote}\"");
            await RefreshRemoteAsync();
        }

        // =============== APK ===============
        private async void BtnRefreshPackages_Click(object sender, RoutedEventArgs e) => await RefreshPackagesAsync();

        private async Task RefreshPackagesAsync()
        {
            var device = SelectedDevice;
            if (device == null) return;
            bool withSystem = ChkSystemApps.IsChecked == true;
            bool showRemoved = ChkShowRemoved != null && ChkShowRemoved.IsChecked == true;
            var cmd = withSystem ? "shell pm list packages" : "shell pm list packages -3";

            try
            {
                var resTask = RunAdbOnDeviceAsync(device, cmd);
                var frozenTask = RunAdbOnDeviceAsync(device, "shell pm list packages -d");

                // Чтобы отличить «удалён для пользователя» от «нет на устройстве»,
                // нужны два полных списка: установленные для пользователя и все
                // известные системе (pm list packages -u).
                var installedTask = showRemoved
                    ? RunAdbOnDeviceAsync(device, "shell pm list packages")
                    : Task.FromResult(new AdbResult());
                var knownTask = showRemoved
                    ? RunAdbOnDeviceAsync(device, "shell pm list packages -u")
                    : Task.FromResult(new AdbResult());

                await Task.WhenAll(resTask, frozenTask, installedTask, knownTask);
                var res = resTask.Result.Output;
                var frozenRes = frozenTask.Result.Output;

                var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (showRemoved)
                {
                    var installed = ParsePackageNames(installedTask.Result.Output);
                    foreach (var p in ParsePackageNames(knownTask.Result.Output))
                        if (!installed.Contains(p)) removed.Add(p);
                }

                var items = BuildPackageList(res, frozenRes, removed, showRemoved);

                if (SelectedDevice != device) return;

                LstPackages.ItemsSource = items;
                int frozenCount = items.Count(x => x.IsFrozen);
                int suspCount = items.Count(x => x.IsSuspicious);
                int removedCount = items.Count(x => x.IsRemoved);
                TxtPackageCount.Text = (withSystem
                    ? $"{items.Count} пакетов (вкл. системные)"
                    : $"{items.Count} пакетов (только 3rd party)")
                    + $", заморожено: {frozenCount}, подозрительных: {suspCount}"
                    + (removedCount > 0 ? $", удалено для пользователя: {removedCount}" : "");
            }
            catch { }
        }

        /// <summary>
        /// Разбирает вывод "pm list packages" в множество имён пакетов.
        /// </summary>
        private static HashSet<string> ParsePackageNames(string output)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(output)) return set;

            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (!t.StartsWith("package:")) continue;
                var name = t.Substring("package:".Length).Trim();
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        /// <summary>
        /// Собирает список пакетов из вывода "pm list packages" и "pm list packages -d".
        /// </summary>
        private static List<PackageEntry> BuildPackageList(
            string packagesOutput, string frozenOutput,
            HashSet<string>? removed = null, bool includeRemoved = false)
        {
            var frozen = ParsePackageNames(frozenOutput);
            var items = new List<PackageEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in ParsePackageNames(packagesOutput))
            {
                seen.Add(name);
                items.Add(new PackageEntry
                {
                    Name = name,
                    IsFrozen = frozen.Contains(name),
                    IsSuspicious = IsSuspiciousPackage(name),
                    IsRemoved = removed?.Contains(name) == true
                });
            }

            if (includeRemoved && removed != null)
            {
                foreach (var name in removed)
                {
                    if (seen.Contains(name)) continue;
                    items.Add(new PackageEntry
                    {
                        Name = name,
                        IsRemoved = true,
                        IsSuspicious = IsSuspiciousPackage(name)
                    });
                }
            }

            return items.OrderBy(x => x.Name).ToList();
        }

        private async void ChkSystemApps_Changed(object sender, RoutedEventArgs e) => await RefreshPackagesAsync();

        private async void ChkShowRemoved_Changed(object sender, RoutedEventArgs e) => await RefreshPackagesAsync();

        /// <summary>
        /// Пакеты, выбранные в списке, — либо выделенные строки, либо имя из поля ввода.
        /// </summary>
        private List<string> GetSelectedPackages()
        {
            var list = new List<string>();

            if (LstPackages.SelectedItems != null)
            {
                foreach (var item in LstPackages.SelectedItems)
                {
                    if (item is PackageEntry pe && !list.Contains(pe.Name)) list.Add(pe.Name);
                }
            }

            if (list.Count == 0)
            {
                var typed = TxtPackageName.Text.Trim();
                if (typed.Length > 0) list.Add(typed);
            }
            return list;
        }

        /// <summary>
        /// Удаление пакета для текущего пользователя (pm uninstall --user 0).
        /// Root не нужен: приложение исчезает с устройства, но система и раздел
        /// /system не затрагиваются — пакет можно вернуть через install-existing.
        /// </summary>
        private async void BtnRemoveForUser_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }

            var packages = GetSelectedPackages();
            if (packages.Count == 0) { MessageBox.Show("Выберите пакет в списке или введите имя"); return; }

            var dlg = packages.Count == 1
                ? new RemovePackageDialog(packages[0], $"Удалить «{packages[0]}»?", 1)
                : new RemovePackageDialog("", $"Удалить {packages.Count} пакетов?", packages.Count);
            if (dlg.ShowDialog() != true) return;

            var keep = dlg.KeepData ? "-k " : "";
            TxtApkOutput.Text = "";
            int ok = 0, fail = 0;

            foreach (var pkg in packages)
            {
                TxtApkOutput.AppendText($"👤 {pkg} ... ");
                var res = await RunAdbOnDeviceAsync(device, $"shell pm uninstall {keep}--user 0 {pkg}");
                var text = res.Output.Trim();
                if (text.Contains("Success", StringComparison.OrdinalIgnoreCase))
                {
                    ok++;
                    TxtApkOutput.AppendText("✔ удалён для пользователя\r\n");
                }
                else
                {
                    fail++;
                    TxtApkOutput.AppendText("✖ " + text + "\r\n");
                }
                TxtApkOutput.ScrollToEnd();
            }

            TxtApkOutput.AppendText($"\r\n=== Удалено для пользователя: {ok}, ошибок: {fail} ===\r\n");
            await RefreshPackagesAsync();
        }

        /// <summary>
        /// Возвращает пакет, удалённый для пользователя (cmd package install-existing).
        /// </summary>
        private async void BtnRestorePackage_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }

            var packages = GetSelectedPackages();
            if (packages.Count == 0) { MessageBox.Show("Выберите пакет в списке или введите имя"); return; }

            TxtApkOutput.Text = "";
            int ok = 0, fail = 0;

            foreach (var pkg in packages)
            {
                TxtApkOutput.AppendText($"♻️ {pkg} ... ");
                var res = await RunAdbOnDeviceAsync(device, $"shell cmd package install-existing {pkg}");
                var text = res.Output.Trim();
                if (text.Contains("installed for user", StringComparison.OrdinalIgnoreCase))
                {
                    ok++;
                    TxtApkOutput.AppendText("✔ восстановлен\r\n");
                }
                else
                {
                    fail++;
                    TxtApkOutput.AppendText("✖ " + text + "\r\n");
                }
                TxtApkOutput.ScrollToEnd();
            }

            TxtApkOutput.AppendText($"\r\n=== Восстановлено: {ok}, ошибок: {fail} ===\r\n");
            await RefreshPackagesAsync();
        }

        // =============== МУСОР (типовой bloatware) ===============
        private readonly System.Collections.ObjectModel.ObservableCollection<BloatItem> _bloatItems = new();

        private void InitBloatItems()
        {
            if (_bloatItems.Count > 0) return;

            foreach (var group in BloatwareCatalog.Groups)
            {
                foreach (var spec in group.Packages)
                {
                    _bloatItems.Add(new BloatItem
                    {
                        Package = spec.Package,
                        Title = spec.Title,
                        Note = spec.Note,
                        Group = group.Title
                    });
                }
            }
        }

        private async Task RefreshBloatAsync()
        {
            InitBloatItems();

            var device = SelectedDevice;
            if (device == null)
            {
                TxtBloatCount.Text = "Выберите устройство";
                return;
            }

            var res = await RunAdbOnDeviceAsync(device, "shell pm list packages");
            var installed = ParsePackageNames(res.Output);

            foreach (var item in _bloatItems)
                item.Installed = installed.Contains(item.Package);

            int found = _bloatItems.Count(i => i.Installed);
            TxtBloatCount.Text = $"найдено на устройстве: {found} из {_bloatItems.Count}";
        }

        private async void BtnBloatRefresh_Click(object sender, RoutedEventArgs e) => await RefreshBloatAsync();

        private void BtnBloatSelectAll_Click(object sender, RoutedEventArgs e)
        {
            InitBloatItems();
            foreach (var item in _bloatItems)
                if (item.Installed) item.Selected = true;
        }

        private void BtnBloatClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _bloatItems) item.Selected = false;
        }

        private async void BtnBloatRemove_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }

            InitBloatItems();
            var selected = _bloatItems.Where(i => i.Selected && i.Installed).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Отметьте пакеты и нажмите «Проверить наличие»");
                return;
            }

            var dlg = new RemovePackageDialog("", $"Удалить {selected.Count} отмеченных пакетов?", selected.Count);
            if (dlg.ShowDialog() != true) return;

            var keep = dlg.KeepData ? "-k " : "";
            TxtBloatLog.Text = $"=== Удаление {selected.Count} пакетов ===\r\n";
            int ok = 0, fail = 0;

            foreach (var item in selected)
            {
                TxtBloatLog.AppendText($"{item.Package} ... ");
                var res = await RunAdbOnDeviceAsync(device, $"shell pm uninstall {keep}--user 0 {item.Package}");
                var text = res.Output.Trim();
                if (text.Contains("Success", StringComparison.OrdinalIgnoreCase))
                {
                    ok++;
                    TxtBloatLog.AppendText("✔\r\n");
                }
                else
                {
                    fail++;
                    TxtBloatLog.AppendText("✖ " + text + "\r\n");
                }
                TxtBloatLog.ScrollToEnd();
            }

            TxtBloatLog.AppendText($"\r\n=== Удалено для пользователя: {ok}, ошибок: {fail} ===\r\n");
            TxtBloatLog.AppendText("Вернуть пакеты можно на вкладке APK → «♻️ Восстановить».\r\n");
            TxtBloatLog.ScrollToEnd();

            await RefreshBloatAsync();
            await RefreshPackagesAsync();
        }

        private async void BtnFreeze_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var pkg = TxtPackageName.Text.Trim();
            if (pkg.Length == 0) { MessageBox.Show("Выберите или введите пакет"); return; }
            if (MessageBox.Show($"Заморозить \"{pkg}\"?\r\n\r\nПриложение будет отключено " +
                                "(pm disable-user). Его не будет в лаунчере, но данные сохранятся.",
                "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            PrgApk.Visibility = Visibility.Visible;
            PrgApk.IsIndeterminate = true;
            TxtApkOutput.Text = $"⏳ Заморозка {pkg}...\r\n";

            var result = await RunAdbOnDeviceAsync(device, $"shell pm disable-user --user 0 {pkg}");

            TxtApkOutput.AppendText("❄ " + result.Output);
            PrgApk.IsIndeterminate = false;
            PrgApk.Visibility = Visibility.Collapsed;
        }

        private async void BtnUnfreeze_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var pkg = TxtPackageName.Text.Trim();
            if (pkg.Length == 0) { MessageBox.Show("Выберите или введите пакет"); return; }

            PrgApk.Visibility = Visibility.Visible;
            PrgApk.IsIndeterminate = true;
            TxtApkOutput.Text = $"⏳ Разморозка {pkg}...\r\n";

            var result = await RunAdbOnDeviceAsync(device, $"shell pm enable {pkg}");

            TxtApkOutput.AppendText("☀ " + result.Output);
            PrgApk.IsIndeterminate = false;
            PrgApk.Visibility = Visibility.Collapsed;
        }

        private void LstPackages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (LstPackages == null || TxtPackageName == null) return;
                if (LstPackages.SelectedItems.Count == 1 &&
                    LstPackages.SelectedItem is PackageEntry pe)
                {
                    TxtPackageName.Text = pe.Name;
                }
            }
            catch { }
        }

        private async void BtnInstallApk_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            var dlg = new OpenFileDialog { Filter = "APK|*.apk" };
            if (dlg.ShowDialog() != true) return;

            PrgApk.Visibility = Visibility.Visible;
            PrgApk.IsIndeterminate = true;
            TxtApkOutput.Text = $"⏳ Установка {Path.GetFileName(dlg.FileName)}...\r\n";

            var path = dlg.FileName;
            var result = await RunAdbOnDeviceAsync(device, $"install -r \"{path}\"");

            TxtApkOutput.AppendText("✔ " + result.Output);
            PrgApk.IsIndeterminate = false;
            PrgApk.Visibility = Visibility.Collapsed;

            await RefreshPackagesAsync();
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var pkg = TxtPackageName.Text.Trim();
            if (pkg.Length == 0) { MessageBox.Show("Выберите или введите пакет"); return; }
            if (MessageBox.Show($"Удалить \"{pkg}\" с телефона?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            PrgApk.Visibility = Visibility.Visible;
            PrgApk.IsIndeterminate = true;
            TxtApkOutput.Text = $"⏳ Удаление {pkg}...\r\n";

            var result = await RunAdbOnDeviceAsync(device, $"uninstall {pkg}");

            TxtApkOutput.AppendText("✔ " + result.Output);
            PrgApk.IsIndeterminate = false;
            PrgApk.Visibility = Visibility.Collapsed;

            await RefreshPackagesAsync();
        }

        private async void BtnPackageInfo_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var pkg = TxtPackageName.Text.Trim();
            if (pkg.Length == 0) return;
            var res = await RunAdbOnDeviceAsync(device, $"shell dumpsys package {pkg}");
            TxtApkOutput.Text = res.Output;
        }

        private async void BtnPackageLaunch_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var pkg = TxtPackageName.Text.Trim();
            if (pkg.Length == 0) return;
            var res = await RunAdbOnDeviceAsync(device,
                $"shell monkey -p {pkg} -c android.intent.category.LAUNCHER 1");
            TxtApkOutput.Text = res.Output;
            await RefreshAfterInputAsync();
        }

        // =============== LOGCAT ===============
        private async void BtnLogcatDump_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var res = await RunAdbOnDeviceAsync(device, "logcat -d -v time");
            TxtLogcat.Text = res.Output;
        }

        private async void BtnLogcatClear_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            await RunAdbOnDeviceAsync(device, "logcat -c");
            MessageBox.Show("Логи очищены");
        }

        private async void BtnLogcatTag_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) return;
            var tag = TxtLogcatTag.Text.Trim();
            var res = await RunAdbOnDeviceAsync(device,
                string.IsNullOrEmpty(tag) ? "logcat -d -v time" : $"logcat -d -v time -s {tag}");
            TxtLogcat.Text = res.Output;
        }

        // =============== МАССОВЫЕ ОПЕРАЦИИ ===============
        private void BtnApkFolderBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            if (dlg.ShowDialog() == true)
            {
                TxtApkFolder.Text = dlg.FolderName;
                ScanApkFolder();
            }
        }

        private void BtnApkFolderScan_Click(object sender, RoutedEventArgs e) => ScanApkFolder();

        private void ScanApkFolder()
        {
            var folder = TxtApkFolder.Text.Trim();
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("Папка не существует: " + folder);
                return;
            }
            try
            {
                var items = new List<ApkFileEntry>();
                foreach (var f in Directory.GetFiles(folder, "*.apk", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        items.Add(new ApkFileEntry { Name = fi.Name, FullPath = fi.FullName, Size = fi.Length });
                    }
                    catch { }
                }
                items = items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                LstApkFiles.ItemsSource = items;
                TxtMassLog.Text = $"Найдено APK: {items.Count}\r\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка чтения папки: " + ex.Message);
            }
        }

        private void BtnSelectAllApks_Click(object sender, RoutedEventArgs e) => LstApkFiles.SelectAll();
        private void BtnClearApks_Click(object sender, RoutedEventArgs e) => LstApkFiles.UnselectAll();
        private void BtnSelectAllPkgs_Click(object sender, RoutedEventArgs e) => LstPackagesMass.SelectAll();
        private void BtnClearPkgs_Click(object sender, RoutedEventArgs e) => LstPackagesMass.UnselectAll();
        private async void BtnRefreshMassPkgs_Click(object sender, RoutedEventArgs e) => await RefreshMassPackagesAsync();

        private async Task RefreshMassPackagesAsync()
        {
            var device = SelectedDevice;
            if (device == null) return;
            try
            {
                bool withSystem = ChkSystemApps.IsChecked == true;
                var cmd = withSystem ? "shell pm list packages" : "shell pm list packages -3";

                var resTask = RunAdbOnDeviceAsync(device, cmd);
                var frozenTask = RunAdbOnDeviceAsync(device, "shell pm list packages -d");
                await Task.WhenAll(resTask, frozenTask);

                var items = BuildPackageList(resTask.Result.Output, frozenTask.Result.Output);
                LstPackagesMass.ItemsSource = items;
            }
            catch { }
        }

        private async void BtnMassInstall_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            var files = LstApkFiles.SelectedItems.Cast<ApkFileEntry>().ToList();
            if (files.Count == 0) { MessageBox.Show("Выберите APK в левом списке"); return; }

            PrgMass.Visibility = Visibility.Visible;
            PrgMass.IsIndeterminate = false;
            PrgMass.Minimum = 0;
            PrgMass.Maximum = files.Count;
            PrgMass.Value = 0;

            TxtMassLog.Text = $"=== Установка {files.Count} APK ===\r\n";
            int ok = 0, fail = 0;

            foreach (var f in files)
            {
                TxtMassLog.AppendText($"⏳ {f.Name} ... ");
                TxtMassLog.ScrollToEnd();
                var path = f.FullPath;
                var result = await RunAdbOnDeviceAsync(device, $"install -r \"{path}\"");
                var text = result.Output;
                bool success = text.Contains("Success");
                if (success) { ok++; TxtMassLog.AppendText("✔ OK\r\n"); }
                else { fail++; TxtMassLog.AppendText("✖ " + text.Trim() + "\r\n"); }
                PrgMass.Value++;
                TxtMassLog.ScrollToEnd();
            }

            TxtMassLog.AppendText($"\r\n=== Итого: успешно {ok}, ошибок {fail} ===\r\n");
            TxtMassLog.ScrollToEnd();
            PrgMass.Visibility = Visibility.Collapsed;
            await RefreshMassPackagesAsync();
        }

        private async void BtnMassUninstall_Click(object sender, RoutedEventArgs e)
        {
            var device = SelectedDevice;
            if (device == null) { MessageBox.Show("Выберите устройство"); return; }
            var pkgs = LstPackagesMass.SelectedItems.Cast<PackageEntry>().ToList();
            if (pkgs.Count == 0) { MessageBox.Show("Выберите приложения в правом списке"); return; }
            if (MessageBox.Show($"Удалить {pkgs.Count} приложений?", "Подтверждение",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            PrgMass.Visibility = Visibility.Visible;
            PrgMass.IsIndeterminate = false;
            PrgMass.Minimum = 0;
            PrgMass.Maximum = pkgs.Count;
            PrgMass.Value = 0;

            TxtMassLog.Text = $"=== Удаление {pkgs.Count} приложений ===\r\n";
            int ok = 0, fail = 0;

            foreach (var p in pkgs)
            {
                TxtMassLog.AppendText($"⏳ {p.Name} ... ");
                TxtMassLog.ScrollToEnd();
                var name = p.Name;
                var result = await RunAdbOnDeviceAsync(device, $"uninstall {name}");
                var text = result.Output;
                bool success = text.Contains("Success");
                if (success) { ok++; TxtMassLog.AppendText("✔ OK\r\n"); }
                else { fail++; TxtMassLog.AppendText("✖ " + text.Trim() + "\r\n"); }
                PrgMass.Value++;
                TxtMassLog.ScrollToEnd();
            }

            TxtMassLog.AppendText($"\r\n=== Итого: успешно {ok}, ошибок {fail} ===\r\n");
            TxtMassLog.ScrollToEnd();
            PrgMass.Visibility = Visibility.Collapsed;
            await RefreshMassPackagesAsync();
        }
        // =============== ПОИСК РЕКЛАМНЫХ / ПОДОЗРИТЕЛЬНЫХ ПАКЕТОВ ===============
        private static readonly string[] SuspiciousKeywords = new[]
        {
            "ads", "adservice", "advert", "ad_sdk", "admob",
            "tracker", "analytics", "metric", "telemetry",
            "suggest", "recommend", "promo", "banner",
            "tracking", "advertise", "admanager"
        };

        private static bool IsSuspiciousPackage(string pkg)
        {
            if (string.IsNullOrEmpty(pkg)) return false;
            var lower = pkg.ToLowerInvariant();
            foreach (var kw in SuspiciousKeywords)
                if (lower.Contains(kw)) return true;
            return false;
        }

        private async void BtnFindSuspicious_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDevice == null) { MessageBox.Show("Выберите устройство"); return; }

            if (LstPackages.ItemsSource == null) await RefreshPackagesAsync();

            // ItemsSource — это List<PackageEntry>, но он может оказаться и списком
            // подозрительных пакетов, поэтому приводим аккуратно, без исключений.
            var all = (LstPackages.ItemsSource as IEnumerable<PackageEntry>)?.ToList() ?? new List<PackageEntry>();
            if (all.Count == 0) await RefreshPackagesAsync();
            all = (LstPackages.ItemsSource as IEnumerable<PackageEntry>)?.ToList() ?? all;

            var susp = all.Where(p => IsSuspiciousPackage(p.Name)).ToList();

            if (susp.Count == 0)
            {
                MessageBox.Show(
                    "Подозрительных пакетов не найдено.\n\n" +
                    "Совет: включите галочку «Системные приложения» и нажмите «Обновить список» — " +
                    "иногда рекламные модули прячутся среди системных.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Фильтруем список на подозрительные
            LstPackages.ItemsSource = susp;
            try { LstPackages.SelectAll(); }
            catch { /* SelectionMode может не поддерживать SelectAll — игнорируем */ }
            TxtPackageCount.Text = $"Подозрительных: {susp.Count} из {all.Count} (выделены)";

            // Диалог с предложением заморозить
            var preview = string.Join("\r\n", susp.Take(15).Select(p => "  • " + p.Name));
            if (susp.Count > 15) preview += $"\r\n  ... и ещё {susp.Count - 15}";

            var msg =
                $"🔍 Найдено {susp.Count} подозрительных пакетов:\r\n\r\n" +
                preview + "\r\n\r\n" +
                "Что сделать?\r\n" +
                "  • ДА — заморозить их все сейчас (безопасно, можно разморозить)\r\n" +
                "  • НЕТ — оставить в списке для ручной проверки";

            var r = MessageBox.Show(msg, "Обнаружены подозрительные приложения",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (r == MessageBoxResult.Yes)
                await FreezeManyPackagesAsync(susp);
        }

        private async void BtnShowAllPackages_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPackagesAsync();
        }

        private async Task FreezeManyPackagesAsync(List<PackageEntry> pkgs)
        {
            var device = SelectedDevice;
            if (device == null) return;

            PrgApk.Visibility = Visibility.Visible;
            PrgApk.IsIndeterminate = false;
            PrgApk.Minimum = 0;
            PrgApk.Maximum = pkgs.Count;
            PrgApk.Value = 0;

            TxtApkOutput.Text = $"⏳ Заморозка {pkgs.Count} пакетов...\r\n";
            int ok = 0, fail = 0;

            foreach (var p in pkgs)
            {
                TxtApkOutput.AppendText($"  ❄ {p.Name} ... ");
                TxtApkOutput.ScrollToEnd();

                var name = p.Name;
                var res = await RunAdbOnDeviceAsync(device, $"shell pm disable-user --user 0 {name}");
                var text = res.Output;

                bool success = text.Contains("new state: disabled", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("disabled-user", StringComparison.OrdinalIgnoreCase);

                if (success) { ok++; TxtApkOutput.AppendText("✔\r\n"); }
                else { fail++; TxtApkOutput.AppendText("✖ " + text.Trim() + "\r\n"); }

                PrgApk.Value++;
                TxtApkOutput.ScrollToEnd();
            }

            TxtApkOutput.AppendText($"\r\n=== Заморожено: {ok}, ошибок: {fail} ===\r\n");
            TxtApkOutput.AppendText(
                "Разморозить: выберите пакет → ☀ Разморозить, или вручную: shell pm enable <пакет>\r\n");
            TxtApkOutput.ScrollToEnd();

            PrgApk.Visibility = Visibility.Collapsed;
            await RefreshPackagesAsync();
        }
        // =============== КОНСОЛЬ ===============
        private async void BtnRunCmd_Click(object sender, RoutedEventArgs e)
        {
            var cmd = TxtCmd.Text.Trim();
            if (cmd.Length == 0) return;
            string full = (SelectedDevice != null &&
                          !cmd.StartsWith("devices") &&
                          !cmd.StartsWith("connect"))
                ? $"-s {SelectedDevice} {cmd}"
                : cmd;

            TxtConsole.AppendText($"> adb {full}\n");
            string res = (await RunAdbAsync(full)).Text;
            TxtConsole.AppendText(res);
            if (!res.EndsWith("\n")) TxtConsole.AppendText("\n");
            TxtConsole.ScrollToEnd();
        }
    }
}





























































