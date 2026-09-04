using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record StartResult(bool Started, int ProcessId, string? Error)
{
    public static StartResult Fail(string error) => new(false, 0, error);
    public static StartResult Ok(int processId) => new(true, processId, null);
}

public interface IProcessManager
{
    /// <summary>App đang có tiến trình sống do Cowork khởi chạy.</summary>
    bool IsRunning(Guid appId);

    IReadOnlyCollection<Guid> RunningAppIds { get; }

    StartResult Start(ManagedApp app, RunTrigger trigger);

    /// <summary>Yêu cầu dừng lịch sự; sau <paramref name="graceMs"/> ms thì kill.</summary>
    Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default);

    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Các dòng output gần nhất của app (bộ đệm vòng trong bộ nhớ).</summary>
    IReadOnlyList<AppOutputLine> GetOutput(Guid appId);

    event EventHandler<AppStatusChanged>? StatusChanged;
    event EventHandler<AppOutputLine>? OutputReceived;

    /// <summary>Phát sinh khi tiến trình kết thúc, kèm bản ghi lịch sử đã hoàn tất.</summary>
    event EventHandler<AppRunRecord>? RunCompleted;
}
