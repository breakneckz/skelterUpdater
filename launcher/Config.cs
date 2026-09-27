namespace SkelterLauncher;

/// <summary>Всё, что специфично для конкретной игры и репозитория, лежит здесь.</summary>
internal static class Config
{
    /// <summary>Манифест берём с raw.githubusercontent.com, а не через GitHub API — нет лимита запросов.</summary>
    public const string ManifestUrl =
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

    public const string UserAgent = "SkelterLauncher/1.1 (+https://github.com/breakneckz/skelterUpdater)";
}
