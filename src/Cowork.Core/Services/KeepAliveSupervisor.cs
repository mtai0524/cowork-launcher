using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record RestartScheduledEventArgs(ManagedApp App, DateTimeOffset DueAt, int Attempt, int? Limit);

public sealed record RestartDueEventArgs(ManagedApp App, int Attempt);

public sealed record KeepAliveGaveUpEventArgs(ManagedApp App, int Attempts, TimeSpan Window);

/// <summary>
/// Theo dõi các app bật "giữ luôn chạy": nghe <see cref="IProcessManager.RunCompleted"/>,
/// hỏi <see cref="KeepAlivePolicy"/> rồi hẹn giờ khởi động lại.
///
/// Giống <see cref="DailyScheduler"/>, lớp này <em>không</em> tự khởi chạy tiến trình — nó chỉ
/// phát <see cref="RestartDue"/> và để tầng ứng dụng quyết định, nhờ vậy mọi lần chạy đều đi
/// qua cùng một chỗ kiểm tra cấu hình và ghi lịch sử.
/// </summary>
public sealed class KeepAliveSupervisor : IDisposable
{
    private readonly IProcessManager _processManager;
    private readonly IAppSource _source;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    private readonly Dictionary<Guid, Timer> _pending = new();
    private readonly Dictionary<Guid, List<DateTimeOffset>> _restarts = new();
    private bool _disposed;

    public KeepAliveSupervisor(IProcessManager processManager, IAppSource source, IClock clock, ICoworkLogger logger)
    {
        _processManager = processManager;
        _source = source;
        _clock = clock;
        _logger = logger;

        _processManager.RunCompleted += OnRunCompleted;
    }

    /// <summary>Đã hẹn giờ khởi động lại; giao diện dùng để hiện đếm ngược.</summary>
    public event EventHandler<RestartScheduledEventArgs>? RestartScheduled;

    /// <summary>Tới giờ khởi động lại. Người nghe chịu trách nhiệm khởi chạy.</summary>
    public event EventHandler<RestartDueEventArgs>? RestartDue;

    /// <summary>Chạm trần số lần khởi động lại — app coi như hỏng hẳn, cần người xem.</summary>
    public event EventHandler<KeepAliveGaveUpEventArgs>? GaveUp;

    public bool IsWaiting(Guid appId)
    {
        lock (_gate)
            return _pending.ContainsKey(appId);
    }

    /// <summary>Huỷ lần khởi động lại đang chờ (người dùng bấm Dừng hoặc tự chạy tay).</summary>
    public bool Cancel(Guid appId)
    {
        lock (_gate)
        {
            if (!_pending.Remove(appId, out var timer))
                return false;

            timer.Dispose();
            return true;
        }
    }

    /// <summary>Số lần đã tự khởi động lại trong cửa sổ đếm, để hiển thị "lần 3/10".</summary>
    public int RecentRestartCount(Guid appId)
    {
        lock (_gate)
        {
            return _restarts.TryGetValue(appId, out var list)
                ? KeepAlivePolicy.CountWithinWindow(list, _clock.Now)
                : 0;
        }
    }

    private void OnRunCompleted(object? sender, AppRunRecord record)
    {
        if (_disposed)
            return;

        try
        {
            var app = FindApp(record.AppId);
            if (app is null)
                return;

            var now = _clock.Now;
            RestartDecision decision;
            int attemptsSoFar;

            lock (_gate)
            {
                var history = HistoryFor(app.Id);
                attemptsSoFar = KeepAlivePolicy.CountWithinWindow(history, now);
                decision = KeepAlivePolicy.Decide(app, record, history, now);
            }

            if (decision.Refusal == RestartRefusal.LimitReached)
            {
                _logger.Warning($"'{app.Name}' đã tự khởi động lại {attemptsSoFar} lần trong một giờ, tạm dừng giữ chạy.");
                GaveUp?.Invoke(this, new KeepAliveGaveUpEventArgs(app, attemptsSoFar, KeepAlivePolicy.Window));
                return;
            }

            if (!decision.ShouldRestart)
                return;

            Schedule(app, decision.Delay, attemptsSoFar + 1, now);
        }
        catch (Exception ex)
        {
            // Đây là callback từ luồng nền; lỗi lọt ra ngoài sẽ làm sập cả Cowork.
            _logger.Error("Lỗi khi xét khởi động lại app.", ex);
        }
    }

    private void Schedule(ManagedApp app, TimeSpan delay, int attempt, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            // Đã có một lần chờ rồi thì thay bằng lần mới, không xếp chồng.
            if (_pending.Remove(app.Id, out var existing))
                existing.Dispose();

            var id = app.Id;
            _pending[id] = new Timer(_ => OnDue(id), null, delay, Timeout.InfiniteTimeSpan);
        }

        var limit = app.MaxRestartsPerHour > 0 ? app.MaxRestartsPerHour : (int?)null;
        _logger.Info($"'{app.Name}' đã thoát, sẽ khởi động lại sau {delay.TotalSeconds:0}s (lần {attempt}).");
        RestartScheduled?.Invoke(this, new RestartScheduledEventArgs(app, now + delay, attempt, limit));
    }

    private void OnDue(Guid appId)
    {
        try
        {
            int attempt;
            lock (_gate)
            {
                // Bị huỷ trong lúc chờ thì timer đã bị gỡ khỏi danh sách — không làm gì nữa.
                if (!_pending.Remove(appId, out var timer))
                    return;
                timer.Dispose();
            }

            // Đọc lại app tại thời điểm tới giờ: người dùng có thể vừa tắt keep-alive hoặc tắt app.
            var app = FindApp(appId);
            if (app is null || !app.KeepAlive || !app.CanRun)
                return;

            var now = _clock.Now;
            lock (_gate)
            {
                var history = HistoryFor(appId);
                history.Add(now);
                history.RemoveAll(at => now - at >= KeepAlivePolicy.Window);
                attempt = history.Count;
            }

            RestartDue?.Invoke(this, new RestartDueEventArgs(app, attempt));
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi khởi động lại app.", ex);
        }
    }

    private ManagedApp? FindApp(Guid appId)
        => _source.GetApps().FirstOrDefault(a => a.Id == appId);

    private List<DateTimeOffset> HistoryFor(Guid appId)
    {
        if (!_restarts.TryGetValue(appId, out var list))
            _restarts[appId] = list = new List<DateTimeOffset>();
        return list;
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

        _processManager.RunCompleted -= OnRunCompleted;
    }
}
