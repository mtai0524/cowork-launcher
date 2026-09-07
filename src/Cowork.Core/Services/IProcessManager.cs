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

    /// <summary>Yêu cầu dừng lịch sự; sau <paramref name="graceMs"/> ms thì kill. Kết quả ghi là <see cref="RunOutcome.Cancelled"/> — dừng tay là ý người dùng.</summary>
    Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dừng vì lý do khác ý người dùng (kiểm tra sức khoẻ thất bại…): ghi <paramref name="outcome"/> và
    /// <paramref name="reason"/> vào bản ghi của lần chạy, rồi dừng lịch sự như <see cref="StopAsync"/>.
    /// </summary>
    Task<bool> TerminateAsync(Guid appId, RunOutcome outcome, string? reason, int graceMs = 5000, CancellationToken cancellationToken = default);

    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Các dòng output gần nhất của app (bộ đệm vòng trong bộ nhớ).</summary>
    IReadOnlyList<AppOutputLine> GetOutput(Guid appId);

    /// <summary>
    /// Cửa sổ chính của tiến trình, để chụp ảnh gửi lên hub. Trả <c>0</c> khi app không chạy
    /// hoặc chạy nhưng chưa/không có cửa sổ — dịch vụ nền và console ẩn đều rơi vào đây.
    /// </summary>
    nint MainWindowHandle(Guid appId);

    event EventHandler<AppStatusChanged>? StatusChanged;
    event EventHandler<AppOutputLine>? OutputReceived;

    /// <summary>Phát sinh khi tiến trình kết thúc, kèm bản ghi lịch sử đã hoàn tất.</summary>
    event EventHandler<AppRunRecord>? RunCompleted;
}
