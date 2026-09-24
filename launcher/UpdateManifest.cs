using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkelterLauncher;

/// <summary>Содержимое version.json из корня репозитория.</summary>
internal sealed class UpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("executable")] public string? Executable { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }

    /// <summary>Если true — папка игры очищается перед распаковкой (удаляет файлы, выпиленные из сборки).</summary>
    [JsonPropertyName("cleanInstall")] public bool CleanInstall { get; set; }

    public string ExecutableOrDefault =>
        string.IsNullOrWhiteSpace(Executable) ? Config.DefaultExecutable : Executable!;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static UpdateManifest Parse(string json)
    {
        // Редактор или скрипт публикации могли сохранить манифест с BOM — System.Text.Json на нём падает.
        var clean = json.TrimStart('\uFEFF', '\u200B').Trim();

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(clean, Options)
            ?? throw new InvalidDataException("version.json пустой");

        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("В version.json не задано поле \"version\"");
        if (string.IsNullOrWhiteSpace(manifest.Url))
            throw new InvalidDataException("В version.json не задано поле \"url\"");

        return manifest;
    }
}
