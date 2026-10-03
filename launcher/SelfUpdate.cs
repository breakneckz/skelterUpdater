using System.Diagnostics;

namespace SkelterLauncher;

/// <summary>
/// Самообновление лаунчера. Запущенный exe нельзя перезаписать, но можно переименовать:
/// старый уезжает в .old, новый встаёт на его место, лаунчер перезапускается.
/// </summary>
internal static class SelfUpdate
{
    /// <summary>Передаётся перезапущенному лаунчеру — второй раз подряд он себя не обновляет.</summary>
    public const string JustUpdatedArg = "--self-updated";

    public static Version Current => typeof(SelfUpdate).Assembly.GetName().Version ?? new Version(0, 0);

    public static string SelfPath => Path.GetFullPath(Environment.ProcessPath ?? Application.ExecutablePath);

    public static bool JustUpdated => Environment.GetCommandLineArgs().Contains(JustUpdatedArg);

    public static bool IsNewer(LauncherInfo? info) =>
        info is not null
        && !string.IsNullOrWhiteSpace(info.Url)
        && Version.TryParse(info.Version, out var available)
        && available > Current;

    /// <summary>Куда качать новый exe: рядом с собой, чтобы замена была переименованием на том же диске.</summary>
    public static string DownloadPath => SelfPath + ".new";

    /// <summary>
    /// Встаёт на место текущего exe и запускает новую версию. Если замена не вышла — возвращает всё как было.
    ///
    /// Лаунчер — single-file: .NET дочитывает сборки из самого exe по мере надобности. После переименования
    /// exe по старому пути больше нет, поэтому всё, что нужно для перезапуска, готовим заранее —
    /// иначе Process.Start упадёт с FileNotFoundException на System.Diagnostics.Process.
    /// </summary>
    public static void ReplaceAndRestart(string newExe)
    {
        var self = SelfPath;
        var old = self + ".old";

        var startInfo = new ProcessStartInfo(self)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(self)!,
            ArgumentList = { JustUpdatedArg },
        };

        if (File.Exists(old))
            File.Delete(old);

        File.Move(self, old);

        try
        {
            File.Move(newExe, self);
        }
        catch
        {
            File.Move(old, self);
            throw;
        }

        // Новая копия подождёт, пока мы закроемся и отпустим мьютекс (см. Program).
        Process.Start(startInfo);
    }

    /// <summary>Подчищает .old после самообновления. Старый процесс может ещё завершаться — пробуем несколько раз.</summary>
    public static void CleanupLeftoversInBackground()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var old = SelfPath + ".old";
                    if (File.Exists(old))
                        File.Delete(old);
                    return;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}
