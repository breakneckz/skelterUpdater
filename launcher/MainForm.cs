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
    private readonly Label _subtitle = new();
    private readonly LinkLabel _langEn = new();
    private readonly LinkLabel _langRu = new();
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

    /// <summary>Что написано на кнопке, пока она заблокирована на время работы.</summary>
    private ActionMode _buttonLabel;

    /// <summary>
    /// Тексты статуса хранятся функциями, а не строками: при переключении языка
    /// всё, что сейчас на экране, перерисовывается на новом языке.
    /// </summary>
    private Func<string> _statusText = () => "";
    private Color _statusColor = Theme.Text;
    private Func<string> _detailText = () => "";

    /// <summary>Галочку выставляет код, а не игрок — ярлык трогать не надо.</summary>
    private bool _syncingShortcut;

    public MainForm(string launcherDir, LocalState state)
    {
        _launcherDir = launcherDir;
        _state = state;

        BuildUi();
        ApplyLanguage();
        SyncShortcutCheckbox();
    }

    private string InstallDir => _state.InstallPath!;

    private string CacheDir => Path.Combine(_launcherDir, "updates");

    private string GameExecutable =>
        Path.Combine(InstallDir, _manifest?.ExecutableOrDefault ?? Config.DefaultExecutable);

    /// <summary>Игры ещё нет (и это не оборванная установка) — можно выбрать, куда ставить.</summary>
    private bool IsFreshInstall => !_state.UpdateInProgress && !File.Exists(GameExecutable);

    // ---------------------------------------------------------------- интерфейс

    private void BuildUi()
    {
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

        _subtitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _subtitle.ForeColor = Theme.Accent;
        _subtitle.AutoSize = true;
        _subtitle.Location = new Point(35, 82);

        SetupLanguageLink(_langEn, "EN", Language.En);
        SetupLanguageLink(_langRu, "RU", Language.Ru);
        _langRu.Location = new Point(ClientSize.Width - 34 - _langRu.PreferredWidth, 22);
        _langEn.Location = new Point(_langRu.Left - 4 - _langEn.PreferredWidth, 22);

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

        _shortcut.Font = new Font("Segoe UI", 9f);
        _shortcut.ForeColor = Theme.Text;
        _shortcut.FlatStyle = FlatStyle.Flat;
        _shortcut.FlatAppearance.BorderColor = Theme.TextMuted;
        _shortcut.FlatAppearance.CheckedBackColor = Theme.Accent;
        _shortcut.AutoSize = true;
        _shortcut.Location = new Point(34, 304);
        _shortcut.Cursor = Cursors.Hand;
        _shortcut.CheckedChanged += OnShortcutToggled;

        _openFolder.Font = new Font("Segoe UI", 8.5f);
        _openFolder.LinkColor = Theme.TextMuted;
        _openFolder.ActiveLinkColor = Theme.Accent;
        _openFolder.LinkBehavior = LinkBehavior.HoverUnderline;
        _openFolder.AutoSize = true;
        _openFolder.Location = new Point(34, 332);
        _openFolder.LinkClicked += (_, _) => OpenInstallFolder();

        Controls.AddRange([
            _title, _subtitle, _langEn, _langRu, _versions, _pathCaption, _path, _browse,
            _status, _progress, _detail, _action, _shortcut, _openFolder,
        ]);
    }

    private void SetupLanguageLink(LinkLabel link, string text, Language language)
    {
        link.Text = text;
        link.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        link.LinkBehavior = LinkBehavior.HoverUnderline;
        link.ActiveLinkColor = Theme.Accent;
        link.AutoSize = true;
        link.TabStop = false;
        link.LinkClicked += (_, _) => SwitchLanguage(language);
    }

    private void SwitchLanguage(Language language)
    {
        if (Loc.Current == language)
            return;

        Loc.Current = language;
        _state.Language = Loc.Code(language);
        TrySaveState();

        ApplyLanguage();
    }

    /// <summary>Перерисовывает все тексты на текущем языке.</summary>
    private void ApplyLanguage()
    {
        Text = Loc.WindowTitle;
        _subtitle.Text = Loc.Subtitle;
        _browse.Text = Loc.Browse;
        _shortcut.Text = Loc.CreateShortcut;
        _openFolder.Text = Loc.OpenGameFolder;

        _langEn.LinkColor = Loc.Current == Language.En ? Theme.Text : Theme.TextMuted;
        _langRu.LinkColor = Loc.Current == Language.Ru ? Theme.Text : Theme.TextMuted;

        _action.Text = _buttonLabel switch
        {
            ActionMode.Install => Loc.ButtonInstall,
            ActionMode.Play => Loc.ButtonPlay,
            ActionMode.Retry => Loc.ButtonRetry,
            _ => Loc.ButtonChecking,
        };

        _status.Text = _statusText();
        _detail.Text = _detailText();
        UpdateVersionsLabel();
        UpdateLocationControls();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ = RunFlowAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Недокачанный файл остаётся как .part — при следующем запуске докачается.
        _cts.Cancel();
        base.OnFormClosing(e);
    }

    // ---------------------------------------------------------------- сценарий

    private Task RunFlowAsync() => RunGuardedAsync(async () =>
    {
        _manifest = null;
        UpdateVersionsLabel();
        SetStatus(() => Loc.CheckingUpdates);
        _progress.Indeterminate = true;

        UpdateManifest manifest;

        try
        {
            manifest = await _updater.FetchManifestAsync(_cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HandleOffline(ex);
            return;
        }

        _manifest = manifest;
        UpdateVersionsLabel();

        if (await TryUpdateLauncherAsync(manifest))
            return;

        if (IsFreshInstall)
        {
            // Первая установка: ждём, пока игрок выберет папку и нажмёт «Установить».
            _progress.Indeterminate = false;
            _progress.Value = 0;
            SetStatus(() => Loc.ChooseFolderAndInstall);
            SetDetail(SpaceHint);
            SetMode(ActionMode.Install);
            return;
        }

        if (!NeedsUpdate(manifest))
        {
            _progress.Indeterminate = false;
            _progress.Value = 1;
            SetStatus(() => Loc.UpToDate, Theme.Success);
            EnablePlay();
            return;
        }

        await InstallAndFinishAsync(manifest);
    });

    private Task RunInstallAsync()
    {
        if (!InstallLocation.CanWriteTo(InstallDir, out var error))
        {
            SetStatus(() => Loc.CantInstallHere, Theme.Danger);
            SetDetail(() => error);
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
        SetDetail(() => "");
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
        await UpdateGameAsync(manifest);

        _progress.Indeterminate = false;
        _progress.Value = 1;
        SetStatus(() => Loc.VersionInstalled(manifest.Version), Theme.Success);
        SetDetail(() => manifest.LocalizedNotes ?? "");
        UpdateVersionsLabel();

        if (_shortcut.Checked)
        {
            SetStatus(() => Loc.CreatingShortcut);
            var error = await ApplyShortcutAsync();
            SetStatus(() => Loc.VersionInstalled(manifest.Version), Theme.Success);

            if (error is not null)
                SetDetail(() => Loc.ShortcutFailed(error));
        }

        EnablePlay();
    }

    /// <summary>
    /// Есть патч с установленной версии — качаем только его. Не подошёл (игрок пропустил много версий,
    /// файлы игры изменены или повреждены) — откатываемся на полный архив, как раньше.
    /// </summary>
    private async Task UpdateGameAsync(UpdateManifest manifest)
    {
        var patch = !_state.UpdateInProgress && File.Exists(GameExecutable)
            ? manifest.PatchFrom(_state.Version)
            : null;

        if (patch is not null)
        {
            try
            {
                await ApplyPatchAsync(manifest, patch);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus(() => Loc.PatchFallback, Theme.Danger);
                SetDetail(() => Short(ex));
                await Task.Delay(1500, _cts.Token);
            }
        }

        await DownloadVerifyInstallAsync(manifest);
    }

    private async Task ApplyPatchAsync(UpdateManifest manifest, PatchInfo patch)
    {
        string? patchPath = null;

        try
        {
            if (patch.HasData)
            {
                patchPath = Path.Combine(CacheDir, FileNameFor(patch.Url, $"patch-{patch.From}-{manifest.Version}.hdiff"));
                await DownloadVerifiedAsync(patch, patchPath, () => Loc.DownloadingPatch(manifest.Version));
            }

            SetStatus(() => Loc.ApplyingPatch(manifest.Version));
            SetDetail(() => "");
            await Patcher.ApplyAsync(patchPath, patch, InstallDir, manifest, _state, new Progress<ProgressInfo>(OnProgress), _cts.Token);
        }
        finally
        {
            // Патч подходит только к одной версии — хранить его незачем, даже если не встал.
            if (patchPath is not null && !_cts.IsCancellationRequested)
                TryDelete(patchPath);
        }
    }

    /// <summary>Скачать полный архив, сверить хеш и распаковать.</summary>
    private async Task DownloadVerifyInstallAsync(UpdateManifest manifest)
    {
        var zipPath = Path.Combine(CacheDir, FileNameFor(manifest.Url, $"update-{manifest.Version}.zip"));

        await DownloadVerifiedAsync(manifest, zipPath, () => Loc.Downloading(manifest.Version));

        SetStatus(() => Loc.Installing(manifest.Version));
        SetDetail(() => "");
        await Updater.InstallAsync(zipPath, InstallDir, manifest, _state, new Progress<ProgressInfo>(OnProgress), _cts.Token);

        // Архив больше не нужен — освобождаем место.
        TryDelete(zipPath);
    }

    /// <summary>Скачать файл и сверить SHA-256. При битом хеше — одна повторная попытка с нуля.</summary>
    private async Task DownloadVerifiedAsync(IRemoteFile file, string path, Func<string> downloadingText)
    {
        var reporter = new Progress<ProgressInfo>(OnProgress);

        for (var attempt = 1; ; attempt++)
        {
            if (NeedsDownload(path, file))
            {
                SetStatus(downloadingText);
                await _updater.DownloadAsync(file, path, reporter, _cts.Token);
            }

            if (string.IsNullOrWhiteSpace(file.Sha256))
                return;

            SetStatus(() => Loc.Verifying);
            SetDetail(() => "");

            var actual = await Updater.ComputeSha256Async(path, reporter, _cts.Token);
            if (string.Equals(actual, file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                return;

            TryDelete(path);

            if (attempt >= 2)
                throw new InvalidDataException(Loc.CorruptedFinal);

            SetStatus(() => Loc.Corrupted, Theme.Danger);
        }
    }

    // ---------------------------------------------------------------- самообновление

    /// <summary>
    /// Если в манифесте лежит лаунчер новее нашего — ставим его и перезапускаемся.
    /// Любая неудача здесь не мешает играть: просто продолжаем на текущем лаунчере.
    /// </summary>
    private async Task<bool> TryUpdateLauncherAsync(UpdateManifest manifest)
    {
        if (SelfUpdate.JustUpdated || !SelfUpdate.IsNewer(manifest.Launcher))
            return false;

        try
        {
            var newExe = SelfUpdate.DownloadPath;
            await DownloadVerifiedAsync(manifest.Launcher!, newExe, () => Loc.UpdatingLauncher);

            SetStatus(() => Loc.RestartingLauncher);
            SelfUpdate.ReplaceAndRestart(newExe);

            BeginInvoke(Close);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(SelfUpdate.DownloadPath);
            _progress.Indeterminate = true;
            SetDetail(() => "");
            return false;
        }
    }

    // ---------------------------------------------------------------- прогресс

    private bool NeedsUpdate(UpdateManifest manifest)
    {
        if (_state.UpdateInProgress)
            return true; // прошлая установка оборвалась

        if (!File.Exists(Path.Combine(InstallDir, manifest.ExecutableOrDefault)))
            return true; // игры ещё нет либо её снесли

        return !string.Equals(_state.Version, manifest.Version, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsDownload(string path, IRemoteFile file)
    {
        if (!File.Exists(path))
            return true;

        // Целый файл с нужным размером мог остаться с прошлого оборванного запуска.
        return file.Size > 0 && new FileInfo(path).Length != file.Size;
    }

    private void OnProgress(ProgressInfo p)
    {
        _progress.Indeterminate = false;
        _progress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
        SetDetail(() => ProgressDetail(p));
    }

    private static string ProgressDetail(ProgressInfo p)
    {
        var percent = p.Total > 0 ? $"{Math.Min(p.Done * 100 / p.Total, 100)}%" : "";
        var ofTotal = Loc.Of(Format.Bytes(p.Done), Format.Bytes(p.Total));

        return p.Stage switch
        {
            "download" => DownloadDetail(p, ofTotal, percent),
            "verify" => Loc.IntegrityCheck(percent),
            "install" => Loc.Unpacking(ofTotal, percent),
            "patch" => Loc.Patching(ofTotal, percent),
            _ => "",
        };
    }

    private static string DownloadDetail(ProgressInfo p, string ofTotal, string percent)
    {
        var parts = new List<string> { ofTotal, percent };

        if (p.BytesPerSecond is { } speed)
        {
            parts.Add(Format.Speed(speed));

            var eta = Format.Eta(p.Total - p.Done, speed);
            if (eta.Length > 0)
                parts.Add(Loc.Left(eta));
        }

        return string.Join("   ", parts.Where(x => x.Length > 0));
    }

    // ---------------------------------------------------------------- папка установки

    private void ChooseInstallFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Loc.FolderPickerTitle,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = InstallLocation.ExistingAncestor(InstallDir) ?? "",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        var target = InstallLocation.FromUserChoice(dialog.SelectedPath);

        if (!InstallLocation.CanWriteTo(target, out var error))
        {
            SetStatus(() => Loc.CantInstallHere, Theme.Danger);
            SetDetail(() => error);
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

        SetStatus(() => Loc.ChooseFolderAndInstall);
        SetDetail(SpaceHint);

        if (_manifest is not null)
            SetMode(ActionMode.Install);
    }

    private void UpdateLocationControls()
    {
        var canChoose = !_busy && IsFreshInstall;

        _pathCaption.Text = IsFreshInstall ? Loc.InstallFolder : Loc.InstalledIn;
        _path.Text = InstallDir;
        _path.Width = canChoose ? 396 : 492;
        _browse.Visible = canChoose;
        _shortcut.Enabled = !_busy;
    }

    private string SpaceHint()
    {
        var parts = new List<string>();

        if (_manifest is { Size: > 0 })
            parts.Add(Loc.DownloadSize(Format.Bytes(_manifest.Size)));

        if (InstallLocation.FreeSpace(InstallDir) is { } free)
            parts.Add(Loc.FreeOnDisk(Path.GetPathRoot(Path.GetFullPath(InstallDir)) ?? "", Format.Bytes(free)));

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
        var created = _shortcut.Checked;

        SetDetail(() => error is not null
            ? Loc.ShortcutError(error)
            : created ? Loc.ShortcutCreated : Loc.ShortcutRemoved);
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
        var self = SelfUpdate.SelfPath;

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
            SetStatus(() => Loc.Offline, Theme.TextMuted);
            SetDetail(() => Short(ex));
            EnablePlay();
            return;
        }

        Fail(ex, () => Loc.ManifestFailed);
    }

    private void Fail(Exception ex, Func<string>? headline = null)
    {
        _progress.Indeterminate = false;
        _progress.Value = 0;
        SetStatus(headline ?? (() => Loc.UpdateError), Theme.Danger);
        SetDetail(() => Short(ex));
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

        // На время работы кнопка гаснет, но надпись остаётся прежней.
        if (mode != ActionMode.None)
        {
            _buttonLabel = mode;
            ApplyLanguage();
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
            SetStatus(() => Loc.GameMissing, Theme.Danger);
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
            Fail(ex, () => Loc.LaunchFailed);
        }
    }

    // ---------------------------------------------------------------- мелочи

    private void SetStatus(Func<string> text, Color? color = null)
    {
        _statusText = text;
        _statusColor = color ?? Theme.Text;
        _status.Text = text();
        _status.ForeColor = _statusColor;
    }

    private void SetDetail(Func<string> text)
    {
        _detailText = text;
        _detail.Text = text();
    }

    private void UpdateVersionsLabel()
    {
        var installed = string.IsNullOrWhiteSpace(_state.Version) ? Loc.NotInstalled : _state.Version;
        _versions.Text = _manifest is null
            ? Loc.Installed(installed)
            : $"{Loc.Installed(installed)}      {Loc.Available(_manifest.Version)}";
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
            SetDetail(() => Short(ex));
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

    private static string FileNameFor(string url, string fallback)
    {
        var fromUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Path.GetFileName(uri.LocalPath) : "";
        return string.IsNullOrWhiteSpace(fromUrl) ? fallback : fromUrl;
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
        ex is HttpRequestException ? Loc.Network(ex.Message) : ex.Message;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cts.Dispose();

        base.Dispose(disposing);
    }
}
