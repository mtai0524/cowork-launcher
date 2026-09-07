namespace Cowork.Core.Models;

/// <summary>Nguồn kích hoạt một lần chạy.</summary>
public enum RunTrigger
{
    Manual = 0,
    Schedule = 1,
    Startup = 2,
    RunAll = 3,

    /// <summary>Cowork tự khởi động lại vì app bật "giữ luôn chạy".</summary>
    KeepAlive = 4,

    /// <summary>Lệnh gửi từ web qua hub.</summary>
    Remote = 5,

    /// <summary>Cowork tự chạy lại vì lần chạy trước kết thúc lỗi và app có đặt số lần thử lại.</summary>
    Retry = 6,

    /// <summary>Máy vừa thức dậy, mở khoá, hoặc có mạng trở lại.</summary>
    SystemEvent = 7,
}

public enum RunOutcome
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    TimedOut = 4,
    NotStarted = 5,

    /// <summary>Còn sống nhưng treo — kiểm tra sức khoẻ thất bại nên Cowork đã dừng nó. Lý do nằm ở <see cref="AppRunRecord.Error"/>.</summary>
    Unhealthy = 6,
}

/// <summary>Một dòng lịch sử chạy, lưu bền để xem lại hôm qua app nào lỗi.</summary>
public sealed class AppRunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public RunTrigger Trigger { get; set; }
    public RunOutcome Outcome { get; set; } = RunOutcome.Running;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public int? ExitCode { get; set; }
    public int ProcessId { get; set; }
    public string? Error { get; set; }

    public TimeSpan? Duration => FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
}
