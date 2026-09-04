using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record ScheduleDueEventArgs(ManagedApp App, DateTimeOffset DueAt, RunTrigger Trigger);

public interface IScheduler : IDisposable
{
    bool IsRunning { get; }

    void Start();
    void Stop();

    /// <summary>Chạy một vòng kiểm tra ngay lập tức (dùng cho test và cho nút "kiểm tra lịch").</summary>
    void Tick();

    /// <summary>Phát sinh khi một app tới hạn chạy. Người nghe chịu trách nhiệm khởi chạy.</summary>
    event EventHandler<ScheduleDueEventArgs>? AppDue;
}

/// <summary>Nguồn cấp danh sách app hiện tại cho scheduler (thường là view-model của dashboard).</summary>
public interface IAppSource
{
    IReadOnlyList<ManagedApp> GetApps();

    /// <summary>Ghi nhận app vừa được scheduler kích hoạt, để lần tick sau không chạy lại.</summary>
    void MarkScheduled(Guid appId, DateTimeOffset at);
}
