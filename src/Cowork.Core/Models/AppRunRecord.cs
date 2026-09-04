namespace Cowork.Core.Models;

/// <summary>Nguồn kích hoạt một lần chạy.</summary>
public enum RunTrigger
{
    Manual = 0,
    Schedule = 1,
    Startup = 2,
    RunAll = 3,
}

public enum RunOutcome
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    TimedOut = 4,
    NotStarted = 5,
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
