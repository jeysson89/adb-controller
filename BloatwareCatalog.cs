using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AdbController
{
    /// <summary>
    /// Запись каталога «мусора»: пакет, который обычно есть в прошивке,
    /// но пользователю не нужен.
    /// </summary>
    public sealed class BloatItem : INotifyPropertyChanged
    {
        private bool _installed;
        private bool _selected;

        public string Package { get; init; } = "";
        public string Title { get; init; } = "";
        public string Note { get; init; } = "";

        /// <summary>Группа производителя, к которой относится пакет.</summary>
        public string Group { get; init; } = "";

        /// <summary>Пакет присутствует на устройстве.</summary>
        public bool Installed
        {
            get => _installed;
            set { if (_installed != value) { _installed = value; OnChanged(); } }
        }

        /// <summary>Помечен пользователем к удалению.</summary>
        public bool Selected
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; OnChanged(); } }
        }

        public string StatusText => Installed ? "установлен" : "не найден";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Каталог типового мусора по производителям.
    ///
    /// В список попадают только те пакеты, удаление которых через
    /// "pm uninstall --user 0" безопасно: они не являются системными ядрами,
    /// без которых не работает базовая функциональность (Google Play Services,
    /// провайдеры, ввод, оболочка и т. п.).
    /// </summary>
    public static class BloatwareCatalog
    {
        public sealed record Group(string Title, Spec[] Packages);

        public sealed record Spec(string Package, string Title, string Note);

        private static Spec[] S(params string[] rows)
        {
            // rows: "package|Title|Пояснение"
            var list = new List<Spec>();
            foreach (var r in rows)
            {
                var p = r.Split('|');
                list.Add(new Spec(p[0], p.Length > 1 ? p[1] : p[0], p.Length > 2 ? p[2] : ""));
            }
            return list.ToArray();
        }

        public static readonly Group[] Groups =
        {
            new("Realme / ColorOS / Oppo", S(
                "com.heytap.browser|Браузер HeyTap|Заменить на свой браузер",
                "com.heytap.market|Магазин приложений HeyTap|Полезен только для обновлений ColorOS",
                "com.heytap.pictorial|Обои HeyTap|Штатные обои",
                "com.oppo.quicksearchbox|Поиск Oppo|Быстрый поиск на рабочем столе",
                "com.oppo.rftoolkit|RF-инструменты Oppo|Диагностика радиомодуля",
                "com.oppo.engineermode|Инженерное меню Oppo|Для разработчиков",
                "com.oppo.engineermode.camera|Инженерное меню — камера|Для разработчиков",
                "com.oppo.engineermode.network|Инженерное меню — сеть|Для разработчиков",
                "com.coloros.filemanager|Файловый менеджер ColorOS|Есть встроенный в оболочку",
                "com.coloros.gallery3d|Галерея ColorOS|Есть Google Фото",
                "com.coloros.video|Видеоплеер ColorOS|Есть Google ТВ",
                "com.coloros.weather2|Погода ColorOS|Есть в оболочке",
                "com.coloros.weather.service|Сервис погоды ColorOS|Внутренний сервис оболочки",
                "com.coloros.childrenspace|Детский режим|Режим детской безопасности",
                "com.coloros.activation|Активация ColorOS|Приветственные экраны",
                "com.coloros.operationManual|Руководство пользователя|Оффлайн-инструкция",
                "com.coloros.lockassistant|Ассистент блокировки|Экран блокировки",
                "com.coloros.bootreg|Регистрация загрузки|Служебное",
                "com.coloros.compass2|Компас ColorOS|Есть в приложении «Компас»",
                "com.coloros.phonemanager|Менеджер телефона ColorOS|Обслуживание",
                "com.realme.as.music|Музыка Realme|Есть Google YouTube Music",
                "com.realme.smartdrive|Облако Realme Drive|Облачное хранилище",
                "com.realme.backuprestore|Резервное копирование Realme|⚠ Перед удалением проверьте бэкапы",
                "com.nearme.statistics.rom|Статистика Nearme|Отправка телеметрии",
                "com.nearme.romupdate|Проверка обновлений ROM|Есть системная проверка",
                "android.autoinstalls.config.oppo|Автоустановка Oppo|Предустановка приложений"
            )),

            new("Google", S(
                "com.google.android.youtube|YouTube|Удаление убирает приложение, не аккаунт",
                "com.google.android.apps.youtube.music|YouTube Music|Музыкальный плеер",
                "com.google.android.apps.googleassistant|Google Ассистент|Голосовой помощник",
                "com.google.android.as|Google Ассистент (служба)|⚠ Не удалять без замены ассистента",
                "com.google.android.as.oss|Google Ассистент (служба)|⚠ Не удалять без замены ассистента",
                "com.google.android.apps.maps|Google Карты|Навигация",
                "com.google.android.apps.photos|Google Фото|Облачные фото",
                "com.google.android.apps.docs|Google Документы|Редактор офисных документов",
                "com.google.android.gm|Gmail|Почта Google",
                "com.google.android.videos|Google ТВ|Видеосервис YouTube",
                "com.google.android.keep|Google Keep|Заметки",
                "com.google.android.googlequicksearchbox|Поиск Google|Виджет поиска",
                "com.google.android.apps.messaging|Сообщения Google|SMS по умолчанию",
                "com.google.android.apps.walletnfcrel|Google Wallet NFC|Бесконтактная оплата",
                "com.google.android.apps.wellbeing|Цифровое благополучие|Экранное время",
                "com.google.android.apps.nbu.files|Google Файлы (NBU)|Файлы Google",
                "com.google.android.apps.restore|Google Резервное копирование|⚠ Перед удалением отключите бэкап",
                "com.google.android.feedback|Обратная связь Google|Отправка отзывов",
                "com.google.android.accessibility.switchaccess|Switch Access|Спец. возможности",
                "com.google.android.printservice.recommendation|Подбор принтеров|Реклама принтеров",
                "com.google.android.apps.turbo|Turbo|Ускоритель игр",
                "com.google.android.apps.safetyhub|Google SafetyHub|Проверка безопасности"
            )),

            new("MIUI / HyperOS (Xiaomi)", S(
                "com.miui.player|Медиаплеер MIUI|Музыка и видео",
                "com.miui.video|Видео MIUI|Видеоплеер",
                "com.miui.notes|Заметки MIUI|Заметки",
                "com.miui.weather2|Погода MIUI|Есть в оболочке",
                "com.miui.browser|Браузер MIUI|Заменить на свой",
                "com.miui.gallery|Галерея MIUI|Есть Google Фото",
                "com.miui.cloudbackup|Резервные копии MIUI|⚠ Проверьте бэкапы",
                "com.miui.cloudservice|Облако MIUI|Облако Xiaomi",
                "com.miui.cloudservice.sysbase|Облако MIUI (служба)|Служба облака",
                "com.miui.securitycore|Безопасность MIUI|Сканер, кредитная защита",
                "com.miui.securityadd|Установка приложений MIUI|Установка из APK",
                "com.miui.analytics|Аналитика MIUI|Телеметрия",
                "com.xiaomi.joyose|PlayServices MIUI|⚠ Может сломать Push-уведомления",
                "com.miui.gamecenter|Game Center MIUI|Игровой центр",
                "com.miui.screenrecorder|Запись экрана MIUI|Запись экрана",
                "com.miui.bugreport|Отчёты о баге MIUI|Диагностика",
                "com.miui.bugreport2|Отчёты о баге MIUI 2|Диагностика",
                "com.miui.kissback|Возврат MIUI|Мастер свайпов",
                "com.miui.intentguide|Подсказки MIUI|Обучение жестам",
                "com.miui.huanji|Уборка MIUI|Очистка памяти",
                "com.miui.duapp|Мусор MIUI|⚠ Не удалять — удаляет неиспользуемые APK",
                "com.miui.videoplayer|Видеоплеер MIUI|Видеоплеер",
                "com.xiaomi.passport|Аккаунт Xiaomi|⚠ Часто нужен для работы прошивки",
                "com.xiaomi.vipaccount|VIP Xiaomi|Аккаунт",
                "com.xiaomi.mipicks|Mi Picks|Магазин Xiaomi",
                "com.xiaomi.miservice|MiService|⚠ Не удалять — фоновые службы",
                "com.xiaomi.passport.ui|Passport UI|Интерфейс аккаунта"
            )),

            new("OneUI / Samsung", S(
                "com.samsung.android.app.spage|Samsung Internet|Заменить на свой браузер",
                "com.samsung.android.app.sfolder|Samsung Folder|Есть Google Файлы",
                "com.samsung.android.app.social|Samsung Social|Сеть Samsung",
                "com.samsung.android.voc|Samsung Voice|Голосовой помощник Bixby",
                "com.samsung.android.game.gametools|Game Plugins|Игровые оптимизации",
                "com.samsung.android.game.gos|Game Optimizing Service|Игровые оптимизации",
                "com.samsung.android.game.galaxyapps|Galaxy Store|Магазин Galaxy",
                "com.samsung.android.app.tips|Фишки Samsung|Виджеты и подсказки",
                "com.samsung.android.app.mgame|M Game|Игровой лаунчер",
                "com.samsung.android.app.shealth|Samsung Health|Здоровье",
                "com.samsung.android.app.contacts|Samsung Contacts|Есть Google Контакты",
                "com.samsung.android.aremoji|AR Emoji|AR-эмодзи",
                "com.samsung.android.app.arzone|AR Zone|AR-сервисы",
                "com.samsung.android.app.samsungapps|Samsung Apps|Магазин Galaxy",
                "com.samsung.android.bixbyagent|Bixby|⚠ Не удалять без замены",
                "com.samsung.android.bixbyvision|Bixby Vision|Камера Bixby"
            )),

            new("EMUI / Huawei", S(
                "com.huawei.browser|Браузер Huawei|Заменить на свой",
                "com.huawei.videos|Видео Huawei|Видеоплеер",
                "com.huawei.gamecenter|Game Center Huawei|Игровой центр",
                "com.huawei.android.tips|Советы Huawei|Подсказки и виджеты",
                "com.huawei.hwid|Huawei ID|⚠ Часто нужен",
                "com.huawei.android.hwid|Huawei ID (служба)|⚠ Часто нужен",
                "com.huawei.hwid.apiclient|Huawei ID API|⚠ Служба аккаунта",
                "com.huawei.android.music|Музыка Huawei|Плеер",
                "com.huawei.contacts|Контакты Huawei|Есть Google Контакты",
                "com.huawei.android.hidisk|Облако Huawei|Облачное хранилище",
                "com.huawei.android.appmarket|Huawei AppGallery|Магазин Huawei",
                "com.huawei.android.picturepicker|Галерея Huawei|Выбор фото",
                "com.huawei.vodservice|Huawei Видео|Сервис видео",
                "com.huawei.broadcaster|Huawei ТВ-каналы|Просмотр ТВ"
            )),

            new("Прочее", S(
                "com.android.browser|Браузер AOSP|Если установлен",
                "com.android.email|Email AOSP|Почта AOSP",
                "com.android.dreams|Дремы AOSP|Заставки экрана",
                "com.android.wallpaper|Живые обои AOSP|Штатные обои",
                "com.android.wallpapercropper|Обрезка обоев AOSP|Хелпер обоев",
                "com.android.wallpaperbackup|Резервные копии обоев|Хелпер обоев",
                "com.android.overlay.default.browser|Overlay браузера|Хелпер",
                "com.android.providers.downloads.ui|UI загрузок|Хелпер",
                "com.android.printspooler|Служба печати|⚠ Удалите, если не печатаете",
                "com.android.bluetooth|Bluetooth|⚠ Не удалять без нужды",
                "com.android.nfc|NFC|⚠ Не удалять без нужды",
                "com.android.dialer|Телефон AOSP|Звонилка",
                "com.android.contacts|Контакты AOSP|Контакты"
            ))
        };
    }
}
