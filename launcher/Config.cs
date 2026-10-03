namespace SkelterLauncher;

/// <summary>Всё, что специфично для конкретной игры и репозитория, лежит здесь.</summary>
internal static class Config
{
    /// <summary>Манифест берём с raw.githubusercontent.com, а не через GitHub API — нет лимита запросов.</summary>
    private const string RemoteManifestUrl =
        "https://raw.githubusercontent.com/breakneckz/skelterUpdater/main/version.json";

    public const string AppName = "Skelter Arena";

    /// <summary>Имя исполняемого файла по умолчанию; version.json может его переопределить.</summary>
    public const string DefaultExecutable = "Skelter Arena.exe";

    /// <summary>Папка установки по умолчанию, если рядом с лаунчером игры ещё нет.</summary>
    public const string DefaultInstallFolderName = "Skelter Arena";

    /// <summary>Под этим именем копия лаунчера кладётся в папку игры — на неё смотрит ярлык.</summary>
    public const string LauncherFileName = "SkelterLauncher.exe";

    /// <summary>Папка в %LocalAppData% с installed.json — общая для всех копий лаунчера.</summary>
    public const string StateFolderName = "SkelterArena";

    public const string UserAgent = "SkelterLauncher/1.2 (+https://github.com/breakneckz/skelterUpdater)";

    /// <summary>
    /// Песочница для проверки лаунчера: если задана переменная SKELTER_LAUNCHER_DEV_DIR, манифест
    /// читается из &lt;папка&gt;\version.json, а installed.json и ярлык живут там же.
    /// Настоящая установка и ярлык на рабочем столе при этом не трогаются.
    /// </summary>
    public static readonly string? DevDir =
        Environment.GetEnvironmentVariable("SKELTER_LAUNCHER_DEV_DIR") is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : null;

    public static string ManifestUrl => DevDir is null ? RemoteManifestUrl : Path.Combine(DevDir, "version.json");

    public static string StateDir => DevDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        StateFolderName);

    public static string DesktopDir => DevDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}
