using System;
using System.IO;
using System.Text.Json;

namespace AdbController
{
    public class AppSettings
    {
        public bool IsDark { get; set; } = true;
        public int ScreenRefreshMs { get; set; } = 1500;
        public int MonitorIntervalMs { get; set; } = 5000;
        public int WatchIntervalMs { get; set; } = 500;
        public bool WatchNotify { get; set; } = false;
        public bool MonAuto { get; set; } = false;
        public bool ScreenAuto { get; set; } = false;
        public string LastDeviceSerial { get; set; } = "";
        public string LastApkFolder { get; set; } = "";
        public string WifiIp { get; set; } = "192.168.1.100";
        public string WifiPort { get; set; } = "5555";
        public double ZoomScale { get; set; } = 0.3;
        public double WinWidth { get; set; } = 1320;
        public double WinHeight { get; set; } = 860;
    }

    public static class SettingsManager
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "AdbController");
        private static readonly string FilePath = Path.Combine(Dir, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public static string SettingsPath => FilePath;
    }
}

