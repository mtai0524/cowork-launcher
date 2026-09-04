using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Bộ đếm nền, cứ mỗi <see cref="TickInterval"/> lại hỏi <see cref="ScheduleEvaluator"/>
/// xem app nào tới hạn rồi phát sự kiện <see cref="AppDue"/>.
/// Bản thân scheduler không khởi chạy tiến trình — việc đó do tầng ứng dụng quyết định.
/// </summary>
public sealed class DailyScheduler : IScheduler
{
    /// <summary>15 giây đủ mịn cho lịch theo phút mà gần như không tốn CPU.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);

    private readonly IAppSource _source;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    private Timer? _timer;
    private bool _ticking;
    private bool _disposed;

    public DailyScheduler(IAppSource source, IClock clock, ICoworkLogger logger)
    {
        _source = source;
        _clock = clock;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler<ScheduleDueEventArgs>? AppDue;

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning || _disposed)
                return;

            _timer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
            IsRunning = true;
            _logger.Info("Bộ lập lịch đã bật.");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning)
                return;

            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
            _logger.Info("Bộ lập lịch đã tắt.");
        }
    }

    public void Tick()
    {
        // Một tick chạy lâu (do người nghe khởi chạy app) không được chồng lên tick sau.
        lock (_gate)
        {
            if (_ticking)
                return;
            _ticking = true;
        }

        try
        {
            var now = _clock.Now;

            foreach (var app in _source.GetApps())
            {
                if (!ScheduleEvaluator.IsDue(app, now))
                    continue;

                // Đánh dấu trước khi kích hoạt: nếu người nghe ném lỗi thì cũng không
                // lặp vô hạn ở tick kế tiếp.
                _source.MarkScheduled(app.Id, now);

                _logger.Info($"Tới giờ chạy '{app.Name}' theo lịch.");
                AppDue?.Invoke(this, new ScheduleDueEventArgs(app, now, RunTrigger.Schedule));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi trong vòng kiểm tra lịch.", ex);
        }
        finally
        {
            lock (_gate)
                _ticking = false;
        }
    }

    /// <summary>Kích hoạt các app có lịch "khi mở Cowork". Gọi một lần lúc khởi động.</summary>
    public void RunStartupApps()
    {
        var now = _clock.Now;

        foreach (var app in _source.GetApps())
        {
            if (!app.CanRun || !app.Schedule.IsAutomatic)
                continue;
            if (app.Schedule.Kind != ScheduleKind.OnCoworkStartup)
                continue;

            _source.MarkScheduled(app.Id, now);
            AppDue?.Invoke(this, new ScheduleDueEventArgs(app, now, RunTrigger.Startup));
        }
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }
}
