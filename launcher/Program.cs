namespace SkelterLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Две копии лаунчера, качающие один архив, затрут работу друг друга.
        using var single = new Mutex(initiallyOwned: true, @"Global\SkelterArenaLauncher", out var isFirst);

        if (!isFirst)
        {
            MessageBox.Show(
                "Лаунчер уже запущен.",
                Config.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        Application.Run(new MainForm());
    }

    private static void ReportCrash(Exception? ex)
    {
        MessageBox.Show(
            ex?.ToString() ?? "Неизвестная ошибка",
            $"{Config.AppName} — сбой лаунчера",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
