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

    public const string UserAgent = "SkelterLauncher/1.0 (+https://github.com/breakneckz/skelterUpdater)";
}
