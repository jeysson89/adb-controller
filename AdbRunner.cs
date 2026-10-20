using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdbController
{
    /// <summary>
    /// Результат выполнения adb-команды.
    /// Никогда не бросает исключений: отсутствие adb, таймаут и ошибки
    /// возвращаются как данные, а не как падение приложения.
    /// </summary>
    public sealed class AdbResult
    {
        public string Stdout { get; init; } = "";
        public string Stderr { get; init; } = "";
        public int ExitCode { get; init; } = -1;

        /// <summary>Процесс не завершился за отведённое время и был убит.</summary>
        public bool TimedOut { get; init; }

        /// <summary>adb.exe не найден в системе.</summary>
        public bool AdbNotFound { get; init; }

        /// <summary>Не удалось запустить процесс adb.</summary>
        public bool FailedToStart { get; init; }

        /// <summary>Команда отменена вызывающим кодом.</summary>
        public bool Cancelled { get; init; }

        public const string AdbMissingMessage =
            "adb.exe не найден. Установите Android SDK Platform-Tools или добавьте adb.exe в PATH " +
            "(проверяются: ANDROID_HOME, ANDROID_SDK_ROOT, %LOCALAPPDATA%\\Android\\Sdk\\platform-tools, C:\\platform-tools).";

        /// <summary>
        /// Текст в прежней семантике проекта: stdout, а если он пуст — stderr.
        /// Парсеры вывода продолжают работать без изменений.
        /// </summary>
        public string Text
        {
            get
            {
                if (AdbNotFound) return "[adb не найден] " + AdbMissingMessage;
                if (FailedToStart) return "[не удалось запустить adb] " + Stderr;
                if (Cancelled) return "[отменено] " + Stdout + Stderr;
                if (TimedOut) return "[timeout] " + Stdout + Stderr;
                return Output;
            }
        }

        /// <summary>Вывод команды без служебных префиксов — для парсеров.</summary>
        public string Output => string.IsNullOrWhiteSpace(Stdout) ? Stderr : Stdout;

        /// <summary>Команда завершилась штатно (без таймаута и без ошибок запуска).</summary>
        public bool Ok =>
            !AdbNotFound && !FailedToStart && !Cancelled && !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Надёжная обёртка над adb.exe.
    ///
    /// Решает три проблемы, из-за которых проект раньше зависал и падал:
    ///   1) adb может отсутствовать в системе — теперь это не исключение, а результат;
    ///   2) ReadToEnd() блокировался до выхода процесса, из-за чего таймаут не работал
    ///      и adb-поток мог зависнуть навсегда — теперь читаем оба потока асинхронно
    ///      и реально убиваем процесс по таймауту;
    ///   3) чтение stdout и stderr по очереди могло привести к дедлоку, когда stderr
    ///      переполнял буфер — теперь оба канала читаются параллельно.
    /// </summary>
    public static class AdbRunner
    {
        /// <summary>Префикс имён встроенных ресурсов с adb.</summary>
        private const string EmbeddedPrefix = "AdbController.EmbeddedAdb.";

        /// <summary>Файлы, которые распаковываются из сборки при первом запуске.</summary>
        private static readonly string[] EmbeddedFiles =
        {
            "adb.exe",
            "AdbWinApi.dll",
            "AdbWinUsbApi.dll",
            "libwinpthread-1.dll",
            "source.properties",
            "NOTICE.txt"
        };

        private static readonly object Sync = new();
        private static string? _cachedAdb;
        private static DateTime _lastSearch = DateTime.MinValue;
        private static bool _searched;

        /// <summary>
        /// Ищет adb.exe: встроенный в сборку (распаковывается при первом запуске),
        /// рядом с программой, в переменных окружения Android SDK, в типовых местах
        /// установки и в PATH. Результат успешного поиска кэшируется.
        /// </summary>
        public static string? FindAdb(bool forceSearch = false)
        {
            lock (Sync)
            {
                if (!forceSearch && _searched)
                {
                    if (_cachedAdb != null) return _cachedAdb;
                    // adb не нашлось — перепроверяем не чаще одного раза в 10 секунд,
                    // чтобы пользователь мог установить Platform-Tools и сразу увидеть результат
                    if ((DateTime.Now - _lastSearch).TotalSeconds < 10) return null;
                }

                _searched = true;
                _lastSearch = DateTime.Now;
                _cachedAdb = SearchAdb();
                return _cachedAdb;
            }
        }

        public static bool IsAvailable => FindAdb() != null;

        /// <summary>
        /// Куда распаковывается встроенный adb.
        /// </summary>
        public static string BundledAdbDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdbController", "platform-tools");

        private static string? SearchAdb()
        {
            // 1. Встроенный в сборку adb — гарантирует работу без установленного SDK
            var bundled = EnsureBundledAdb();
            if (bundled != null) return bundled;

            // 2. Рядом с программой (portable-режим)
            try
            {
                var baseDir = AppContext.BaseDirectory;
                foreach (var p in new[]
                         {
                             Path.Combine(baseDir, "adb.exe"),
                             Path.Combine(baseDir, "platform-tools", "adb.exe")
                         })
                {
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // 3. Переменные окружения Android SDK
            foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT", "ANDROID_SDK_HOME" })
            {
                var root = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrWhiteSpace(root)) continue;
                try
                {
                    var p = Path.Combine(root.Trim(), "platform-tools", "adb.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }

            // 4. Типовые места установки
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                foreach (var p in new[]
                         {
                             Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"),
                             @"C:\platform-tools\adb.exe",
                             @"C:\adb\adb.exe",
                             @"C:\Android\platform-tools\adb.exe"
                         })
                {
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            // 5. PATH
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        var p = Path.Combine(dir.Trim(), "adb.exe");
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Распаковывает встроенный adb на диск (если ещё не распакован) и
        /// возвращает путь к adb.exe. Распаковка идёт в %LOCALAPPDATA%, чтобы
        /// не требовать прав администратора и не зависеть от расположения .exe.
        /// </summary>
        private static string? EnsureBundledAdb()
        {
            try
            {
                var asm = typeof(AdbRunner).Assembly;
                var dir = BundledAdbDirectory;
                var adbPath = Path.Combine(dir, "adb.exe");
                var markerPath = Path.Combine(dir, ".version");

                // Версия встроенного adb берётся из source.properties — это крошечный
                // ресурс, поэтому проверка не стоит ничего.
                var stamp = ReadEmbeddedText(asm, "source.properties")?.Trim() ?? "";

                // Уже распаковано и версия совпадает — ничего не делаем
                if (File.Exists(adbPath) && File.Exists(markerPath))
                {
                    try
                    {
                        if (File.ReadAllText(markerPath).Trim() == stamp)
                            return adbPath;
                    }
                    catch { }
                }

                Directory.CreateDirectory(dir);

                foreach (var name in EmbeddedFiles)
                {
                    var target = Path.Combine(dir, name);
                    try
                    {
                        using var src = asm.GetManifestResourceStream(EmbeddedPrefix + name);
                        if (src == null) continue;

                        // Пишем во временный файл и подменяем только после успеха —
                        // прерванная распаковка не должна оставить битый adb.exe
                        var tmp = target + ".tmp";
                        using (var fs = File.Create(tmp)) src.CopyTo(fs);
                        File.Move(tmp, target, overwrite: true);
                    }
                    catch
                    {
                        // Например, файл занят работающим adb-сервером. Если старая
                        // копия есть и она рабочая — продолжаем использовать её.
                        try { if (File.Exists(target)) continue; } catch { }
                    }
                }

                try { File.WriteAllText(markerPath, stamp); } catch { }

                return File.Exists(adbPath) ? adbPath : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadEmbeddedText(Assembly asm, string name)
        {
            try
            {
                using var src = asm.GetManifestResourceStream(EmbeddedPrefix + name);
                if (src == null) return null;
                using var reader = new StreamReader(src);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Выполняет adb-команду. stdout и stderr читаются параллельно,
        /// таймаут реально работает: процесс принудительно завершается.
        /// </summary>
        public static async Task<AdbResult> RunAsync(
            string args, int timeoutMs = 30000, CancellationToken ct = default)
        {
            var adb = FindAdb();
            if (adb == null)
                return new AdbResult { AdbNotFound = true };

            var psi = new ProcessStartInfo
            {
                FileName = adb,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = new Process { StartInfo = psi };

            try
            {
                if (!proc.Start())
                    return new AdbResult { FailedToStart = true, Stderr = "Не удалось запустить процесс adb." };
            }
            catch (Exception ex)
            {
                return new AdbResult { FailedToStart = true, Stderr = ex.Message };
            }

            // Оба канала читаются одновременно — иначе переполнение stderr
            // при большом stdout приводит к взаимоблокировке процессов.
            Task<string> outTask, errTask;
            try
            {
                outTask = proc.StandardOutput.ReadToEndAsync();
                errTask = proc.StandardError.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
                return new AdbResult { FailedToStart = true, Stderr = ex.Message };
            }

            var timedOut = false;
            var cancelled = false;

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = ct.IsCancellationRequested;
                    timedOut = !cancelled;
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                }
            }

            var stdout = await DrainAsync(outTask).ConfigureAwait(false);
            var stderr = await DrainAsync(errTask).ConfigureAwait(false);

            var exitCode = -1;
            try { if (proc.HasExited) exitCode = proc.ExitCode; } catch { }

            return new AdbResult
            {
                Stdout = stdout,
                Stderr = stderr,
                ExitCode = exitCode,
                TimedOut = timedOut,
                Cancelled = cancelled
            };
        }

        /// <summary>Синхронная обёртка. Вызывать только из фонового потока, не с UI-потока.</summary>
        public static AdbResult Run(string args, int timeoutMs = 30000, CancellationToken ct = default)
            => RunAsync(args, timeoutMs, ct).GetAwaiter().GetResult();

        /// <summary>
        /// Выполняет adb-команду с бинарным выводом (например, exec-out screencap -p).
        /// </summary>
        public static async Task<byte[]> RunBinaryAsync(
            string args, int timeoutMs = 30000, CancellationToken ct = default)
        {
            var adb = FindAdb();
            if (adb == null) return Array.Empty<byte>();

            var psi = new ProcessStartInfo
            {
                FileName = adb,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };

            try
            {
                if (!proc.Start()) return Array.Empty<byte>();
            }
            catch { return Array.Empty<byte>(); }

            using var ms = new MemoryStream();
            Task copyTask, errTask;
            try
            {
                copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, 81920, ct);
                errTask = proc.StandardError.ReadToEndAsync();
            }
            catch
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
                return Array.Empty<byte>();
            }

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                }
            }

            try { await copyTask.ConfigureAwait(false); } catch { }
            try { await errTask.ConfigureAwait(false); } catch { }

            return ms.ToArray();
        }

        /// <summary>
        /// Забирает результат потока, не зависая на нём, если процесс был убит.
        /// </summary>
        private static async Task<string> DrainAsync(Task<string> task)
        {
            try
            {
                return await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch
            {
                return "";
            }
        }
    }
}
