using System.Diagnostics;

namespace SkelterLauncher;

internal sealed class MainForm : Form
{
    private enum ActionMode { None, Install, Play, Retry }

    private readonly Updater _updater = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _launcherDir;
    private readonly LocalState _state;

    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly Label _detail = new();
    private readonly Label _versions = new();
    private readonly Label _pathCaption = new();
    private readonly TextBox _path = new();
    private readonly Button _browse = new();
    private readonly CheckBox _shortcut = new();
    private readonly Button _action = new();
    private readonly LinkLabel _openFolder = new();
    private readonly ProgressBarEx _progress = new();

    private UpdateManifest? _manifest;
    private bool _busy;
    private ActionMode _mode;

    /// <summary>Галочку выставляет код, а не игрок — ярлык трогать не надо.</summary>
    private bool _syncingShortcut;

    public MainForm()
    {
        _launcherDir = Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath)!;
        _state = LocalState.Load(_launcherDir);

        BuildUi();
        SyncShortcutCheckbox();
        UpdateLocationControls();
    }

    private string InstallDir => _state.InstallPath!;

    private string GameExecutable =>
        Path.Combine(InstallDir, _manifest?.ExecutableOrDefault ?? Config.DefaultExecutable);

    /// <summary>Игры ещё нет (и это не оборванная установка) — можно выбрать, куда ставить.</summary>
    private bool IsFreshInstall => !_state.UpdateInProgress && !File.Exists(GameExecutable);

    // ---------------------------------------------------------------- интерфейс

    private void BuildUi()
    {
        Text = $"{Config.AppName} — лаунчер";
        ClientSize = new Size(560, 372);
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

        _pathCaption.Font = new Font("Segoe UI", 8.5f);
        _pathCaption.ForeColor = Theme.TextMuted;
        _pathCaption.AutoSize = true;
        _pathCaption.Location = new Point(33, 138);

        _path.Font = Font;
        _path.ReadOnly = true;
        _path.BorderStyle = BorderStyle.FixedSingle;
        _path.BackColor = Theme.Surface;
        _path.ForeColor = Theme.Text;
        _path.TabStop = false;
        _path.Location = new Point(34, 159);
        _path.Width = 396;

        _browse.Text = "Обзор…";
        _browse.Size = new Size(90, _path.Height + 2);
        _browse.Location = new Point(436, 158);
        _browse.FlatStyle = FlatStyle.Flat;
        _browse.FlatAppearance.BorderColor = Theme.Track;
        _browse.FlatAppearance.MouseOverBackColor = Theme.Track;
        _browse.FlatAppearance.MouseDownBackColor = Theme.Surface;
        _browse.BackColor = Theme.Surface;
        _browse.ForeColor = Theme.Text;
        _browse.Cursor = Cursors.Hand;
        _browse.Click += (_, _) => ChooseInstallFolder();

        _status.Font = new Font("Segoe UI", 10.5f);
        _status.ForeColor = Theme.Text;
        _status.AutoSize = false;
        _status.Location = new Point(32, 204);
        _status.Size = new Size(496, 24);

        _progress.Location = new Point(34, 236);
        _progress.Size = new Size(492, 8);

        _detail.Font = new Font("Segoe UI", 8.5f);
        _detail.ForeColor = Theme.TextMuted;
        _detail.AutoSize = false;
        _detail.Location = new Point(32, 252);
        _detail.Size = new Size(496, 20);

        _action.Text = "ПРОВЕРКА…";
        _action.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
        _action.Size = new Size(200, 46);
        _action.Location = new Point(328, 302);
        _action.FlatStyle = FlatStyle.Flat;
        _action.FlatAppearance.BorderSize = 0;
        _action.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
        _action.FlatAppearance.MouseDownBackColor = Theme.AccentPressed;
        _action.BackColor = Theme.Accent;
        _action.ForeColor = Color.White;
        _action.Enabled = false;
        _action.Cursor = Cursors.Hand;
        _action.Click += OnActionClick;

        _shortcut.Text = "Создать ярлык игры на рабочем столе";
        _shortcut.Font = new Font("Segoe UI", 9f);
        _shortcut.ForeColor = Theme.Text;
        _shortcut.FlatStyle = FlatStyle.Flat;
        _shortcut.FlatAppearance.BorderColor = Theme.TextMuted;
        _shortcut.FlatAppearance.CheckedBackColor = Theme.Accent;
        _shortcut.AutoSize = true;
        _shortcut.Location = new Point(34, 304);
        _shortcut.Cursor = Cursors.Hand;
        _shortcut.CheckedChanged += OnShortcutToggled;

        _openFolder.Text = "Папка игры";
        _openFolder.Font = new Font("Segoe UI", 8.5f);
        _openFolder.LinkColor = Theme.TextMuted;
        _openFolder.ActiveLinkColor = Theme.Accent;
        _openFolder.LinkBehavior = LinkBehavior.HoverUnderline;
        _openFolder.AutoSize = true;
        _openFolder.Location = new Point(34, 332);
        _openFolder.Click += (_, _) => OpenInstallFolder();

        Controls.AddRange([
            _title, subtitle, _versions, _pathCaption, _path, _browse,
            _status, _progress, _detail, _action, _shortcut, _openFolder,
        ]);
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

    private Task RunFlowAsync() => RunGuardedAsync(async () =>
    {
        UpdateVersionsLabel(null);
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

        if (IsFreshInstall)
        {
            // Первая установка: ждём, пока игрок выберет папку и нажмёт «Установить».
            _progress.Indeterminate = false;
            _progress.Value = 0;
            SetStatus("Выберите папку и нажмите «Установить»");
            _detail.Text = SpaceHint();
            SetMode(ActionMode.Install);
            return;
        }

        if (!NeedsUpdate(_manifest))
        {
            _progress.Indeterminate = false;
            _progress.Value = 1;
            SetStatus("Установлена последняя версия", Theme.Success);
            EnablePlay();
            return;
        }

        await InstallAndFinishAsync(_manifest);
    });

    private Task RunInstallAsync()
    {
        if (!InstallLocation.CanWriteTo(InstallDir, out var error))
        {
            SetStatus("В эту папку установить нельзя — выберите другую", Theme.Danger);
            _detail.Text = error;
            return Task.CompletedTask;
        }

        return RunGuardedAsync(() => InstallAndFinishAsync(_manifest!));
    }

    private async Task RunGuardedAsync(Func<Task> work)
    {
        if (_busy)
            return;

        _busy = true;
        SetMode(ActionMode.None);
        _detail.Text = "";
        UpdateLocationControls();

        try
        {
            await work();
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
            UpdateLocationControls();
        }
    }

    private async Task InstallAndFinishAsync(UpdateManifest manifest)
    {
        await DownloadVerifyInstallAsync(manifest);

        _progress.Indeterminate = false;
        _progress.Value = 1;
        SetStatus($"Версия {manifest.Version} установлена", Theme.Success);
        _detail.Text = manifest.Notes ?? "";
        UpdateVersionsLabel(manifest);

        if (_shortcut.Checked)
        {
            SetStatus("Создаём ярлык на рабочем столе…");
            var error = await ApplyShortcutAsync();
            SetStatus($"Версия {manifest.Version} установлена", Theme.Success);

            if (error is not null)
                _detail.Text = $"Ярлык не создан: {error}";
        }

        EnablePlay();
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

    // ---------------------------------------------------------------- папка установки

    private void ChooseInstallFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Куда установить {Config.AppName}",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = InstallLocation.ExistingAncestor(InstallDir) ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        var target = InstallLocation.FromUserChoice(dialog.SelectedPath);

        if (!InstallLocation.CanWriteTo(target, out var error))
        {
            SetStatus("В эту папку установить нельзя — выберите другую", Theme.Danger);
            _detail.Text = error;
            return;
        }

        _state.InstallPath = target;
        TrySaveState();

        UpdateLocationControls();

        // Если в выбранной папке уже лежит игра — перепроверяем версию вместо установки с нуля.
        if (!IsFreshInstall)
        {
            SyncShortcutCheckbox();
            _ = RunFlowAsync();
            return;
        }

        SetStatus("Выберите папку и нажмите «Установить»");
        _detail.Text = SpaceHint();

        if (_manifest is not null)
            SetMode(ActionMode.Install);
    }

    private void UpdateLocationControls()
    {
        var canChoose = !_busy && IsFreshInstall;

        _pathCaption.Text = IsFreshInstall ? "Папка установки" : "Игра установлена в";
        _path.Text = InstallDir;
        _path.Width = canChoose ? 396 : 492;
        _browse.Visible = canChoose;
        _shortcut.Enabled = !_busy;
    }

    private string SpaceHint()
    {
        var parts = new List<string>();

        if (_manifest is { Size: > 0 })
            parts.Add($"Размер загрузки: {Format.Bytes(_manifest.Size)}");

        if (InstallLocation.FreeSpace(InstallDir) is { } free)
            parts.Add($"Свободно на диске {Path.GetPathRoot(Path.GetFullPath(InstallDir))}: {Format.Bytes(free)}");

        return string.Join("   ·   ", parts);
    }

    // ---------------------------------------------------------------- ярлык

    /// <summary>
    /// Для установленной игры галочка показывает, есть ли ярлык сейчас.
    /// До установки — запомненный выбор игрока (по умолчанию включено).
    /// </summary>
    private void SyncShortcutCheckbox()
    {
        _syncingShortcut = true;
        _shortcut.Checked = IsFreshInstall ? _state.DesktopShortcut : Shortcut.Exists;
        _syncingShortcut = false;
    }

    private async void OnShortcutToggled(object? sender, EventArgs e)
    {
        if (_syncingShortcut)
            return;

        _state.DesktopShortcut = _shortcut.Checked;
        TrySaveState();

        // Игры ещё нет — ярлык сделаем сразу после установки.
        if (_busy || IsFreshInstall)
            return;

        var error = await ApplyShortcutAsync();

        _detail.Text = error is not null
            ? $"Ярлык: {error}"
            : _shortcut.Checked ? "Ярлык создан на рабочем столе" : "Ярлык удалён с рабочего стола";
    }

    /// <summary>Создаёт или удаляет ярлык по галочке. Возвращает текст ошибки или null.</summary>
    private async Task<string?> ApplyShortcutAsync()
    {
        _shortcut.Enabled = false;

        try
        {
            if (!_shortcut.Checked)
            {
                Shortcut.Delete();
                return null;
            }

            // Копирование ~60 МБ — не в UI-потоке.
            var target = await Task.Run(EnsureLauncherInInstallDir);
            Shortcut.Create(target, GameExecutable);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            _shortcut.Enabled = !_busy;
        }
    }

    /// <summary>
    /// Ярлык ведёт на лаунчер, а не на игру напрямую — иначе обновления перестанут доходить.
    /// Лаунчер могли запустить из «Загрузок», поэтому кладём его копию в папку игры и целимся в неё.
    /// </summary>
    private string EnsureLauncherInInstallDir()
    {
        var self = Path.GetFullPath(Environment.ProcessPath ?? Application.ExecutablePath);

        if (string.Equals(
                Path.GetDirectoryName(self),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDir)),
                StringComparison.OrdinalIgnoreCase))
        {
            return self; // лаунчер и так живёт в папке игры
        }

        var copy = Path.Combine(InstallDir, Config.LauncherFileName);
        File.Copy(self, copy, overwrite: true);
        return copy;
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
        SetMode(ActionMode.Retry);
    }

    private void EnablePlay()
    {
        SyncShortcutCheckbox();
        SetMode(ActionMode.Play);
    }

    private void SetMode(ActionMode mode)
    {
        _mode = mode;
        _action.Enabled = mode != ActionMode.None;

        switch (mode)
        {
            case ActionMode.Install:
                _action.Text = "УСТАНОВИТЬ";
                break;
            case ActionMode.Play:
                _action.Text = "ИГРАТЬ";
                break;
            case ActionMode.Retry:
                _action.Text = "ПОВТОРИТЬ";
                break;
        }

        if (_action.Enabled)
            _action.Focus();
    }

    private void OnActionClick(object? sender, EventArgs e)
    {
        switch (_mode)
        {
            case ActionMode.Install:
                _ = RunInstallAsync();
                return;
            case ActionMode.Retry:
                _ = RunFlowAsync();
                return;
            case ActionMode.Play:
                LaunchGame();
                return;
        }
    }

    private void LaunchGame()
    {
        var exe = GameExecutable;

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
            // До установки папки может ещё не быть — открываем то, что есть, и не создаём лишнего.
            var dir = Directory.Exists(InstallDir) ? InstallDir : InstallLocation.ExistingAncestor(InstallDir);
            if (dir is not null)
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _detail.Text = Short(ex);
        }
    }

    private void TrySaveState()
    {
        try
        {
            _state.Save();
        }
        catch
        {
            // Не запомнили выбор — спросим ещё раз при следующем запуске.
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
