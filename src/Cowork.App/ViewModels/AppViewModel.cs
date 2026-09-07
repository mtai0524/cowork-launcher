using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Cowork.Core.Localization;
using Cowork.Remote;
using Cowork.Remote.Contracts;

namespace Cowork.App.ViewModels;

/// <summary>
/// Bọc một <see cref="ManagedApp"/> cho tầng giao diện. Mọi thay đổi được ghi thẳng
/// vào model, nên chỉ cần lưu workspace một lần là đủ.
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    private readonly IConfigFileService _configService;
    private bool _suspendSync;

    public AppViewModel(ManagedApp model, IConfigFileService configService)
    {
        Model = model;
        _configService = configService;

        _suspendSync = true;

        _name = model.Name;
        _description = model.Description;
        _group = model.Group;
        _executablePath = model.ExecutablePath;
        _arguments = model.Arguments;
        _workingDirectory = model.WorkingDirectory;
        _isEnabled = model.Enabled;
        _runAsAdministrator = model.RunAsAdministrator;
        _captureOutput = model.CaptureOutput;
        _singleInstance = model.SingleInstance;
        _windowStyle = model.WindowStyle;
        _timeoutMinutes = model.TimeoutMinutes;
        _keepAlive = model.KeepAlive;
        _restartDelaySeconds = model.RestartDelaySeconds;
        _maxRestartsPerHour = model.MaxRestartsPerHour;
        _retryCount = model.RetryCount;
        _retryDelaySeconds = model.RetryDelaySeconds;
        _successExitCodesText = ExitCodes.Format(model.SuccessExitCodes);
        _stopGraceSeconds = model.StopGraceSeconds;

        var health = model.HealthCheck;
        _healthTarget = health.Target;
        _healthIntervalSeconds = health.IntervalSeconds;
        _healthTimeoutSeconds = health.TimeoutSeconds;
        _healthFailureThreshold = health.FailureThreshold;
        _healthStartupGraceSeconds = health.StartupGraceSeconds;
        _healthSilenceMinutes = health.SilenceMinutes;
        _healthFailurePatternsText = string.Join(Environment.NewLine, health.FailurePatterns);

        _runOnResume = model.SystemTriggers.Contains(SystemEventKind.Resume);
        _runOnSessionUnlock = model.SystemTriggers.Contains(SystemEventKind.SessionUnlock);
        _runOnNetworkAvailable = model.SystemTriggers.Contains(SystemEventKind.NetworkAvailable);
        _systemTriggerDelaySeconds = model.SystemTriggerDelaySeconds;

        _scheduleEnabled = model.Schedule.Enabled;
        _scheduleKind = model.Schedule.Kind;
        _timesText = FormatTimes(model.Schedule.Times);
        _intervalMinutes = (int)Math.Max(1, model.Schedule.Interval.TotalMinutes);
        _windowStart = FormatTime(model.Schedule.WindowStart);
        _windowEnd = FormatTime(model.Schedule.WindowEnd);
        _catchUpMissedRun = model.Schedule.CatchUpMissedRun;

        Days = new ObservableCollection<DayToggleViewModel>(
            new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
            }.Select(d => new DayToggleViewModel(d, model.Schedule.DaysOfWeek.Contains(d))));

        foreach (var day in Days)
            day.PropertyChanged += OnDayToggled;

        EnvironmentVariables = new ObservableCollection<EnvironmentVariableViewModel>(
            model.EnvironmentVariables.Select(p => new EnvironmentVariableViewModel(p.Key, p.Value)));
        EnvironmentVariables.CollectionChanged += OnEnvCollectionChanged;
        foreach (var variable in EnvironmentVariables)
            variable.PropertyChanged += OnEnvItemChanged;

        ConfigFiles = new ObservableCollection<ConfigFileViewModel>(
            model.ConfigFiles.Select(CreateConfigViewModel));
        SelectedConfigFile = ConfigFiles.FirstOrDefault();

        OutputLines = new ObservableCollection<AppOutputLine>();

        WindowStyleOptions = new[]
        {
            AppWindowStyle.Normal, AppWindowStyle.Minimized, AppWindowStyle.Hidden,
        }.Select(style => new ChoiceViewModel<AppWindowStyle>(style, s => Loc.T("WindowStyle." + s))).ToList();
        _selectedWindowStyle = WindowStyleOptions.First(o => o.Value == model.WindowStyle);

        HealthProbeOptions = new[]
        {
            HealthProbeKind.None, HealthProbeKind.TcpPort, HealthProbeKind.HttpGet,
        }.Select(kind => new ChoiceViewModel<HealthProbeKind>(kind, k => Loc.T("HealthProbe." + k))).ToList();
        _selectedHealthProbe = HealthProbeOptions.First(o => o.Value == health.Probe);

        _suspendSync = false;
    }

    public ManagedApp Model { get; }

    public Guid Id => Model.Id;

    // ---------- Thông tin chung ----------

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _group;
    [ObservableProperty] private string _executablePath;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _workingDirectory;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _runAsAdministrator;
    [ObservableProperty] private bool _captureOutput;
    [ObservableProperty] private bool _singleInstance;
    [ObservableProperty] private AppWindowStyle _windowStyle;
    [ObservableProperty] private int _timeoutMinutes;
    [ObservableProperty] private bool _keepAlive;
    [ObservableProperty] private int _restartDelaySeconds;
    [ObservableProperty] private int _maxRestartsPerHour;
    [ObservableProperty] private int _retryCount;
    [ObservableProperty] private int _retryDelaySeconds;
    [ObservableProperty] private string _successExitCodesText;
    [ObservableProperty] private int _stopGraceSeconds;

    partial void OnNameChanged(string value)
    {
        Sync(() => Model.Name = value);
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnDescriptionChanged(string value) => Sync(() => Model.Description = value);

    partial void OnGroupChanged(string value)
    {
        Sync(() => Model.Group = value);
        OnPropertyChanged(nameof(GroupLabel));
    }

    partial void OnExecutablePathChanged(string value)
    {
        Sync(() => Model.ExecutablePath = value);
        RefreshConfigPaths();
    }

    partial void OnArgumentsChanged(string value) => Sync(() => Model.Arguments = value);

    partial void OnWorkingDirectoryChanged(string value)
    {
        Sync(() => Model.WorkingDirectory = value);
        RefreshConfigPaths();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        Sync(() => Model.Enabled = value);
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnRunAsAdministratorChanged(bool value) => Sync(() => Model.RunAsAdministrator = value);
    partial void OnCaptureOutputChanged(bool value) => Sync(() => Model.CaptureOutput = value);
    partial void OnSingleInstanceChanged(bool value) => Sync(() => Model.SingleInstance = value);
    partial void OnWindowStyleChanged(AppWindowStyle value) => Sync(() => Model.WindowStyle = value);
    partial void OnTimeoutMinutesChanged(int value) => Sync(() => Model.TimeoutMinutes = Math.Max(0, value));
    partial void OnKeepAliveChanged(bool value)
    {
        Sync(() => Model.KeepAlive = value);
        OnPropertyChanged(nameof(RetryAvailable));
    }

    partial void OnRestartDelaySecondsChanged(int value) => Sync(() => Model.RestartDelaySeconds = Math.Max(0, value));
    partial void OnMaxRestartsPerHourChanged(int value) => Sync(() => Model.MaxRestartsPerHour = Math.Max(0, value));
    partial void OnRetryCountChanged(int value) => Sync(() => Model.RetryCount = Math.Max(0, value));
    partial void OnRetryDelaySecondsChanged(int value) => Sync(() => Model.RetryDelaySeconds = Math.Max(0, value));
    partial void OnSuccessExitCodesTextChanged(string value)
    {
        var codes = ExitCodes.Parse(value);
        Sync(() => Model.SuccessExitCodes = codes);

        // Viết lại ô nhập theo dạng chuẩn ("0, 1") để người dùng thấy ngay phần gõ sai đã bị bỏ.
        // Ô này cập nhật khi rời focus nên không giật chữ trong lúc gõ.
        var normalized = ExitCodes.Format(codes);
        if (normalized != value)
            SuccessExitCodesText = normalized;
    }
    partial void OnStopGraceSecondsChanged(int value) => Sync(() => Model.StopGraceSeconds = Math.Max(0, value));

    /// <summary>Keep-alive đã tự khởi động lại app, nên các ô thử lại chỉ mở khi keep-alive tắt.</summary>
    public bool RetryAvailable => !KeepAlive;

    // ---------- Kiểm tra sức khoẻ ----------

    /// <summary>Nguồn cho ComboBox chọn kiểu thăm dò.</summary>
    public IReadOnlyList<ChoiceViewModel<HealthProbeKind>> HealthProbeOptions { get; }

    [ObservableProperty] private ChoiceViewModel<HealthProbeKind>? _selectedHealthProbe;
    [ObservableProperty] private string _healthTarget;
    [ObservableProperty] private int _healthIntervalSeconds;
    [ObservableProperty] private int _healthTimeoutSeconds;
    [ObservableProperty] private int _healthFailureThreshold;
    [ObservableProperty] private int _healthStartupGraceSeconds;
    [ObservableProperty] private int _healthSilenceMinutes;
    [ObservableProperty] private string _healthFailurePatternsText;

    /// <summary>Kiểu thăm dò đang đặt; bắn PropertyChanged cho tên này khi người dùng đổi thật.</summary>
    public HealthProbeKind HealthProbe => Model.HealthCheck.Probe;

    /// <summary>Các ô mục tiêu / thời gian chờ chỉ có nghĩa khi có thăm dò.</summary>
    public bool HasHealthProbe => Model.HealthCheck.HasProbe;

    partial void OnSelectedHealthProbeChanged(ChoiceViewModel<HealthProbeKind>? value)
    {
        // ComboBox có lúc đẩy null trong quá trình dựng lại danh sách; bỏ qua để không mất lựa chọn.
        if (value is null || value.Value == Model.HealthCheck.Probe)
            return;

        Sync(() => Model.HealthCheck.Probe = value.Value);
        OnPropertyChanged(nameof(HealthProbe));
        OnPropertyChanged(nameof(HasHealthProbe));
    }

    partial void OnHealthTargetChanged(string value) => Sync(() => Model.HealthCheck.Target = (value ?? string.Empty).Trim());
    partial void OnHealthIntervalSecondsChanged(int value) => Sync(() => Model.HealthCheck.IntervalSeconds = Math.Max(1, value));
    partial void OnHealthTimeoutSecondsChanged(int value) => Sync(() => Model.HealthCheck.TimeoutSeconds = Math.Max(1, value));
    partial void OnHealthFailureThresholdChanged(int value) => Sync(() => Model.HealthCheck.FailureThreshold = Math.Max(1, value));
    partial void OnHealthStartupGraceSecondsChanged(int value) => Sync(() => Model.HealthCheck.StartupGraceSeconds = Math.Max(0, value));
    partial void OnHealthSilenceMinutesChanged(int value) => Sync(() => Model.HealthCheck.SilenceMinutes = Math.Max(0, value));

    partial void OnHealthFailurePatternsTextChanged(string value)
        => Sync(() => Model.HealthCheck.FailurePatterns = ParsePatterns(value));

    /// <summary>
    /// Mỗi dòng một mẫu, bỏ dòng trắng. Luôn gán danh sách <em>mới</em> thay vì sửa tại chỗ: bộ theo dõi
    /// sức khoẻ đọc danh sách này từ luồng khác và chỉ chụp lại ở nhịp kế tiếp.
    /// </summary>
    private static List<string> ParsePatterns(string? text)
        => (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    // ---------- Phụ thuộc ----------

    public ObservableCollection<DependencyOptionViewModel> DependencyOptions { get; } = new();

    public bool HasDependencyOptions => DependencyOptions.Count > 0;

    /// <summary>Cảnh báo về đồ thị phụ thuộc (vòng lặp, trỏ tới app đã xoá…). Do MainViewModel tính.</summary>
    [ObservableProperty]
    private string? _dependencyWarning;

    /// <summary>
    /// Dựng lại bảng phụ thuộc từ danh sách app hiện có. Gọi mỗi khi người dùng chọn app khác hoặc
    /// danh sách app thay đổi — bảng này nói về *những app khác*, nên không tự cập nhật được.
    /// </summary>
    public void RefreshDependencyOptions(IEnumerable<ManagedApp> others)
    {
        ArgumentNullException.ThrowIfNull(others);

        var existing = Model.DependsOn.ToDictionary(d => d.AppId, d => d);

        DependencyOptions.Clear();
        foreach (var target in others.Where(a => a.Id != Model.Id))
            DependencyOptions.Add(new DependencyOptionViewModel(target, existing.GetValueOrDefault(target.Id), SyncDependencies));

        OnPropertyChanged(nameof(HasDependencyOptions));
    }

    /// <summary>
    /// Ghi lại danh sách phụ thuộc theo các ô đã tick. Bảng chứa đủ mọi app khác nên nó là nguồn
    /// sự thật đầy đủ — kể cả việc dọn những khai báo trỏ tới app đã bị xoá.
    /// </summary>
    private void SyncDependencies() => Sync(() =>
    {
        Model.DependsOn = DependencyOptions
            .Where(o => o.IsSelected)
            .Select(o => new AppDependency { AppId = o.TargetId, Wait = o.Wait })
            .ToList();

        OnPropertyChanged(nameof(DependencyOptions));
    });

    /// <summary>Nguồn cho ComboBox chọn kiểu cửa sổ.</summary>
    public IReadOnlyList<ChoiceViewModel<AppWindowStyle>> WindowStyleOptions { get; }

    [ObservableProperty]
    private ChoiceViewModel<AppWindowStyle>? _selectedWindowStyle;

    partial void OnSelectedWindowStyleChanged(ChoiceViewModel<AppWindowStyle>? value)
    {
        // ComboBox có lúc đẩy null trong quá trình dựng lại danh sách; bỏ qua để
        // không ghi đè lựa chọn đang có bằng giá trị mặc định.
        if (value is not null)
            WindowStyle = value.Value;
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Name) ? Loc.T("App.Untitled") : Name;

    public string GroupLabel => string.IsNullOrWhiteSpace(Group) ? Loc.T("App.Ungrouped") : Group;

    // ---------- Lịch chạy ----------

    [ObservableProperty] private bool _scheduleEnabled;
    [ObservableProperty] private ScheduleKind _scheduleKind;
    [ObservableProperty] private string _timesText;
    [ObservableProperty] private int _intervalMinutes;
    [ObservableProperty] private string _windowStart;
    [ObservableProperty] private string _windowEnd;
    [ObservableProperty] private bool _catchUpMissedRun;

    public ObservableCollection<DayToggleViewModel> Days { get; }

    // ---------- Chạy theo sự kiện hệ thống ----------

    [ObservableProperty] private bool _runOnResume;
    [ObservableProperty] private bool _runOnSessionUnlock;
    [ObservableProperty] private bool _runOnNetworkAvailable;
    [ObservableProperty] private int _systemTriggerDelaySeconds;

    partial void OnRunOnResumeChanged(bool value) => SyncSystemTriggers();
    partial void OnRunOnSessionUnlockChanged(bool value) => SyncSystemTriggers();
    partial void OnRunOnNetworkAvailableChanged(bool value) => SyncSystemTriggers();

    partial void OnSystemTriggerDelaySecondsChanged(int value)
        => Sync(() => Model.SystemTriggerDelaySeconds = Math.Max(0, value));

    /// <summary>
    /// Gán danh sách <em>mới</em> thay vì sửa tại chỗ: supervisor đọc nó từ luồng sự kiện của Windows.
    /// </summary>
    private void SyncSystemTriggers() => Sync(() =>
    {
        var triggers = new List<SystemEventKind>();
        if (RunOnResume)
            triggers.Add(SystemEventKind.Resume);
        if (RunOnSessionUnlock)
            triggers.Add(SystemEventKind.SessionUnlock);
        if (RunOnNetworkAvailable)
            triggers.Add(SystemEventKind.NetworkAvailable);

        Model.SystemTriggers = triggers;
    });

    public bool IsDailyKind => ScheduleKind == ScheduleKind.DailyAtTimes;
    public bool IsIntervalKind => ScheduleKind == ScheduleKind.Interval;

    partial void OnScheduleEnabledChanged(bool value)
    {
        Sync(() => Model.Schedule.Enabled = value);
        RefreshSchedule();
    }

    partial void OnScheduleKindChanged(ScheduleKind value)
    {
        Sync(() => Model.Schedule.Kind = value);
        OnPropertyChanged(nameof(IsDailyKind));
        OnPropertyChanged(nameof(IsIntervalKind));
        RefreshSchedule();
    }

    partial void OnTimesTextChanged(string value)
    {
        Sync(() => Model.Schedule.Times = ParseTimes(value));
        RefreshSchedule();
    }

    partial void OnIntervalMinutesChanged(int value)
    {
        Sync(() => Model.Schedule.Interval = TimeSpan.FromMinutes(Math.Max(1, value)));
        RefreshSchedule();
    }

    partial void OnWindowStartChanged(string value)
    {
        Sync(() => Model.Schedule.WindowStart = ParseTime(value));
        RefreshSchedule();
    }

    partial void OnWindowEndChanged(string value)
    {
        Sync(() => Model.Schedule.WindowEnd = ParseTime(value));
        RefreshSchedule();
    }

    partial void OnCatchUpMissedRunChanged(bool value) => Sync(() => Model.Schedule.CatchUpMissedRun = value);

    private void OnDayToggled(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DayToggleViewModel.IsSelected))
            return;

        Sync(() => Model.Schedule.DaysOfWeek = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList());
        RefreshSchedule();
    }

    /// <summary>Chấp nhận "07:30, 13:00" hoặc "7:30 13:00"; bỏ qua phần nhập sai.</summary>
    private static List<TimeSpan> ParseTimes(string text)
    {
        var result = new List<TimeSpan>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var token in text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TimeSpan.TryParse(token.Trim(), CultureInfo.InvariantCulture, out var value)
                && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1))
            {
                result.Add(value);
            }
        }

        return result.Distinct().OrderBy(t => t).ToList();
    }

    private static TimeSpan? ParseTime(string text)
        => TimeSpan.TryParse((text ?? string.Empty).Trim(), CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string FormatTimes(IEnumerable<TimeSpan> times)
        => string.Join(", ", times.OrderBy(t => t).Select(t => t.ToString(@"hh\:mm")));

    private static string FormatTime(TimeSpan? value)
        => value?.ToString(@"hh\:mm") ?? string.Empty;

    // ---------- Biến môi trường ----------

    public ObservableCollection<EnvironmentVariableViewModel> EnvironmentVariables { get; }

    [RelayCommand]
    private void AddEnvironmentVariable()
        => EnvironmentVariables.Add(new EnvironmentVariableViewModel(Loc.T("Env.DefaultName"), string.Empty));

    [RelayCommand]
    private void RemoveEnvironmentVariable(EnvironmentVariableViewModel? item)
    {
        if (item is not null)
            EnvironmentVariables.Remove(item);
    }

    private void OnEnvCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.NewItems?.OfType<EnvironmentVariableViewModel>() ?? Enumerable.Empty<EnvironmentVariableViewModel>())
            item.PropertyChanged += OnEnvItemChanged;

        foreach (var item in e.OldItems?.OfType<EnvironmentVariableViewModel>() ?? Enumerable.Empty<EnvironmentVariableViewModel>())
            item.PropertyChanged -= OnEnvItemChanged;

        SyncEnvironment();
    }

    private void OnEnvItemChanged(object? sender, PropertyChangedEventArgs e) => SyncEnvironment();

    private void SyncEnvironment() => Sync(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EnvironmentVariables.Where(v => v.IsValid))
            map[item.Name.Trim()] = item.Value ?? string.Empty;

        Model.EnvironmentVariables = map;
    });

    // ---------- File cấu hình ----------

    public ObservableCollection<ConfigFileViewModel> ConfigFiles { get; }

    [ObservableProperty]
    private ConfigFileViewModel? _selectedConfigFile;

    partial void OnSelectedConfigFileChanged(ConfigFileViewModel? value) => value?.Reload();

    private ConfigFileViewModel CreateConfigViewModel(ConfigFileRef reference)
        => new(reference, _configService, Model.ResolveWorkingDirectory);

    [RelayCommand]
    private void AddConfigFile()
    {
        var reference = new ConfigFileRef { Path = string.Empty, Format = ConfigFormat.Auto };
        Model.ConfigFiles.Add(reference);
        SelectedConfigFile = AttachConfigFile(reference);
    }

    /// <summary>
    /// Bọc một <see cref="ConfigFileRef"/> đã có trong model thành view-model và đưa lên danh sách.
    /// Dùng khi thêm file bằng tay lẫn khi thêm hàng loạt từ kết quả quét thư mục.
    /// </summary>
    public ConfigFileViewModel AttachConfigFile(ConfigFileRef reference)
    {
        var viewModel = CreateConfigViewModel(reference);
        ConfigFiles.Add(viewModel);
        return viewModel;
    }

    [RelayCommand]
    private void RemoveConfigFile(ConfigFileViewModel? item)
    {
        if (item is null)
            return;

        Model.ConfigFiles.Remove(item.Model);
        ConfigFiles.Remove(item);
        SelectedConfigFile = ConfigFiles.FirstOrDefault();
    }

    /// <summary>Đường dẫn tương đối phụ thuộc thư mục làm việc — nạp lại khi thư mục đổi.</summary>
    private void RefreshConfigPaths()
    {
        foreach (var config in ConfigFiles)
            config.NotifyPathChanged();
    }

    // ---------- Trạng thái lúc chạy ----------

    [ObservableProperty]
    private AppRuntimeState _runtimeState = AppRuntimeState.Idle;

    [ObservableProperty]
    private int _processId;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>Lần tự khởi động lại đang chờ (0 = không chờ) và số giây chờ — chỉ để hiển thị.</summary>
    [ObservableProperty]
    private int _pendingRestartAttempt;

    [ObservableProperty]
    private int _pendingRestartSeconds;

    /// <summary>Lần chờ hiện tại là thử lại job lỗi (đếm theo lượt, có trần) chứ không phải keep-alive.</summary>
    [ObservableProperty]
    private bool _pendingIsRetry;

    [ObservableProperty]
    private int _pendingRetryLimit;

    partial void OnPendingRestartAttemptChanged(int value) => OnPropertyChanged(nameof(StatusText));

    partial void OnPendingIsRetryChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    public ObservableCollection<AppOutputLine> OutputLines { get; }

    partial void OnRuntimeStateChanged(AppRuntimeState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsRunning));
    }

    public bool IsRunning => RuntimeState is AppRuntimeState.Running or AppRuntimeState.Starting;

    public string StatusText
    {
        get
        {
            if (!IsEnabled)
                return Loc.T("State.Disabled");

            return RuntimeState switch
            {
                AppRuntimeState.Starting => Loc.T("State.Starting"),
                AppRuntimeState.Running => Loc.T("State.Running", ProcessId),
                AppRuntimeState.Stopping => Loc.T("State.Stopping"),
                AppRuntimeState.Failed => Loc.T("State.Failed"),
                AppRuntimeState.WaitingRestart when PendingIsRetry
                    => Loc.T("State.WaitingRetry", PendingRestartSeconds, PendingRestartAttempt, PendingRetryLimit),
                AppRuntimeState.WaitingRestart => Loc.T("State.WaitingRestart", PendingRestartSeconds, PendingRestartAttempt),
                _ => Model.LastRunAt is { } last
                    ? Loc.T("State.LastRunAt", last.ToString("dd/MM HH:mm"))
                    : Loc.T("State.NeverRun"),
            };
        }
    }

    public string ScheduleSummary => Model.Schedule.Describe();

    public string NextRunText
    {
        get
        {
            var next = ScheduleEvaluator.NextRun(Model, DateTimeOffset.Now);
            if (next is null)
            {
                return Model.Schedule.Kind == ScheduleKind.OnCoworkStartup
                    ? Loc.T("Sched.OnStartup")
                    : Loc.T("Common.Dash");
            }

            var day = next.Value.Date == DateTime.Today ? Loc.T("State.Today") : next.Value.ToString("dd/MM");
            return $"{day} {next.Value:HH:mm}";
        }
    }

    public string LastRunText => Model.LastRunAt is { } last
        ? $"{last:dd/MM/yyyy HH:mm:ss}"
          + (Model.LastExitCode is { } code ? Loc.T("State.ExitCodeSuffix", code) : string.Empty)
        : Loc.T("State.NeverRunShort");

    /// <summary>Cập nhật các ô phụ thuộc lịch sau khi người dùng đổi cấu hình.</summary>
    public void RefreshSchedule()
    {
        OnPropertyChanged(nameof(ScheduleSummary));
        OnPropertyChanged(nameof(NextRunText));
    }

    public void RefreshRunInfo()
    {
        OnPropertyChanged(nameof(LastRunText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(NextRunText));
    }

    public IReadOnlyList<ValidationIssue> Validate() => AppValidator.Validate(Model);

    /// <summary>Ảnh chụp gửi lên hub quản lý từ xa.</summary>
    public AppSnapshot ToSnapshot(DateTimeOffset now)
        => SnapshotBuilder.Build(Model, RuntimeState, ProcessId, LastError, now);

    /// <summary>
    /// Nạp lại mọi nhãn sau khi đổi ngôn ngữ. Bắn PropertyChanged với tên rỗng là
    /// cách WPF hiểu "mọi thuộc tính đã đổi", rẻ hơn liệt kê tay và không sót chỗ nào.
    /// </summary>
    public void RefreshLocalizedText()
    {
        foreach (var day in Days)
            day.RefreshLabel();

        foreach (var option in WindowStyleOptions)
            option.RefreshLabel();

        foreach (var option in HealthProbeOptions)
            option.RefreshLabel();

        foreach (var option in DependencyOptions)
            option.RefreshLocalizedText();

        foreach (var config in ConfigFiles)
            config.RefreshLocalizedText();

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Chặn ghi ngược vào model trong lúc khởi tạo view-model.</summary>
    private void Sync(Action action)
    {
        if (_suspendSync)
            return;
        action();
    }
}
