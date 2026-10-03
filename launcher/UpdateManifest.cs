using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkelterLauncher;

/// <summary>Файл на сервере, который качается с докачкой и сверкой SHA-256.</summary>
internal interface IRemoteFile
{
    string Url { get; }
    string Sha256 { get; }
    long Size { get; }
}

/// <summary>
/// Содержимое version.json из корня репозитория. Поля patches/launcher/notesRu появились в лаунчере 1.2 —
/// лаунчер 1.1 их не знает, пропускает и по-прежнему качает полный архив.
/// </summary>
internal sealed class UpdateManifest : IRemoteFile
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("executable")] public string? Executable { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("notesRu")] public string? NotesRu { get; set; }
    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }

    /// <summary>Если true — папка игры очищается перед распаковкой (удаляет файлы, выпиленные из сборки).</summary>
    [JsonPropertyName("cleanInstall")] public bool CleanInstall { get; set; }

    /// <summary>Бинарные патчи с предыдущих версий на эту. Нет подходящего — качаем полный архив.</summary>
    [JsonPropertyName("patches")] public List<PatchInfo> Patches { get; set; } = [];

    /// <summary>Актуальная сборка самого лаунчера — для самообновления.</summary>
    [JsonPropertyName("launcher")] public LauncherInfo? Launcher { get; set; }

    public string ExecutableOrDefault =>
        string.IsNullOrWhiteSpace(Executable) ? Config.DefaultExecutable : Executable!;

    public string? LocalizedNotes =>
        Loc.Current == Language.Ru && !string.IsNullOrWhiteSpace(NotesRu) ? NotesRu : Notes;

    public PatchInfo? PatchFrom(string? installedVersion) =>
        string.IsNullOrWhiteSpace(installedVersion)
            ? null
            : Patches.FirstOrDefault(p => string.Equals(p.From, installedVersion, StringComparison.OrdinalIgnoreCase));

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static UpdateManifest Parse(string json)
    {
        // Редактор или скрипт публикации могли сохранить манифест с BOM — System.Text.Json на нём падает.
        var clean = json.TrimStart('﻿', '​').Trim();

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(clean, Options)
            ?? throw new InvalidDataException("version.json is empty");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("version.json has no \"version\" field");
        if (string.IsNullOrWhiteSpace(manifest.Url))
            throw new InvalidDataException("version.json has no \"url\" field");

        manifest.Patches ??= [];
        return manifest;
    }
}

/// <summary>
/// Патч from → текущая версия. Внутри — дифф HDiffPatch, в котором лежат только изменившиеся и новые файлы,
/// но посчитанный относительно всей старой версии (новые ассеты часто собраны из кусков старых файлов).
/// </summary>
internal sealed class PatchInfo : IRemoteFile
{
    [JsonPropertyName("from")] public string From { get; set; } = "";

    /// <summary>Пусто, если файлы не менялись, а только удалялись.</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>Что получится после наложения: по этому списку сверяем результат перед тем, как трогать игру.</summary>
    [JsonPropertyName("files")] public List<PatchFile> Files { get; set; } = [];

    /// <summary>Файлы, которых в новой версии больше нет.</summary>
    [JsonPropertyName("remove")] public List<string> Remove { get; set; } = [];

    public bool HasData => !string.IsNullOrWhiteSpace(Url);
}

internal sealed class PatchFile
{
    /// <summary>Путь относительно папки игры, разделитель — «/».</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

internal sealed class LauncherInfo : IRemoteFile
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
