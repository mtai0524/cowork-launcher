using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record RetryScheduledEventArgs(ManagedApp App, TimeSpan Delay, int Attempt, int Limit);

public sealed record RetryDueEventArgs(ManagedApp App, int Attempt, int Limit);

public sealed record RetriesExhaustedEventArgs(ManagedApp App, int Attempts, AppRunRecord Record);

/// <summary>
/// Thử lại job chạy lỗi: nghe <see cref="IProcessManager.RunCompleted"/>, hỏi <see cref="RetryPolicy"/>
/// rồi hẹn giờ. Cùng nguyên tắc với <see cref="KeepAliveSupervisor"/>: không tự khởi chạy tiến trình,
/// chỉ phát <see cref="RetryDue"/> để tầng ứng dụng chạy qua đúng một chỗ kiểm tra cấu hình và ghi lịch sử.
///
/// Một "chuỗi" thử lại bắt đầu từ một lần chạy thường (tay, lịch, chạy tất cả…) và gồm các lần chạy
/// có nguồn <see cref="RunTrigger.Retry"/> nối tiếp nó. Bất kỳ lần chạy thường nào cũng mở chuỗi mới.
/// </summary>
public sealed class RetrySupervisor : IDisposable
{
    private readonly IProcessManager _processManager;
    private readonly IAppSource _source;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    private readonly Dictionary<Guid, Timer> _pending = new();

    /// <summary>Số lần đã thử lại trong chuỗi hiện tại của từng app.</summary>
    private readonly Dictionary<Guid, int> _attempts = new();
    private bool _disposed;

    public RetrySupervisor(IProcessManager processManager, IAppSource source, ICoworkLogger logger)
    {
        _processManager = processManager;
        _source = source;
        _logger = logger;

        _processManager.RunCompleted += OnRunCompleted;
    }

    /// <summary>Đã hẹn giờ thử lại; giao diện dùng để hiện đếm ngược.</summary>
    public event EventHandler<RetryScheduledEventArgs>? RetryScheduled;

    /// <summary>Tới giờ thử lại. Người nghe chịu trách nhiệm khởi chạy.</summary>
    public event EventHandler<RetryDueEventArgs>? RetryDue;

    /// <summary>Hết lượt mà vẫn lỗi — lúc này mới đáng báo cho người dùng.</summary>
    public event EventHandler<RetriesExhaustedEventArgs>? RetriesExhausted;

    public bool IsWaiting(Guid appId)
    {
        lock (_gate)
            return _pending.ContainsKey(appId);
    }

    /// <summary>Số lần đã thử lại trong chuỗi hiện tại.</summary>
    public int AttemptsFor(Guid appId)
    {
        lock (_gate)
            return _attempts.GetValueOrDefault(appId);
    }

    /// <summary>Huỷ lần thử lại đang chờ và khép chuỗi (người dùng bấm Dừng hoặc tự chạy tay).</summary>
    public bool Cancel(Guid appId)
    {
        lock (_gate)
        {
            _attempts.Remove(appId);
            if (!_pending.Remove(appId, out var timer))
                return false;

            timer.Dispose();
            return true;
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

            int attemptsSoFar;
            lock (_gate)
            {
                // Lần chạy thường mở chuỗi mới; chỉ lần chạy có nguồn Retry mới nối tiếp chuỗi cũ.
                if (record.Trigger != RunTrigger.Retry)
                    _attempts.Remove(app.Id);

                attemptsSoFar = _attempts.GetValueOrDefault(app.Id);
            }

            var decision = RetryPolicy.Decide(app, record, attemptsSoFar);
            if (decision.ShouldRetry)
            {
                Schedule(app, decision.Delay, attemptsSoFar + 1);
                return;
            }

            // Không thử lại nữa, dù vì thành công, bị dừng tay hay hết lượt: chuỗi khép lại ở đây.
            lock (_gate)
                _attempts.Remove(app.Id);

            if (decision.Refusal == RetryRefusal.LimitReached)
            {
                _logger.Warning($"'{app.Name}' vẫn lỗi sau {attemptsSoFar} lần thử lại, bỏ cuộc.");
                RetriesExhausted?.Invoke(this, new RetriesExhaustedEventArgs(app, attemptsSoFar, record));
            }
        }
        catch (Exception ex)
        {
            // Callback từ luồng nền; lỗi lọt ra ngoài sẽ làm sập cả Cowork.
            _logger.Error("Lỗi khi xét thử lại app.", ex);
        }
    }

    private void Schedule(ManagedApp app, TimeSpan delay, int attempt)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_pending.Remove(app.Id, out var existing))
                existing.Dispose();

            var id = app.Id;
            _pending[id] = new Timer(_ => OnDue(id), null, delay, Timeout.InfiniteTimeSpan);
        }

        _logger.Info($"'{app.Name}' kết thúc lỗi, sẽ thử lại sau {delay.TotalSeconds:0}s (lần {attempt}/{app.RetryCount}).");
        RetryScheduled?.Invoke(this, new RetryScheduledEventArgs(app, delay, attempt, app.RetryCount));
    }

    private void OnDue(Guid appId)
    {
        try
        {
            lock (_gate)
            {
                if (!_pending.Remove(appId, out var timer))
                    return;
                timer.Dispose();
            }

            // Đọc lại app tại thời điểm tới giờ: người dùng có thể vừa tắt thử lại hoặc bật keep-alive.
            var app = FindApp(appId);
            if (app is null || !RetryPolicy.AppliesTo(app))
            {
                lock (_gate)
                    _attempts.Remove(appId);
                return;
            }

            int attempt;
            lock (_gate)
            {
                attempt = _attempts.GetValueOrDefault(appId) + 1;
                _attempts[appId] = attempt;
            }

            RetryDue?.Invoke(this, new RetryDueEventArgs(app, attempt, app.RetryCount));
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi thử lại app.", ex);
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

            foreach (var timer in _pending.Values)
                timer.Dispose();
            _pending.Clear();
            _attempts.Clear();
        }

        _processManager.RunCompleted -= OnRunCompleted;
    }
}
