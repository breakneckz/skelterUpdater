using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SkelterLauncher;

internal readonly record struct ProgressInfo(string Stage, long Done, long Total, double? BytesPerSecond = null);

internal sealed class Updater
{
    private readonly HttpClient _http;

    public Updater()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.UserAgent);
    }

    // ---------------------------------------------------------------- манифест

    public async Task<UpdateManifest> FetchManifestAsync(CancellationToken ct)
    {
        // raw.githubusercontent кеширует ответы на CDN — добиваем запрос уникальным параметром.
        var url = $"{Config.ManifestUrl}?nocache={Guid.NewGuid():N}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return UpdateManifest.Parse(json);
    }

    // ---------------------------------------------------------------- скачивание

    /// <summary>
    /// Качает архив в dest.part с докачкой при обрыве, затем переименовывает в dest.
    /// </summary>
    public async Task DownloadAsync(UpdateManifest manifest, string dest, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var part = dest + ".part";
        long resumeFrom = File.Exists(part) ? new FileInfo(part).Length : 0;

        // Если прошлый .part больше заявленного размера — он мусорный.
        if (manifest.Size > 0 && resumeFrom > manifest.Size)
        {
            File.Delete(part);
            resumeFrom = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.Url);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // Сервер считает, что файл уже целиком у нас — целостность всё равно проверит хеш.
            File.Move(part, dest, overwrite: true);
            return;
        }

        // Докачка не поддержана сервером — начинаем заново.
        if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            resumeFrom = 0;

        response.EnsureSuccessStatusCode();

        long total = manifest.Size > 0
            ? manifest.Size
            : (response.Content.Headers.ContentLength ?? 0) + resumeFrom;

        EnsureFreeSpace(dest, total - resumeFrom);

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = new FileStream(
                         part,
                         resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         1 << 20,
                         useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long done = resumeFrom;
            var clock = Stopwatch.StartNew();
            long sinceReport = 0;
            var lastReport = TimeSpan.Zero;

            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                sinceReport += read;

                var elapsed = clock.Elapsed;
                var window = (elapsed - lastReport).TotalSeconds;
                if (window >= 0.2)
                {
                    progress.Report(new ProgressInfo("download", done, total, sinceReport / window));
                    lastReport = elapsed;
                    sinceReport = 0;
                }
            }

            progress.Report(new ProgressInfo("download", done, total));
        }

        File.Move(part, dest, overwrite: true);
    }

    // ---------------------------------------------------------------- проверка целостности

    public static async Task<string> ComputeSha256Async(string path, IProgress<ProgressInfo> progress, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var sha = SHA256.Create();

        var buffer = new byte[1 << 20];
        long done = 0;
        long total = stream.Length;

        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buffer, 0, read, null, 0);
            done += read;
            progress.Report(new ProgressInfo("verify", done, total));
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // ---------------------------------------------------------------- установка

    public static async Task InstallAsync(
        string zipPath,
        string installDir,
        UpdateManifest manifest,
        LocalState state,
        IProgress<ProgressInfo> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(installDir);
        var installRoot = Path.GetFullPath(installDir);

        using var archive = ZipFile.OpenRead(zipPath);

        // Записи каталогов в zip имеют пустое Name — они нам не нужны, папки создаём сами.
        var files = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var rootPrefix = FindCommonRootFolder(files);

        var plan = new List<(ZipArchiveEntry Entry, string TargetPath)>(files.Count);

        foreach (var entry in files)
        {
            var relative = StripRoot(entry.FullName, rootPrefix).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(installRoot, relative));

            // Zip slip: запись не должна вылезать за пределы папки установки.
            if (!targetPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Архив содержит недопустимый путь: {entry.FullName}");

            plan.Add((entry, targetPath));
        }

        long total = plan.Sum(p => p.Entry.Length);

        // Обновление поверх существующей установки занимает только разницу в размерах.
        EnsureFreeSpace(installRoot, plan.Sum(p => GrowthOf(p.Entry, p.TargetPath)));

        var protectedPaths = ProtectedPaths(state);

        // Флаг на время распаковки: если процесс убьют посередине, при следующем старте
        // мы это увидим и поставим версию заново вместо запуска битой игры.
        state.UpdateInProgress = true;
        state.Save();

        if (manifest.CleanInstall)
            CleanInstallDir(installRoot, protectedPaths);

        long done = 0;

        foreach (var (entry, targetPath) in plan)
        {
            ct.ThrowIfCancellationRequested();

            // Себя и свой installed.json не перезаписываем — лаунчер сейчас запущен.
            if (protectedPaths.Contains(targetPath))
            {
                done += entry.Length;
                progress.Report(new ProgressInfo("install", done, total));
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            await using (var source = entry.Open())
            await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                var buffer = new byte[1 << 20];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress.Report(new ProgressInfo("install", done, total));
                }
            }

            try
            {
                File.SetLastWriteTime(targetPath, entry.LastWriteTime.LocalDateTime);
            }
            catch
            {
                // Время файла — косметика, не повод валить установку.
            }
        }

        state.UpdateInProgress = false;
        state.Version = manifest.Version;
        state.InstallPath = installRoot;
        state.Save();
    }

    /// <summary>Сколько места реально добавит запись: для уже лежащего файла — только прирост размера.</summary>
    private static long GrowthOf(ZipArchiveEntry entry, string targetPath)
    {
        try
        {
            return File.Exists(targetPath)
                ? Math.Max(0, entry.Length - new FileInfo(targetPath).Length)
                : entry.Length;
        }
        catch
        {
            return entry.Length;
        }
    }

    /// <summary>Файлы, которые нельзя трогать при распаковке и очистке.</summary>
    private static HashSet<string> ProtectedPaths(LocalState state)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var self = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(self))
            set.Add(Path.GetFullPath(self));

        if (!string.IsNullOrEmpty(state.StatePath))
            set.Add(Path.GetFullPath(state.StatePath));

        return set;
    }

    /// <summary>Сносит содержимое папки установки, кроме защищённых файлов.</summary>
    private static void CleanInstallDir(string installRoot, HashSet<string> protectedPaths)
    {
        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (protectedPaths.Contains(full))
                continue;

            try
            {
                File.Delete(full);
            }
            catch
            {
                // Занятый файл просто перезапишется на следующем шаге.
            }
        }

        // Сначала самые глубокие папки, иначе родитель ещё не пуст.
        foreach (var dir in Directory.EnumerateDirectories(installRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // Пустую папку не удалили — ничего страшного.
            }
        }
    }

    /// <summary>Если все файлы архива лежат в одной общей папке — при распаковке её срезаем.</summary>
    private static string? FindCommonRootFolder(List<ZipArchiveEntry> files)
    {
        if (files.Count == 0)
            return null;

        string? root = null;

        foreach (var entry in files)
        {
            var slash = entry.FullName.IndexOf('/');
            if (slash <= 0)
                return null; // есть файл в корне архива — срезать нечего

            var candidate = entry.FullName[..slash];
            root ??= candidate;

            if (!string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return root;
    }

    private static string StripRoot(string fullName, string? rootPrefix) =>
        rootPrefix is null ? fullName : fullName[(rootPrefix.Length + 1)..];

    private static void EnsureFreeSpace(string path, long needed)
    {
        if (needed <= 0)
            return;

        string? root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return; // нестандартный путь — просто не проверяем
        }

        if (string.IsNullOrEmpty(root))
            return;

        long free;
        try
        {
            free = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (ArgumentException)
        {
            return; // сетевой путь и т.п.
        }

        // Запас в 256 МБ, чтобы не упереться в ноль на последнем файле.
        if (free < needed + (256L << 20))
        {
            throw new IOException(
                $"Недостаточно места на диске {root}. Нужно ~{Format.Bytes(needed)}, свободно {Format.Bytes(free)}.");
        }
    }
}
