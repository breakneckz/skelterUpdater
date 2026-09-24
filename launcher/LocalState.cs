using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkelterLauncher;

/// <summary>Состояние установки, лежит в installed.json рядом с лаунчером.</summary>
internal sealed class LocalState
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("installPath")] public string? InstallPath { get; set; }

    /// <summary>Выставляется на время распаковки. Если при старте true — прошлая установка оборвалась.</summary>
    [JsonPropertyName("updateInProgress")] public bool UpdateInProgress { get; set; }

    [JsonIgnore] public string StatePath { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static LocalState Load(string launcherDir)
    {
        var path = Path.Combine(launcherDir, "installed.json");
        LocalState state;

        try
        {
            state = File.Exists(path)
                ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path), Options) ?? new LocalState()
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
        var json = JsonSerializer.Serialize(this, Options);
        var tmp = StatePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, StatePath, overwrite: true);
    }
}
