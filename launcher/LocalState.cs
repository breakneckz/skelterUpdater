using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkelterLauncher;

/// <summary>
/// Состояние установки: %LocalAppData%\SkelterArena\installed.json. Не рядом с лаунчером —
/// иначе копия лаунчера в папке игры (на неё смотрит ярлык) и исходный exe считали бы
/// каждая свою версию и качали игру заново.
/// </summary>
internal sealed class LocalState
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("installPath")] public string? InstallPath { get; set; }

    /// <summary>Выставляется на время распаковки. Если при старте true — прошлая установка оборвалась.</summary>
    [JsonPropertyName("updateInProgress")] public bool UpdateInProgress { get; set; }

    /// <summary>Галочка «ярлык на рабочем столе», пока игра ещё не поставлена.</summary>
    [JsonPropertyName("desktopShortcut")] public bool DesktopShortcut { get; set; } = true;

    /// <summary>Язык интерфейса: "en" (по умолчанию) или "ru".</summary>
    [JsonPropertyName("language")] public string? Language { get; set; }

    [JsonIgnore] public string StatePath { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static LocalState Load(string launcherDir)
    {
        var path = Path.Combine(Config.StateDir, "installed.json");

        // Лаунчер 1.0 хранил installed.json рядом с собой — подхватываем, чтобы не качать игру заново.
        var legacyPath = Path.Combine(launcherDir, "installed.json");
        var source = File.Exists(path) ? path : File.Exists(legacyPath) ? legacyPath : null;

        LocalState state;

        try
        {
            state = source is not null
                ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(source), Options) ?? new LocalState()
                : new LocalState();
        }
        catch
        {
            // Битый installed.json не должен мешать запуску — просто переустановим.
            state = new LocalState();
        }

        state.StatePath = path;

        if (string.IsNullOrWhiteSpace(state.InstallPath))
            state.InstallPath = ResolveDefaultInstallPath(launcherDir);

        if (source == legacyPath)
        {
            try
            {
                state.Save();
            }
            catch
            {
                // Не перенесли — перенесём при следующем сохранении.
            }
        }

        return state;
    }

    /// <summary>
    /// Если лаунчер положили прямо в папку с игрой — ставим туда же.
    /// Иначе игра уезжает в подпапку рядом с лаунчером.
    /// </summary>
    private static string ResolveDefaultInstallPath(string launcherDir)
    {
        if (File.Exists(Path.Combine(launcherDir, Config.DefaultExecutable)))
            return launcherDir;

        var nested = Path.Combine(launcherDir, Config.DefaultInstallFolderName);
        if (File.Exists(Path.Combine(nested, Config.DefaultExecutable)))
            return nested;

        return nested;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var json = JsonSerializer.Serialize(this, Options);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, StatePath, overwrite: true);
    }
}
