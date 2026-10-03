using System.Diagnostics;
using System.Security.Cryptography;

namespace SkelterLauncher;

/// <summary>
/// Накладывает бинарный патч (HDiffPatch) на установленную игру.
///
/// Порядок такой, чтобы до последнего шага игра оставалась целой:
///   1. hpatchz собирает новые версии изменившихся файлов в .update-staging внутри папки игры;
///   2. каждый собранный файл сверяется с SHA-256 из манифеста;
///   3. только после этого файлы переезжают на место, а выкинутые из сборки — удаляются.
/// Ошибка на шагах 1–2 игру не трогает. Обрыв на шаге 3 ловит флаг updateInProgress —
/// при следующем запуске лаунчер поставит полную версию.
/// </summary>
internal static class Patcher
{
    public const string StagingFolderName = ".update-staging";

    /// <summary>Версия hpatchz, вшитого в лаунчер. Должна совпадать с hdiffz в tools\hdiffpatch.</summary>
    private const string HPatchVersion = "5.1.3";

    public static async Task ApplyAsync(
        string? patchPath,
        PatchInfo patch,
        string installDir,
        UpdateManifest manifest,
        LocalState state,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        var installRoot = Path.GetFullPath(installDir);
        var staging = Path.Combine(installRoot, StagingFolderName);

        // Пути проверяем заранее: битый манифест не должен успеть ничего сломать.
        var files = patch.Files
            .Select(f => (File: f, Staged: Updater.ResolveInside(staging, f.Path), Target: Updater.ResolveInside(installRoot, f.Path)))
            .ToList();
        var removals = patch.Remove.Select(r => Updater.ResolveInside(installRoot, r)).ToList();

        if (files.Count > 0 && (patchPath is null || !patch.HasData))
            throw new InvalidDataException("Patch has files but no data");

        TryDeleteDirectory(staging);

        try
        {
            if (files.Count > 0)
            {
                long total = files.Sum(f => f.File.Size);
                Updater.EnsureFreeSpace(installRoot, total);

                await RunHPatchAsync(installRoot, patchPath!, staging, total, progress, ct).ConfigureAwait(false);
                await VerifyStagedAsync(files, total, progress, ct).ConfigureAwait(false);
            }

            Commit(files, removals, installRoot, manifest, state);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    // ---------------------------------------------------------------- hpatchz

    private static async Task RunHPatchAsync(
        string oldDir,
        string patchPath,
        string outDir,
        long expectedBytes,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        var exe = await EnsureHPatchAsync(ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // -s-64m: старая версия читается потоком с кешем 64 МБ, а не целиком в память.
        psi.ArgumentList.Add("-s-64m");
        psi.ArgumentList.Add(oldDir);
        psi.ArgumentList.Add(patchPath);
        psi.ArgumentList.Add(outDir);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("hpatchz did not start");

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        try
        {
            // Своего прогресса у hpatchz нет — смотрим, сколько байт уже легло в staging.
            while (!process.HasExited)
            {
                progress.Report(new ProgressInfo("patch", DirectorySize(outDir), expectedBytes));
                await Task.WhenAny(process.WaitForExitAsync(ct), Task.Delay(200, ct)).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        progress.Report(new ProgressInfo("patch", expectedBytes, expectedBytes));

        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);

        // Код вроде 106 — исходный файл у игрока не совпал с тем, от которого строился патч.
        if (process.ExitCode != 0)
            throw new InvalidDataException($"hpatchz exit code {process.ExitCode}");
    }

    /// <summary>hpatchz.exe вшит в лаунчер ресурсом — достаём его в папку состояния при первой надобности.</summary>
    private static async Task<string> EnsureHPatchAsync(CancellationToken ct)
    {
        var dir = Path.Combine(Config.StateDir, "tools");
        var path = Path.Combine(dir, $"hpatchz-{HPatchVersion}.exe");

        await using var resource = typeof(Patcher).Assembly.GetManifestResourceStream("hpatchz.exe")
            ?? throw new InvalidOperationException("hpatchz.exe is missing from the launcher build");

        if (File.Exists(path) && new FileInfo(path).Length == resource.Length)
            return path;

        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";

        await using (var target = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            await resource.CopyToAsync(target, ct).ConfigureAwait(false);

        File.Move(tmp, path, overwrite: true);
        return path;
    }

    // ---------------------------------------------------------------- проверка и замена

    private static async Task VerifyStagedAsync(
        List<(PatchFile File, string Staged, string Target)> files,
        long total,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        long done = 0;
        var buffer = new byte[1 << 20];

        foreach (var (file, staged, _) in files)
        {
            if (!File.Exists(staged) || new FileInfo(staged).Length != file.Size)
                throw new InvalidDataException($"Patch produced a wrong file: {file.Path}");

            await using var stream = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            using var sha = SHA256.Create();

            int read;
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                done += read;
                progress.Report(new ProgressInfo("verify", done, total));
            }

            sha.TransformFinalBlock([], 0, 0);
            var actual = Convert.ToHexString(sha.Hash!);

            if (!string.Equals(actual, file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Checksum mismatch after patching: {file.Path}");
        }
    }

    private static void Commit(
        List<(PatchFile File, string Staged, string Target)> files,
        List<string> removals,
        string installRoot,
        UpdateManifest manifest,
        LocalState state)
    {
        var protectedPaths = Updater.ProtectedPaths(state, installRoot);

        state.UpdateInProgress = true;
        state.Save();

        foreach (var (_, staged, target) in files)
        {
            if (protectedPaths.Contains(target))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(staged, target, overwrite: true);
        }

        foreach (var path in removals)
        {
            if (protectedPaths.Contains(path))
                continue;

            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);

            RemoveEmptyParents(Path.GetDirectoryName(path), installRoot);
        }

        state.UpdateInProgress = false;
        state.Version = manifest.Version;
        state.InstallPath = installRoot;
        state.Save();
    }

    private static void RemoveEmptyParents(string? dir, string installRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(installRoot);

        try
        {
            while (!string.IsNullOrEmpty(dir)
                   && dir.Length > root.Length
                   && Directory.Exists(dir)
                   && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
            // Пустая папка осталась — не беда.
        }
    }

    // ---------------------------------------------------------------- мелочи

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch
        {
            return 0; // файл как раз пишется — посчитаем в следующий раз
        }
    }

    public static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Удалим при следующем обновлении.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // Уже завершился.
        }
    }
}
