namespace SkelterLauncher;

/// <summary>Выбор и проверка папки установки.</summary>
internal static class InstallLocation
{
    /// <summary>
    /// Превращает папку из диалога в папку установки. Игру не вываливаем россыпью в непустую папку
    /// (D:\Games, рабочий стол, корень диска): там появится подпапка «Skelter Arena».
    /// Это ещё и страховка: cleanInstall чистит папку установки целиком.
    /// </summary>
    public static string FromUserChoice(string selected)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));

        if (File.Exists(Path.Combine(full, Config.DefaultExecutable)))
            return full; // здесь уже стоит игра

        if (string.Equals(Path.GetFileName(full), Config.DefaultInstallFolderName, StringComparison.OrdinalIgnoreCase))
            return full;

        var isDriveRoot = string.Equals(
            full,
            Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? ""),
            StringComparison.OrdinalIgnoreCase);

        if (!isDriveRoot && IsEmptyDirectory(full))
            return full;

        return Path.Combine(full, Config.DefaultInstallFolderName);
    }

    /// <summary>
    /// Пробует записать файл в папку (или в ближайшую существующую родительскую), не создавая
    /// лишних папок. Ловит Program Files и прочие места, куда без админа не пишется.
    /// </summary>
    public static bool CanWriteTo(string path, out string error)
    {
        var probeDir = ExistingAncestor(path);
        if (probeDir is null)
        {
            error = "Диск недоступен.";
            return false;
        }

        var probe = Path.Combine(probeDir, $".skelter-write-test-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            error = ex is UnauthorizedAccessException
                ? $"Нет прав на запись в {probeDir}. Выберите папку вне Program Files."
                : ex.Message;
            return false;
        }
    }

    public static string? ExistingAncestor(string path)
    {
        string? dir;
        try
        {
            dir = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        for (; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir))
                return dir;
        }

        return null;
    }

    public static long? FreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null; // сетевой путь, отключённый диск и т.п.
        }
    }

    private static bool IsEmptyDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return false;
        }
    }
}
