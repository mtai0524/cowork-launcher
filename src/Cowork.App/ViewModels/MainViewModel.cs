using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;

namespace Cowork.App.ViewModels;

/// <summary>
/// View-model gốc: giữ danh sách app, điều phối tiến trình và lịch chạy,
/// đồng thời là <see cref="IAppSource"/> cho bộ lập lịch.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAppSource, IDisposable
{
    private readonly IWorkspaceStore _store;
    private readonly IProcessManager _processManager;
    private readonly IRunHistoryStore _history;
    private readonly IConfigFileService _configService;
    private readonly IConfigFileScanner _configScanner;
    private readonly IProgramScanner _programScanner;
    private readonly ICoworkLogger _logger;
    private readonly CoworkPaths _paths;
    private readonly DailyScheduler _scheduler;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _uiRefreshTimer;

    private CoworkWorkspace _workspace;
    private bool _disposed;

    public MainViewModel(
        IWorkspaceStore store,
        IProcessManager processManager,
        IRunHistoryStore history,
        IConfigFileService configService,
        ICoworkLogger logger,
        CoworkPaths paths,
        Dispatcher dispatcher)
    {
        _store = store;
        _processManager = processManager;
        _history = history;
        _configService = configService;
        _configScanner = new ConfigFileScanner(configService);
        _programScanner = new ProgramScanner();
        _logger = logger;
        _paths = paths;
        _dispatcher = dispatcher;

        _workspace = _store.Load();
        _history.Prune(_workspace.Settings.HistoryRetentionDays);

        Apps = new ObservableCollection<AppViewModel>();
        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;

        History = new ObservableCollection<AppRunRecord>(_history.All());

        foreach (var app in _workspace.Apps.OrderBy(a => a.Order))
            Apps.Add(CreateAppViewModel(app));

        SelectedApp = Apps.FirstOrDefault();

        _schedulerEnabled = _workspace.Settings.SchedulerEnabled;
        _minimizeToTray = _workspace.Settings.MinimizeToTray;
        _startWithWindows = _workspace.Settings.StartWithWindows;
        _historyRetentionDays = _workspace.Settings.HistoryRetentionDays;

        _processManager.StatusChanged += OnProcessStatusChanged;
        _processManager.OutputReceived += OnOutputReceived;
        _processManager.RunCompleted += OnRunCompleted;

        _scheduler = new DailyScheduler(this, SystemClock.Instance, _logger);
        _scheduler.AppDue += OnAppDue;

        // Làm tươi cột "chạy kế tiếp" mỗi 30 giây để đồng hồ trên dashboard không đứng yên.
        _uiRefreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background,
            (_, _) => RefreshAllRunInfo(), _dispatcher);
        _uiRefreshTimer.Start();

        if (_schedulerEnabled)
            _scheduler.Start();

        _scheduler.RunStartupApps();
    }

    public ObservableCollection<AppViewModel> Apps { get; }

    public ICollectionView AppsView { get; }

    public ObservableCollection<AppRunRecord> History { get; }

    [ObservableProperty]
    private AppViewModel? _selectedApp;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng.";

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    // ---------- Thiết lập ----------

    [ObservableProperty] private bool _schedulerEnabled;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private int _historyRetentionDays;

    partial void OnSchedulerEnabledChanged(bool value)
    {
        _workspace.Settings.SchedulerEnabled = value;
        if (value)
            _scheduler.Start();
        else
            _scheduler.Stop();

        MarkDirty();
        StatusMessage = value ? "Đã bật chạy theo lịch." : "Đã tắt chạy theo lịch.";
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _workspace.Settings.MinimizeToTray = value;
        MarkDirty();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _workspace.Settings.StartWithWindows = value;
        StartupRegistration.Apply(value, _logger);
        MarkDirty();
    }

    partial void OnHistoryRetentionDaysChanged(int value)
    {
        _workspace.Settings.HistoryRetentionDays = Math.Max(1, value);
        MarkDirty();
    }

    partial void OnSearchTextChanged(string value) => AppsView.Refresh();

    partial void OnSelectedAppChanged(AppViewModel? value)
    {
        value?.SelectedConfigFile?.Reload();
        RefreshAppCommands();
    }

    private bool FilterApp(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        if (item is not AppViewModel app)
            return false;

        return app.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || app.Group.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || app.ExecutablePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private AppViewModel CreateAppViewModel(ManagedApp model)
    {
        var viewModel = new AppViewModel(model, _configService);
        viewModel.PropertyChanged += OnAppPropertyChanged;
        viewModel.RuntimeState = _processManager.IsRunning(model.Id)
            ? AppRuntimeState.Running
            : AppRuntimeState.Idle;
        return viewModel;
    }

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Các thuộc tính chỉ phục vụ hiển thị không làm workspace bẩn.
        if (e.PropertyName is nameof(AppViewModel.RuntimeState)
            or nameof(AppViewModel.ProcessId)
            or nameof(AppViewModel.StatusText)
            or nameof(AppViewModel.NextRunText)
            or nameof(AppViewModel.LastRunText)
            or nameof(AppViewModel.LastError)
            or nameof(AppViewModel.SelectedConfigFile)
            or nameof(AppViewModel.IsRunning))
        {
            return;
        }

        MarkDirty();
    }

    private void MarkDirty()
    {
        HasUnsavedChanges = true;
        RefreshAppCommands();
    }

    // ---------- Quản lý danh sách app ----------

    [RelayCommand]
    private void AddApp()
    {
        var model = new ManagedApp
        {
            Name = "App mới " + (Apps.Count + 1),
            Order = Apps.Count,
        };

        _workspace.Apps.Add(model);

        var viewModel = CreateAppViewModel(model);
        Apps.Add(viewModel);
        SelectedApp = viewModel;

        MarkDirty();
        StatusMessage = "Đã thêm app mới. Hãy chọn file chương trình cần chạy.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void DuplicateApp()
    {
        if (SelectedApp is null)
            return;

        var copy = SelectedApp.Model.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = SelectedApp.Name + " (bản sao)";
        copy.LastRunAt = null;
        copy.LastScheduledRunAt = null;
        copy.LastExitCode = null;
        // Mỗi file cấu hình cần Id riêng để không đụng nhau giữa hai app.
        foreach (var config in copy.ConfigFiles)
            config.Id = Guid.NewGuid();

        _workspace.Apps.Add(copy);

        var viewModel = CreateAppViewModel(copy);
        Apps.Add(viewModel);
        SelectedApp = viewModel;

        MarkDirty();
        StatusMessage = $"Đã nhân bản '{SelectedApp.Name}'.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private async Task DeleteAppAsync()
    {
        if (SelectedApp is not { } app)
            return;

        var confirm = MessageBox.Show(
            $"Xoá app '{app.DisplayTitle}' khỏi Cowork?\n\nFile cấu hình và chương trình trên đĩa KHÔNG bị xoá.",
            "Xác nhận xoá", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        if (_processManager.IsRunning(app.Id))
            await _processManager.StopAsync(app.Id).ConfigureAwait(true);

        app.PropertyChanged -= OnAppPropertyChanged;
        _workspace.Apps.Remove(app.Model);
        Apps.Remove(app);
        SelectedApp = Apps.FirstOrDefault();

        Reorder();
        MarkDirty();
        StatusMessage = $"Đã xoá '{app.DisplayTitle}'.";
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(1);

    private bool CanMoveUp() => SelectedApp is not null && Apps.IndexOf(SelectedApp) > 0;

    private bool CanMoveDown() => SelectedApp is not null && Apps.IndexOf(SelectedApp) < Apps.Count - 1;

    private void Move(int delta)
    {
        if (SelectedApp is not { } app)
            return;

        var index = Apps.IndexOf(app);
        var target = index + delta;
        if (target < 0 || target >= Apps.Count)
            return;

        Apps.Move(index, target);
        SelectedApp = app;
        Reorder();
        MarkDirty();
    }

    /// <summary>Đồng bộ thứ tự trong workspace theo thứ tự hiển thị.</summary>
    private void Reorder()
    {
        _workspace.Apps.Clear();
        for (var i = 0; i < Apps.Count; i++)
        {
            Apps[i].Model.Order = i;
            _workspace.Apps.Add(Apps[i].Model);
        }
    }

    private bool HasSelectedApp() => SelectedApp is not null;

    // ---------- Chạy / dừng ----------

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void RunSelected()
    {
        if (SelectedApp is { } app)
            RunApp(app, RunTrigger.Manual);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private async Task StopSelectedAsync()
    {
        if (SelectedApp is not { } app)
            return;

        app.RuntimeState = AppRuntimeState.Stopping;
        var stopped = await _processManager.StopAsync(app.Id).ConfigureAwait(true);
        StatusMessage = stopped ? $"Đã yêu cầu dừng '{app.DisplayTitle}'." : "App không chạy.";
    }

    [RelayCommand]
    private void RunAll()
    {
        var started = 0;
        foreach (var app in Apps.Where(a => a.IsEnabled))
        {
            if (RunApp(app, RunTrigger.RunAll))
                started++;
        }

        StatusMessage = $"Đã khởi chạy {started} app.";
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        await _processManager.StopAllAsync().ConfigureAwait(true);
        StatusMessage = "Đã yêu cầu dừng tất cả app đang chạy.";
    }

    private bool RunApp(AppViewModel app, RunTrigger trigger)
    {
        var issues = app.Validate();
        if (AppValidator.HasErrors(issues))
        {
            var message = string.Join(Environment.NewLine,
                issues.Where(i => i.IsError).Select(i => "• " + i.Message));

            app.LastError = message;
            app.RuntimeState = AppRuntimeState.Failed;
            StatusMessage = $"'{app.DisplayTitle}' chưa chạy được: cấu hình còn thiếu.";
            _logger.Warning($"Bỏ qua '{app.Name}' vì cấu hình lỗi: {message}");
            return false;
        }

        app.RuntimeState = AppRuntimeState.Starting;
        app.OutputLines.Clear();

        var result = _processManager.Start(app.Model, trigger);
        if (!result.Started)
        {
            app.LastError = result.Error;
            app.RuntimeState = AppRuntimeState.Failed;
            StatusMessage = $"'{app.DisplayTitle}': {result.Error}";
            return false;
        }

        app.LastError = null;
        app.ProcessId = result.ProcessId;
        app.Model.LastRunAt = DateTimeOffset.Now;
        app.RefreshRunInfo();
        MarkDirty();
        return true;
    }

    // ---------- Sự kiện từ tầng dưới ----------

    private void OnProcessStatusChanged(object? sender, AppStatusChanged e)
        => _dispatcher.Invoke(() =>
        {
            var app = FindApp(e.AppId);
            if (app is null)
                return;

            app.RuntimeState = e.State;
            app.ProcessId = e.ProcessId;

            if (e.Message is not null)
                app.LastError = e.Message;

            app.RefreshRunInfo();
        });

    private void OnOutputReceived(object? sender, AppOutputLine line)
        => _dispatcher.BeginInvoke(() =>
        {
            var app = FindApp(line.AppId);
            if (app is null)
                return;

            app.OutputLines.Add(line);

            var limit = Math.Max(100, _workspace.Settings.OutputBufferLines);
            while (app.OutputLines.Count > limit)
                app.OutputLines.RemoveAt(0);
        });

    private void OnRunCompleted(object? sender, AppRunRecord record)
        => _dispatcher.Invoke(() =>
        {
            _history.Add(record);
            History.Insert(0, record);

            var app = FindApp(record.AppId);
            if (app is null)
                return;

            app.Model.LastExitCode = record.ExitCode;
            app.Model.LastRunAt = record.StartedAt;
            app.RefreshRunInfo();

            if (record.Outcome is RunOutcome.Failed or RunOutcome.NotStarted or RunOutcome.TimedOut)
            {
                app.LastError = record.Error ?? $"Kết thúc với mã thoát {record.ExitCode}.";
                StatusMessage = $"'{app.DisplayTitle}' kết thúc lỗi (mã {record.ExitCode}).";
            }

            MarkDirty();
        });

    private void OnAppDue(object? sender, ScheduleDueEventArgs e)
        => _dispatcher.Invoke(() =>
        {
            var app = FindApp(e.App.Id);
            if (app is null)
                return;

            RunApp(app, e.Trigger);
        });

    private AppViewModel? FindApp(Guid id) => Apps.FirstOrDefault(a => a.Id == id);

    // ---------- IAppSource ----------

    public IReadOnlyList<ManagedApp> GetApps()
        => _dispatcher.Invoke(() => Apps.Select(a => a.Model).ToList());

    public void MarkScheduled(Guid appId, DateTimeOffset at)
        => _dispatcher.Invoke(() =>
        {
            var app = FindApp(appId);
            if (app is null)
                return;

            app.Model.LastScheduledRunAt = at;
            app.RefreshSchedule();
            MarkDirty();
        });

    // ---------- Lưu trữ ----------

    [RelayCommand]
    public void SaveWorkspace()
    {
        try
        {
            Reorder();
            _store.Save(_workspace);
            HasUnsavedChanges = false;
            StatusMessage = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Không lưu được workspace.", ex);
            StatusMessage = "Không lưu được: " + ex.Message;
            MessageBox.Show("Không lưu được cấu hình:\n" + ex.Message, "Cowork",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Lưu im lặng khi thoát — không hiện hộp thoại nếu lỗi.</summary>
    public void SaveQuietly()
    {
        if (!HasUnsavedChanges)
            return;

        try
        {
            Reorder();
            _store.Save(_workspace);
            HasUnsavedChanges = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Không lưu được workspace lúc thoát.", ex);
        }
    }

    // ---------- Tiện ích ----------

    [RelayCommand]
    private void BrowseExecutable()
    {
        if (SelectedApp is not { } app)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn chương trình cần chạy",
            Filter = "Chương trình (*.exe;*.bat;*.cmd;*.ps1)|*.exe;*.bat;*.cmd;*.ps1|Tất cả (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            app.ExecutablePath = dialog.FileName;
            if (string.IsNullOrWhiteSpace(app.Name) || app.Name.StartsWith("App mới", StringComparison.Ordinal))
                app.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    [RelayCommand]
    private void BrowseWorkingDirectory()
    {
        if (SelectedApp is not { } app)
            return;

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục làm việc" };
        if (dialog.ShowDialog() == true)
            app.WorkingDirectory = dialog.FolderName;
    }

    /// <summary>
    /// Quét một thư mục để tìm chương trình cần chạy, rồi điền vào ô lệnh chạy của app.
    /// Đỡ phải tự mò đường dẫn khi chỉ nhớ mang máng app nằm ở thư mục nào.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void ScanProgramFolder()
    {
        if (SelectedApp is not { } app)
            return;

        var startDirectory = ResolveScanStartDirectory(app);
        if (startDirectory is null)
            return;

        var scanViewModel = new ScanProgramViewModel(_programScanner, startDirectory);
        var window = new Views.ScanProgramWindow(scanViewModel) { Owner = Application.Current.MainWindow };

        if (window.ShowDialog() != true || scanViewModel.SelectedCandidate is not { } chosen)
            return;

        var (executable, arguments) = ProgramScanner.BuildCommand(chosen.Candidate);
        app.ExecutablePath = executable;

        // Chỉ ghi đè tham số khi thật sự có gì để ghi (trường hợp .ps1), để không
        // xoá mất tham số người dùng đã gõ trước đó.
        if (arguments.Length > 0)
            app.Arguments = arguments;

        // Thư mục làm việc mặc định nên là nơi chứa script, không phải thư mục quét.
        if (string.IsNullOrWhiteSpace(app.WorkingDirectory))
        {
            var scriptDirectory = Path.GetDirectoryName(chosen.Candidate.FullPath);
            if (!string.IsNullOrEmpty(scriptDirectory))
                app.WorkingDirectory = scriptDirectory;
        }

        if (string.IsNullOrWhiteSpace(app.Name) || app.Name.StartsWith("App mới", StringComparison.Ordinal))
            app.Name = Path.GetFileNameWithoutExtension(chosen.Candidate.FullPath);

        MarkDirty();
        StatusMessage = $"Đã chọn '{Path.GetFileName(chosen.Candidate.FullPath)}' làm lệnh chạy.";
    }

    /// <summary>
    /// Thư mục bắt đầu quét: ưu tiên thư mục làm việc, rồi tới thư mục chứa chương trình
    /// hiện tại; nếu chưa có gì thì hỏi người dùng.
    /// </summary>
    private string? ResolveScanStartDirectory(AppViewModel app)
    {
        var candidates = new[]
        {
            app.Model.WorkingDirectory,
            string.IsNullOrWhiteSpace(app.Model.ExecutablePath)
                ? null
                : Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(app.Model.ExecutablePath)),
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (Directory.Exists(expanded))
                return expanded;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục cần quét" };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>
    /// Quét một thư mục để tìm file cấu hình rồi cho người dùng tick chọn.
    /// Kết quả quét chỉ là gợi ý — không tự thêm file nào khi chưa được xác nhận.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void ScanConfigFolder()
    {
        if (SelectedApp is not { } app)
            return;

        var startDirectory = app.Model.ResolveWorkingDirectory();
        if (!Directory.Exists(startDirectory))
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Chọn thư mục cần quét" };
            if (dialog.ShowDialog() != true)
                return;
            startDirectory = dialog.FolderName;
        }

        var alreadyAdded = app.ConfigFiles
            .Select(c => c.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path));

        var scanViewModel = new ScanConfigViewModel(_configScanner, startDirectory, alreadyAdded);
        var window = new Views.ScanConfigWindow(scanViewModel) { Owner = Application.Current.MainWindow };

        if (window.ShowDialog() != true)
            return;

        var added = AddScannedFiles(app, scanViewModel.SelectedCandidates);
        StatusMessage = added > 0
            ? $"Đã thêm {added} file cấu hình vào '{app.DisplayTitle}'."
            : "Các file đã chọn đều có sẵn trong danh sách.";
    }

    /// <summary>
    /// Thêm các file quét được vào app, bỏ qua file trùng và rút gọn thành đường dẫn
    /// tương đối khi file nằm trong thư mục làm việc.
    /// </summary>
    private int AddScannedFiles(AppViewModel app, IReadOnlyList<ConfigCandidate> candidates)
    {
        var workingDirectory = app.Model.ResolveWorkingDirectory();
        var existing = new HashSet<string>(
            app.ConfigFiles.Select(c => c.FullPath), StringComparer.OrdinalIgnoreCase);

        var added = 0;
        ConfigFileViewModel? lastAdded = null;

        foreach (var candidate in candidates)
        {
            if (!existing.Add(candidate.FullPath))
                continue;

            var reference = new ConfigFileRef
            {
                DisplayName = Path.GetFileName(candidate.FullPath),
                Path = MakeRelativeIfInside(candidate.FullPath, workingDirectory),
                Format = candidate.Format,
            };

            app.Model.ConfigFiles.Add(reference);
            lastAdded = app.AttachConfigFile(reference);
            added++;
        }

        if (lastAdded is not null)
        {
            app.SelectedConfigFile = lastAdded;
            MarkDirty();
        }

        return added;
    }

    /// <summary>
    /// File nằm trong thư mục làm việc thì lưu đường dẫn tương đối — nhờ vậy nhân bản app
    /// sang thư mục khác chỉ cần đổi mỗi thư mục làm việc.
    /// </summary>
    private static string MakeRelativeIfInside(string fullPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return fullPath;

        var relative = Path.GetRelativePath(workingDirectory, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath
            : relative;
    }

    [RelayCommand]
    private void BrowseConfigFile()
    {
        if (SelectedApp?.SelectedConfigFile is not { } config)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file cấu hình",
            Filter = "Cấu hình (*.json;*.ini;*.xml;*.config;*.env;*.conf)|*.json;*.ini;*.xml;*.config;*.env;*.conf"
                     + "|Tất cả (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        config.Model.Path = dialog.FileName;
        if (string.IsNullOrWhiteSpace(config.Model.DisplayName))
            config.Model.DisplayName = Path.GetFileName(dialog.FileName);

        config.NotifyPathChanged();
        config.Reload();
        MarkDirty();
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenInExplorer(_paths.Root);

    [RelayCommand]
    private void OpenLogFolder() => OpenInExplorer(_paths.LogDirectory);

    [RelayCommand(CanExecute = nameof(HasSelectedApp))]
    private void OpenAppFolder()
    {
        if (SelectedApp is { } app)
            OpenInExplorer(app.Model.ResolveWorkingDirectory());
    }

    [RelayCommand]
    private void OpenConfigFolder()
    {
        if (SelectedApp?.SelectedConfigFile is { } config && !string.IsNullOrWhiteSpace(config.FullPath))
            RevealInExplorer(config.FullPath);
    }

    private void OpenInExplorer(string path)
    {
        if (!Directory.Exists(path))
        {
            StatusMessage = "Thư mục không tồn tại: " + path;
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void RevealInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            StatusMessage = "File không tồn tại: " + filePath;
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CheckScheduleNow()
    {
        _scheduler.Tick();
        RefreshAllRunInfo();
        StatusMessage = "Đã kiểm tra lịch một lượt.";
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _history.Clear();
        History.Clear();
        StatusMessage = "Đã xoá lịch sử chạy.";
    }

    private void RefreshAllRunInfo()
    {
        foreach (var app in Apps)
            app.RefreshRunInfo();
    }

    private void RefreshAppCommands()
    {
        DuplicateAppCommand.NotifyCanExecuteChanged();
        DeleteAppCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        RunSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        OpenAppFolderCommand.NotifyCanExecuteChanged();
        ScanConfigFolderCommand.NotifyCanExecuteChanged();
        ScanProgramFolderCommand.NotifyCanExecuteChanged();
    }

    public bool AnyRunning => _processManager.RunningAppIds.Count > 0;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _uiRefreshTimer.Stop();
        _scheduler.AppDue -= OnAppDue;
        _scheduler.Dispose();

        _processManager.StatusChanged -= OnProcessStatusChanged;
        _processManager.OutputReceived -= OnOutputReceived;
        _processManager.RunCompleted -= OnRunCompleted;
    }
}
