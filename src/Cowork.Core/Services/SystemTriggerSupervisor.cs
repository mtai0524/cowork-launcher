using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record SystemTriggerDueEventArgs(ManagedApp App, SystemEventKind Kind);

/// <summary>
/// Chạy app theo sự kiện của máy: thức dậy sau khi ngủ, mở khoá màn hình, có mạng trở lại.
///
/// Cùng khuôn với <see cref="DailyScheduler"/>: không tự khởi chạy tiến trình, chỉ phát
/// <see cref="Due"/> để mọi lần chạy đi qua đúng một chỗ kiểm tra cấu hình và ghi lịch sử.
/// Phần "có nên chạy không" nằm trọn ở <see cref="SystemTriggerPolicy"/>, hàm thuần.
/// </summary>
public sealed class SystemTriggerSupervisor : IDisposable
{
    private readonly ISystemEventSource _source;
    private readonly IAppSource _apps;
    private readonly IProcessManager _processManager;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    private readonly Dictionary<(Guid AppId, SystemEventKind Kind), DateTimeOffset> _lastTriggered = new();
    private readonly Dictionary<(Guid AppId, SystemEventKind Kind), Timer> _pending = new();
    private bool _disposed;

    public SystemTriggerSupervisor(
        ISystemEventSource source,
        IAppSource apps,
        IProcessManager processManager,
        IClock clock,
        ICoworkLogger logger)
    {
        _source = source;
        _apps = apps;
        _processManager = processManager;
        _clock = clock;
        _logger = logger;

        _source.Occurred += OnSystemEvent;
    }

    /// <summary>Tới lúc chạy app vì một sự kiện hệ thống. Người nghe chịu trách nhiệm khởi chạy.</summary>
    public event EventHandler<SystemTriggerDueEventArgs>? Due;

    public bool IsWaiting(Guid appId, SystemEventKind kind)
    {
        lock (_gate)
            return _pending.ContainsKey((appId, kind));
    }

    private void OnSystemEvent(object? sender, SystemEventKind kind)
    {
        if (_disposed)
            return;

        try
        {
            var now = _clock.Now;
            _logger.Info($"Sự kiện hệ thống: {kind}.");

            foreach (var app in _apps.GetApps())
            {
                if (!SystemTriggerPolicy.AppliesTo(app))
                    continue;

                DateTimeOffset? last;
                lock (_gate)
                {
                    last = _lastTriggered.TryGetValue((app.Id, kind), out var at) ? at : null;

                    // Đã hẹn giờ cho đúng cặp app–sự kiện này rồi thì thôi, không xếp chồng.
                    if (_pending.ContainsKey((app.Id, kind)))
                        continue;
                }

                var decision = SystemTriggerPolicy.Decide(
                    app, kind, last, _processManager.IsRunning(app.Id), now);

                if (decision.ShouldRun)
                    Schedule(app, kind, decision.Delay, now);
            }
        }
        catch (Exception ex)
        {
            // Callback từ luồng của Windows; lỗi lọt ra ngoài sẽ làm sập cả Cowork.
            _logger.Error("Lỗi khi xét sự kiện hệ thống.", ex);
        }
    }

    private void Schedule(ManagedApp app, SystemEventKind kind, TimeSpan delay, DateTimeOffset now)
    {
        var key = (app.Id, kind);

        lock (_gate)
        {
            if (_disposed)
                return;

            // Ghi mốc ngay lúc nhận sự kiện, không đợi tới lúc chạy: khoảng lặng phải tính từ sự kiện,
            // nếu không một loạt sự kiện dồn dập trong lúc chờ sẽ lọt hết qua.
            _lastTriggered[key] = now;
            _pending[key] = new Timer(_ => OnDue(key), null, delay, Timeout.InfiniteTimeSpan);
        }

        _logger.Info($"'{app.Name}' sẽ chạy sau {delay.TotalSeconds:0}s vì sự kiện {kind}.");
    }

    private void OnDue((Guid AppId, SystemEventKind Kind) key)
    {
        try
        {
            lock (_gate)
            {
                if (!_pending.Remove(key, out var timer))
                    return;
                timer.Dispose();
            }

            // Đọc lại app tại thời điểm tới giờ: người dùng có thể vừa tắt app hoặc bỏ đăng ký.
            var app = _apps.GetApps().FirstOrDefault(a => a.Id == key.AppId);
            if (app is null || !app.CanRun || !app.SystemTriggers.Contains(key.Kind))
                return;

            if (app.SingleInstance && _processManager.IsRunning(app.Id))
                return;

            Due?.Invoke(this, new SystemTriggerDueEventArgs(app, key.Kind));
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi chạy app theo sự kiện hệ thống.", ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var timer in _pending.Values)
                timer.Dispose();
            _pending.Clear();
        }

        _source.Occurred -= OnSystemEvent;
    }
}
