using System.Diagnostics;

namespace SkelterLauncher;

internal sealed class MainForm : Form
{
    private readonly Updater _updater = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _launcherDir;
    private readonly LocalState _state;

    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly Label _versions = new();
    private readonly Button _action = new();
    private readonly LinkLabel _openFolder = new();
    private readonly ProgressBarEx _progress = new();

    private UpdateManifest? _manifest;
    private bool _busy;
    private bool _readyToPlay;

    public MainForm()
    {
        _launcherDir = Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath)!;
        _state = LocalState.Load(_launcherDir);

        BuildUi();
    }

    private string InstallDir => _state.InstallPath!;

    // ---------------------------------------------------------------- интерфейс

    private void BuildUi()
    {
        Text = $"{Config.AppName} — лаунчер";
        ClientSize = new Size(560, 300);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch
        {
            // Без иконки тоже живём.
        }

        _title.Text = Config.AppName.ToUpperInvariant();
        _title.Font = new Font("Segoe UI", 24f, FontStyle.Bold);
        _title.ForeColor = Theme.Text;
        _title.AutoSize = true;
        _title.Location = new Point(32, 34);

        var subtitle = new Label
        {
            Text = "ЛАУНЧЕР",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(35, 82),
        };

        _versions.Font = new Font("Segoe UI", 8.5f);
        _versions.ForeColor = Theme.TextMuted;
        _versions.AutoSize = true;
        _versions.Location = new Point(35, 104);

        _status.Font = new Font("Segoe UI", 10.5f);
        _status.ForeColor = Theme.Text;
        _status.AutoSize = false;
        _status.Location = new Point(32, 152);
        _status.Size = new Size(496, 24);

        _progress.Location = new Point(34, 184);
        _progress.Size = new Size(492, 8);

        _detail.Font = new Font("Segoe UI", 8.5f);
        _detail.ForeColor = Theme.TextMuted;
        _detail.AutoSize = false;
        _detail.Location = new Point(32, 200);
        _detail.Size = new Size(496, 20);

        _action.Text = "ПРОВЕРКА…";
        _action.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _action.Size = new Size(200, 46);
        _action.Location = new Point(328, 232);
        _action.FlatStyle = FlatStyle.Flat;
        _action.FlatAppearance.BorderSize = 0;
        _action.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
        _action.FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
        _action.BackColor = Theme.Accent;
        _action.ForeColor = Color.White;
        _action.Enabled = false;
        _action.Cursor = Cursors.Hand;
        _action.Click += OnActionClick;

        _openFolder.Text = "Папка игры";
        _openFolder.Font = new Font("Segoe UI", 8.5f);
        _openFolder.LinkColor = Theme.TextMuted;
        _openFolder.ActiveLinkColor = Theme.Accent;
        _openFolder.LinkBehavior = LinkBehavior.HoverUnderline;
        _openFolder.AutoSize = true;
        _openFolder.Location = new Point(34, 248);
        _openFolder.Click += (_, _) => OpenInstallFolder();

        Controls.AddRange([_title, subtitle, _versions, _status, _progress, _detail, _action, _openFolder]);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = RunFlowAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Недокачанный архив остаётся как .part — при следующем запуске докачается.
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    // ---------------------------------------------------------------- сценарий

    private async Task RunFlowAsync()
    {
        if (_busy)
            return;

        _busy = true;
        _readyToPlay = false;
        _action.Enabled = false;
        _detail.Text = "";
        UpdateVersionsLabel(null);

        try
        {
            SetStatus("Проверяем обновления…");
            _progress.Indeterminate = true;

            try
            {
                _manifest = await _updater.FetchManifestAsync(_cts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HandleOffline(ex);
                return;
            }

            UpdateVersionsLabel(_manifest);

            if (!NeedsUpdate(_manifest))
            {
                _progress.Indeterminate = false;
                _progress.Value = 1;
                SetStatus("Установлена последняя версия", Theme.Success);
                EnablePlay();
                return;
            }

            await DownloadVerifyInstallAsync(_manifest);

            _progress.Indeterminate = false;
            _progress.Value = 1;
            SetStatus($"Версия {_manifest.Version} установлена", Theme.Success);
            _detail.Text = _manifest.Notes ?? "";
            UpdateVersionsLabel(_manifest);
            EnablePlay();
        }
        catch (OperationCanceledException)
        {
            // Окно закрывают — доделывать нечего.
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Скачать, сверить хеш и распаковать. При битом хеше — одна повторная попытка с нуля.</summary>
    private async Task DownloadVerifyInstallAsync(UpdateManifest manifest)
    {
        var cacheDir = Path.Combine(_launcherDir, "updates");
        var zipPath = Path.Combine(cacheDir, FileNameFor(manifest));

        for (var attempt = 1; ; attempt++)
        {
            var reporter = new Progress<ProgressInfo>(OnProgress);

            if (NeedsDownload(zipPath, manifest))
            {
                SetStatus($"Скачиваем обновление {manifest.Version}…");
                await _updater.DownloadAsync(manifest, zipPath, reporter, _cts.Token);
            }

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                SetStatus("Проверяем архив…");
                _detail.Text = "";

                var actual = await Updater.ComputeSha256Async(zipPath, reporter, _cts.Token);
                if (!string.Equals(actual, manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(zipPath);

                    if (attempt >= 2)
                        throw new InvalidDataException(
                            "Скачанный архив повреждён (не совпадает контрольная сумма). Попробуйте позже.");

                    SetStatus("Архив повреждён, качаем заново…", Theme.Danger);
                    continue;
                }
            }

            SetStatus($"Устанавливаем {manifest.Version}…");
            _detail.Text = "";
            await Updater.InstallAsync(zipPath, InstallDir, manifest, _state, reporter, _cts.Token);

            // Архив больше не нужен — освобождаем место.
            TryDelete(zipPath);
            return;
        }
    }

    private bool NeedsUpdate(UpdateManifest manifest)
    {
        if (_state.UpdateInProgress)
            return true; // прошлая установка оборвалась

        if (!File.Exists(Path.Combine(InstallDir, manifest.ExecutableOrDefault)))
            return true; // игры ещё нет либо её снесли

        return !string.Equals(_state.Version, manifest.Version, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsDownload(string zipPath, UpdateManifest manifest)
    {
        if (!File.Exists(zipPath))
            return true;

        // Целый архив с нужным размером мог остаться с прошлого оборванного запуска.
        return manifest.Size > 0 && new FileInfo(zipPath).Length != manifest.Size;
    }

    private void OnProgress(ProgressInfo p)
    {
        _progress.Indeterminate = false;
        _progress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;

        var percent = p.Total > 0 ? $"{p.Done * 100 / p.Total}%" : "";

        _detail.Text = p.Stage switch
        {
            "download" => DownloadDetail(p, percent),
            "verify" => $"Проверка целостности   {percent}",
            "install" => $"Распаковка   {Format.Bytes(p.Done)} из {Format.Bytes(p.Total)}   {percent}",
            _ => "",
        };
    }

    private static string DownloadDetail(ProgressInfo p, string percent)
    {
        var parts = new List<string> { $"{Format.Bytes(p.Done)} из {Format.Bytes(p.Total)}", percent };

        if (p.BytesPerSecond is { } speed)
        {
            parts.Add(Format.Speed(speed));

            var eta = Format.Eta(p.Total - p.Done, speed);
            if (eta.Length > 0)
                parts.Add($"осталось {eta}");
        }

        return string.Join("   ", parts.Where(x => x.Length > 0));
    }

    // ---------------------------------------------------------------- состояния

    private void HandleOffline(Exception ex)
    {
        _progress.Indeterminate = false;

        var installed = !_state.UpdateInProgress
                        && _state.Version is not null
                        && File.Exists(Path.Combine(InstallDir, Config.DefaultExecutable));

        if (installed)
        {
            _progress.Value = 1;
            SetStatus("Нет связи с сервером обновлений — играем на текущей версии", Theme.TextMuted);
            _detail.Text = Short(ex);
            EnablePlay();
            return;
        }

        Fail(ex, "Не удалось получить список версий. Проверьте интернет.");
    }

    private void Fail(Exception ex, string? headline = null)
    {
        _progress.Indeterminate = false;
        _progress.Value = 0;
        SetStatus(headline ?? "Ошибка обновления", Theme.Danger);
        _detail.Text = Short(ex);

        _readyToPlay = false;
        _action.Text = "ПОВТОРИТЬ";
        _action.Enabled = true;
    }

    private void EnablePlay()
    {
        _readyToPlay = true;
        _action.Text = "ИГРАТЬ";
        _action.Enabled = true;
        _action.Focus();
    }

    private void OnActionClick(object? sender, EventArgs e)
    {
        if (!_readyToPlay)
        {
            _ = RunFlowAsync();
            return;
        }

        var exe = Path.Combine(InstallDir, _manifest?.ExecutableOrDefault ?? Config.DefaultExecutable);

        if (!File.Exists(exe))
        {
            SetStatus("Файл игры не найден — переустанавливаем", Theme.Danger);
            _state.Version = null;
            _state.Save();
            _ = RunFlowAsync();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = InstallDir,
                UseShellExecute = true,
            });

            Close();
        }
        catch (Exception ex)
        {
            Fail(ex, "Не удалось запустить игру");
        }
    }

    // ---------------------------------------------------------------- мелочи

    private void SetStatus(string text, Color? color = null)
    {
        _status.Text = text;
        _status.ForeColor = color ?? Theme.Text;
    }

    private void UpdateVersionsLabel(UpdateManifest? manifest)
    {
        var installed = string.IsNullOrWhiteSpace(_state.Version) ? "не установлена" : _state.Version;
        _versions.Text = manifest is null
            ? $"Установлено: {installed}"
            : $"Установлено: {installed}      Доступно: {manifest.Version}";
    }

    private void OpenInstallFolder()
    {
        try
        {
            Directory.CreateDirectory(InstallDir);
            Process.Start(new ProcessStartInfo(InstallDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _detail.Text = Short(ex);
        }
    }

    private static string FileNameFor(UpdateManifest manifest)
    {
        var fromUrl = Path.GetFileName(new Uri(manifest.Url).LocalPath);
        return string.IsNullOrWhiteSpace(fromUrl) ? $"update-{manifest.Version}.zip" : fromUrl;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Не удалили кеш — не повод падать.
        }
    }

    private static string Short(Exception ex) =>
        ex is HttpRequestException ? $"Сеть: {ex.Message}" : ex.Message;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cts.Dispose();

        base.Dispose(disposing);
    }
}
