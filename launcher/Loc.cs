namespace SkelterLauncher;

internal enum Language { En, Ru }

/// <summary>
/// Все строки интерфейса. Английский — по умолчанию, русский — по выбору игрока.
/// Пары лежат рядом, чтобы при правке одной не забыть вторую.
/// </summary>
internal static class Loc
{
    public static Language Current { get; set; } = Language.En;

    public static Language Parse(string? code) =>
        string.Equals(code, "ru", StringComparison.OrdinalIgnoreCase) ? Language.Ru : Language.En;

    public static string Code(Language language) => language == Language.Ru ? "ru" : "en";

    private static string S(string en, string ru) => Current == Language.Ru ? ru : en;

    // ---------------------------------------------------------------- окно

    public static string WindowTitle => S($"{Config.AppName} — Launcher", $"{Config.AppName} — лаунчер");
    public static string Subtitle => S("LAUNCHER", "ЛАУНЧЕР");
    public static string Browse => S("Browse…", "Обзор…");
    public static string InstallFolder => S("Install folder", "Папка установки");
    public static string InstalledIn => S("Installed in", "Игра установлена в");
    public static string CreateShortcut => S("Create a desktop shortcut", "Создать ярлык игры на рабочем столе");
    public static string OpenGameFolder => S("Game folder", "Папка игры");

    public static string ButtonChecking => S("CHECKING…", "ПРОВЕРКА…");
    public static string ButtonInstall => S("INSTALL", "УСТАНОВИТЬ");
    public static string ButtonPlay => S("PLAY", "ИГРАТЬ");
    public static string ButtonRetry => S("RETRY", "ПОВТОРИТЬ");

    public static string NotInstalled => S("not installed", "не установлена");
    public static string Installed(string v) => S($"Installed: {v}", $"Установлено: {v}");
    public static string Available(string v) => S($"Available: {v}", $"Доступно: {v}");

    // ---------------------------------------------------------------- статус

    public static string CheckingUpdates => S("Checking for updates…", "Проверяем обновления…");
    public static string ChooseFolderAndInstall => S("Choose a folder and press “Install”", "Выберите папку и нажмите «Установить»");
    public static string UpToDate => S("You have the latest version", "Установлена последняя версия");
    public static string VersionInstalled(string v) => S($"Version {v} installed", $"Версия {v} установлена");
    public static string CreatingShortcut => S("Creating desktop shortcut…", "Создаём ярлык на рабочем столе…");
    public static string ShortcutFailed(string e) => S($"Shortcut not created: {e}", $"Ярлык не создан: {e}");
    public static string ShortcutError(string e) => S($"Shortcut: {e}", $"Ярлык: {e}");
    public static string ShortcutCreated => S("Desktop shortcut created", "Ярлык создан на рабочем столе");
    public static string ShortcutRemoved => S("Desktop shortcut removed", "Ярлык удалён с рабочего стола");

    public static string Downloading(string v) => S($"Downloading update {v}…", $"Скачиваем обновление {v}…");
    public static string DownloadingPatch(string v) => S($"Downloading patch {v}…", $"Скачиваем патч {v}…");
    public static string Verifying => S("Verifying download…", "Проверяем загрузку…");
    public static string Corrupted => S("Download is corrupted, downloading again…", "Файл повреждён, качаем заново…");
    public static string CorruptedFinal =>
        S("The download is corrupted (checksum mismatch). Please try again later.",
          "Скачанный файл повреждён (не совпадает контрольная сумма). Попробуйте позже.");
    public static string Installing(string v) => S($"Installing {v}…", $"Устанавливаем {v}…");
    public static string ApplyingPatch(string v) => S($"Applying patch {v}…", $"Применяем патч {v}…");
    public static string PatchFallback => S("Patch didn't fit — downloading the full version", "Патч не подошёл — качаем версию целиком");

    public static string UpdatingLauncher => S("Updating the launcher…", "Обновляем лаунчер…");
    public static string RestartingLauncher => S("Restarting the launcher…", "Перезапускаем лаунчер…");

    public static string CantInstallHere => S("Can't install to this folder — choose another one", "В эту папку установить нельзя — выберите другую");
    public static string Offline => S("Can't reach the update server — playing the current version", "Нет связи с сервером обновлений — играем на текущей версии");
    public static string ManifestFailed => S("Couldn't get the version list. Check your internet connection.", "Не удалось получить список версий. Проверьте интернет.");
    public static string UpdateError => S("Update failed", "Ошибка обновления");
    public static string GameMissing => S("Game files not found — reinstalling", "Файл игры не найден — переустанавливаем");
    public static string LaunchFailed => S("Couldn't start the game", "Не удалось запустить игру");
    public static string Network(string e) => S($"Network: {e}", $"Сеть: {e}");

    // ---------------------------------------------------------------- прогресс

    public static string Of(string done, string total) => S($"{done} of {total}", $"{done} из {total}");
    public static string Left(string eta) => S($"{eta} left", $"осталось {eta}");
    public static string IntegrityCheck(string percent) => S($"Integrity check   {percent}", $"Проверка целостности   {percent}");
    public static string Unpacking(string doneOfTotal, string percent) => S($"Unpacking   {doneOfTotal}   {percent}", $"Распаковка   {doneOfTotal}   {percent}");
    public static string Patching(string doneOfTotal, string percent) => S($"Updating files   {doneOfTotal}   {percent}", $"Обновление файлов   {doneOfTotal}   {percent}");
    public static string DownloadSize(string size) => S($"Download size: {size}", $"Размер загрузки: {size}");
    public static string FreeOnDisk(string drive, string free) => S($"Free on {drive}: {free}", $"Свободно на диске {drive}: {free}");

    // ---------------------------------------------------------------- единицы

    public static string[] ByteUnits => Current == Language.Ru
        ? ["Б", "КБ", "МБ", "ГБ", "ТБ"]
        : ["B", "KB", "MB", "GB", "TB"];

    public static string PerSecond(string size) => S($"{size}/s", $"{size}/с");
    public static string Hours(int h, int m) => S($"{h} h {m} min", $"{h} ч {m} мин");
    public static string Minutes(int m) => S($"{m} min", $"{m} мин");
    public static string Seconds(int s) => S($"{s} s", $"{s} с");

    // ---------------------------------------------------------------- ошибки

    public static string FolderPickerTitle => S($"Where to install {Config.AppName}", $"Куда установить {Config.AppName}");
    public static string DriveUnavailable => S("The drive is unavailable.", "Диск недоступен.");
    public static string NoWriteAccess(string dir) =>
        S($"No write access to {dir}. Choose a folder outside Program Files.",
          $"Нет прав на запись в {dir}. Выберите папку вне Program Files.");
    public static string NotEnoughSpace(string root, string needed, string free) =>
        S($"Not enough space on {root}. Needed ~{needed}, free {free}.",
          $"Недостаточно места на диске {root}. Нужно ~{needed}, свободно {free}.");
    public static string BadArchivePath(string path) => S($"The archive contains an invalid path: {path}", $"Архив содержит недопустимый путь: {path}");

    public static string AlreadyRunning => S("The launcher is already running.", "Лаунчер уже запущен.");
    public static string CrashTitle => S($"{Config.AppName} — launcher crash", $"{Config.AppName} — сбой лаунчера");
    public static string UnknownError => S("Unknown error", "Неизвестная ошибка");
}
