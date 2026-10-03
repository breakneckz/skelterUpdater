namespace SkelterLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
        }
    }

    private static void Run()
    {
        var launcherDir = Path.GetDirectoryName(SelfUpdate.SelfPath)!;
        var state = LocalState.Load(launcherDir);
        Loc.Current = Loc.Parse(state.Language);

        // Две копии лаунчера, качающие один архив, затрут работу друг друга.
        var single = new Mutex(initiallyOwned: true, @"Global\SkelterArenaLauncher", out var isFirst);

        // Сразу после самообновления старая копия ещё закрывается — даём ей отпустить мьютекс.
        if (!isFirst && SelfUpdate.JustUpdated)
            isFirst = WaitForPreviousInstance(single);

        if (!isFirst)
        {
            single.Dispose();
            MessageBox.Show(
                Loc.AlreadyRunning,
                Config.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SelfUpdate.CleanupLeftoversInBackground();

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        Application.Run(new MainForm(launcherDir, state));

        single.ReleaseMutex();
        single.Dispose();
    }

    private static bool WaitForPreviousInstance(Mutex single)
    {
        try
        {
            return single.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            return true; // старая копия завершилась, не отпустив мьютекс, — он теперь наш
        }
    }

    private static void ReportCrash(Exception? ex)
    {
        var text = ex?.ToString() ?? Loc.UnknownError;

        try
        {
            // Игрок может прислать этот файл — по нему видно, что сломалось.
            Directory.CreateDirectory(Config.StateDir);
            File.AppendAllText(
                Path.Combine(Config.StateDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {SelfUpdate.Current}{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Не записали лог — хотя бы покажем окно.
        }

        MessageBox.Show(text, Loc.CrashTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
