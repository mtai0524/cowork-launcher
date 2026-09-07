using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record HealthFailedEventArgs(ManagedApp App, HealthFailure Failure, string Reason);

/// <summary>
/// Theo dõi sức khoẻ của các app đang chạy: thăm dò cổng/URL theo chu kỳ, xét output im lặng, và
/// soi từng dòng output tìm mẫu báo lỗi. Phát hiện treo thì <em>tự dừng</em> app qua
/// <see cref="IProcessManager.TerminateAsync"/> với kết quả <see cref="RunOutcome.Unhealthy"/>.
///
/// Khác <see cref="KeepAliveSupervisor"/>, lớp này dừng tiến trình trực tiếp chứ không nhờ tầng
/// ứng dụng: dừng không cần kiểm tra cấu hình hay ghi lịch sử riêng — <see cref="ProcessManager"/>
/// ghi lý do vào đúng bản ghi của lần chạy đó. Phần khởi động lại sau đó vẫn đi qua keep-alive /
/// thử lại như mọi lần kết thúc khác.
/// </summary>
public sealed class HealthMonitor : IDisposable
{
    private readonly IProcessManager _processManager;
    private readonly IAppSource _source;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly IHealthProbe _probe;
    private readonly TimeSpan _minimumInterval;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Watch> _watches = new();
    private bool _disposed;

    /// <param name="minimumInterval">Sàn cho chu kỳ kiểm tra; mặc định <see cref="HealthPolicy.MinimumInterval"/>. Test hạ xuống để khỏi chờ.</param>
    public HealthMonitor(
        IProcessManager processManager,
        IAppSource source,
        IClock clock,
        ICoworkLogger logger,
        IHealthProbe probe,
        TimeSpan? minimumInterval = null)
    {
        _processManager = processManager;
        _source = source;
        _clock = clock;
        _logger = logger;
        _probe = probe;
        _minimumInterval = minimumInterval ?? HealthPolicy.MinimumInterval;

        _processManager.StatusChanged += OnStatusChanged;
        _processManager.OutputReceived += OnOutputReceived;
        _processManager.RunCompleted += OnRunCompleted;
    }

    /// <summary>App bị coi là treo và đang được dừng. Bản ghi lịch sử tới sau qua <see cref="IProcessManager.RunCompleted"/>.</summary>
    public event EventHandler<HealthFailedEventArgs>? Unhealthy;

    public bool IsWatching(Guid appId)
    {
        lock (_gate)
            return _watches.ContainsKey(appId);
    }

    private void OnStatusChanged(object? sender, AppStatusChanged e)
    {
        if (_disposed || e.State != AppRuntimeState.Running)
            return;

        try
        {
            var app = FindApp(e.AppId);
            if (app is null || !HealthPolicy.AppliesTo(app))
                return;

            StartWatching(app, e.ProcessId);
        }
        catch (Exception ex)
        {
            // Callback từ tầng tiến trình; lỗi lọt ra ngoài sẽ làm sập cả Cowork.
            _logger.Error("Lỗi khi bắt đầu theo dõi sức khoẻ app.", ex);
        }
    }

    private void StartWatching(ManagedApp app, int processId)
    {
        var watch = new Watch(app.Id, processId, _clock.Now, Snapshot.Of(app, previous: null));
        var interval = HealthPolicy.CheckInterval(watch.Snapshot.Check, _minimumInterval);

        lock (_gate)
        {
            if (_disposed)
                return;

            if (_watches.Remove(app.Id, out var previous))
                previous.Dispose();

            _watches[app.Id] = watch;
            watch.Interval = interval;
            watch.Timer = new Timer(_ => _ = TickAsync(watch), null, interval, interval);
        }

        _logger.Info($"Theo dõi sức khoẻ '{app.Name}' (PID {processId}, mỗi {interval.TotalSeconds:0.#}s).");
    }

    private void OnOutputReceived(object? sender, AppOutputLine line)
    {
        Watch? watch;
        lock (_gate)
        {
            if (!_watches.TryGetValue(line.AppId, out watch))
                return;

            watch.LastOutputAt = _clock.Now;
        }

        var snapshot = watch.Snapshot;
        if (!snapshot.OutputObservable || snapshot.Patterns.Count == 0)
            return;

        var hit = snapshot.Patterns.Match(line.Text);
        if (hit is not null)
            Terminate(watch, HealthFailure.FailurePattern, Loc.T("Health.ReasonPattern", hit));
    }

    private void OnRunCompleted(object? sender, AppRunRecord record)
    {
        lock (_gate)
        {
            // Khởi động lại nhanh: tiến trình cũ có thể báo kết thúc sau khi tiến trình mới đã được
            // theo dõi — chỉ gỡ khi đúng PID.
            if (!_watches.TryGetValue(record.AppId, out var watch) || watch.ProcessId != record.ProcessId)
                return;

            _watches.Remove(record.AppId);
            watch.Dispose();
        }
    }

    private async Task TickAsync(Watch watch)
    {
        lock (_gate)
        {
            // Một lần thăm dò chậm (chờ hết timeout) không được chồng lên nhịp sau.
            if (_disposed || watch.Disposed || watch.Busy)
                return;

            watch.Busy = true;
        }

        try
        {
            // Đọc lại cấu hình mỗi nhịp: người dùng có thể vừa đổi mục tiêu hay ngưỡng trong lúc app đang chạy.
            var app = FindApp(watch.AppId);
            if (app is null || !HealthPolicy.AppliesTo(app))
            {
                StopWatching(watch);
                return;
            }

            var snapshot = Snapshot.Of(app, watch.Snapshot);
            watch.Snapshot = snapshot;

            var interval = HealthPolicy.CheckInterval(snapshot.Check, _minimumInterval);
            DateTimeOffset lastOutputAt;
            lock (_gate)
            {
                if (watch.Disposed)
                    return;

                if (interval != watch.Interval)
                {
                    watch.Interval = interval;
                    watch.Timer?.Change(interval, interval);
                }

                lastOutputAt = watch.LastOutputAt;
            }

            var decision = HealthPolicy.OnTick(snapshot.Check, snapshot.OutputObservable, watch.StartedAt, lastOutputAt, _clock.Now);
            switch (decision.Action)
            {
                case HealthTickAction.Terminate:
                    Terminate(watch, decision.Failure, Loc.T("Health.ReasonSilence", snapshot.Check.SilenceMinutes));
                    break;

                case HealthTickAction.Probe:
                    await ProbeAsync(watch, snapshot).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi kiểm tra sức khoẻ app.", ex);
        }
        finally
        {
            lock (_gate)
                watch.Busy = false;
        }
    }

    private async Task ProbeAsync(Watch watch, Snapshot snapshot)
    {
        var check = snapshot.Check;
        string? failure;

        try
        {
            failure = await _probe
                .ProbeAsync(check.Probe, check.Target, HealthPolicy.ProbeTimeout(check), watch.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Đã thôi theo dõi trong lúc chờ phản hồi.
            return;
        }

        int failures;
        lock (_gate)
        {
            if (watch.Disposed)
                return;

            failures = failure is null ? 0 : watch.ConsecutiveFailures + 1;
            watch.ConsecutiveFailures = failures;
        }

        if (failure is null)
            return;

        var threshold = Math.Max(1, check.FailureThreshold);
        _logger.Warning($"'{snapshot.Name}' không phản hồi thăm dò ({failures}/{threshold}): {failure}");

        if (HealthPolicy.ReachedFailureThreshold(check, failures))
            Terminate(watch, HealthFailure.ProbeFailed, Loc.T("Health.ReasonProbe", failures, check.Target, failure));
    }

    private void Terminate(Watch watch, HealthFailure failure, string reason)
    {
        if (!StopWatching(watch))
        {
            // Đã dừng rồi: mẫu lỗi có thể khớp nhiều dòng liên tiếp, thăm dò và im lặng có thể cùng chạm ngưỡng.
            return;
        }

        var snapshot = watch.Snapshot;
        _logger.Warning($"'{snapshot.Name}' bị coi là treo: {reason} Đang dừng.");

        // Ra lệnh dừng trước rồi mới báo: người nghe sự kiện có thể muốn thấy trạng thái "đang dừng" ngay.
        _processManager
            .TerminateAsync(watch.AppId, RunOutcome.Unhealthy, reason, Math.Max(0, snapshot.StopGraceSeconds) * 1000)
            .ContinueWith(
                t => _logger.Error($"Không dừng được '{snapshot.Name}' sau khi phát hiện treo.", t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);

        Unhealthy?.Invoke(this, new HealthFailedEventArgs(snapshot.App, failure, reason));
    }

    /// <summary>Gỡ đúng lần theo dõi này (không phải lần mới hơn của cùng app). Trả về true nếu có gỡ.</summary>
    private bool StopWatching(Watch watch)
    {
        lock (_gate)
        {
            if (!_watches.TryGetValue(watch.AppId, out var current) || !ReferenceEquals(current, watch))
                return false;

            _watches.Remove(watch.AppId);
            watch.Dispose();
            return true;
        }
    }

    private ManagedApp? FindApp(Guid appId)
        => _source.GetApps().FirstOrDefault(a => a.Id == appId);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var watch in _watches.Values)
                watch.Dispose();
            _watches.Clear();
        }

        _processManager.StatusChanged -= OnStatusChanged;
        _processManager.OutputReceived -= OnOutputReceived;
        _processManager.RunCompleted -= OnRunCompleted;
    }

    /// <summary>
    /// Bản chụp cấu hình lúc theo dõi, để luồng đọc output không phải hỏi <see cref="IAppSource"/>
    /// (thường là view-model, phải nhảy lên luồng giao diện) cho từng dòng.
    /// </summary>
    private sealed class Snapshot
    {
        private Snapshot(ManagedApp app, HealthCheck check, FailurePatternSet patterns)
        {
            App = app;
            Name = app.Name;
            Check = check;
            Patterns = patterns;
            OutputObservable = HealthPolicy.OutputIsObservable(app);
            StopGraceSeconds = app.StopGraceSeconds;
        }

        public ManagedApp App { get; }
        public string Name { get; }
        public HealthCheck Check { get; }
        public FailurePatternSet Patterns { get; }
        public bool OutputObservable { get; }
        public int StopGraceSeconds { get; }

        /// <summary>Chụp lại; giữ bộ mẫu đã biên dịch của bản trước nếu danh sách mẫu không đổi.</summary>
        public static Snapshot Of(ManagedApp app, Snapshot? previous)
        {
            var check = app.HealthCheck.Clone();
            var patterns = previous is not null && previous.Check.FailurePatterns.SequenceEqual(check.FailurePatterns, StringComparer.Ordinal)
                ? previous.Patterns
                : new FailurePatternSet(check.FailurePatterns);

            return new Snapshot(app, check, patterns);
        }
    }

    /// <summary>Trạng thái theo dõi của một tiến trình đang chạy. Các trường đếm được bảo vệ bởi khoá của monitor.</summary>
    private sealed class Watch : IDisposable
    {
        public Watch(Guid appId, int processId, DateTimeOffset startedAt, Snapshot snapshot)
        {
            AppId = appId;
            ProcessId = processId;
            StartedAt = startedAt;
            LastOutputAt = startedAt;
            Snapshot = snapshot;
        }

        public Guid AppId { get; }
        public int ProcessId { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset LastOutputAt { get; set; }
        public int ConsecutiveFailures { get; set; }
        public bool Busy { get; set; }
        public bool Disposed { get; private set; }
        public TimeSpan Interval { get; set; }
        public Timer? Timer { get; set; }

        /// <summary>Gán nguyên tử; luồng đọc output đọc bản mới nhất mà không cần khoá.</summary>
        public volatile Snapshot Snapshot;

        /// <summary>Huỷ lần thăm dò đang chờ khi thôi theo dõi. Không Dispose để luồng thăm dò còn đọc được Token.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        public void Dispose()
        {
            if (Disposed)
                return;
            Disposed = true;

            Timer?.Dispose();
            Timer = null;
            Cancellation.Cancel();
        }
    }
}
