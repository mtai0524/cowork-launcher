using Cowork.Core.Services;

namespace Cowork.Core.Models;

/// <summary>Trạng thái tức thời của một app (không lưu xuống đĩa).</summary>
public enum AppRuntimeState
{
    Idle = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Failed = 4,

    /// <summary>Đã thoát, đang đếm ngược để tự khởi động lại.</summary>
    WaitingRestart = 5,
}

public sealed record AppStatusChanged(Guid AppId, AppRuntimeState State, int ProcessId, string? Message);

/// <summary>
/// Một dòng trong nhật ký của app: do tiến trình con in ra, hoặc do chính Cowork ghi khi
/// có sự kiện đáng chú ý (yêu cầu dừng, quá giờ…).
/// </summary>
public sealed record AppOutputLine(Guid AppId, DateTimeOffset Timestamp, string Text, LogSource Source)
{
    /// <summary>Giữ lại cho phần đánh dấu đang tô đỏ dòng lỗi.</summary>
    public bool IsError => Source == LogSource.StandardError;

    public static AppOutputLine FromApp(Guid appId, DateTimeOffset at, string text, bool isError)
        => new(appId, at, text, isError ? LogSource.StandardError : LogSource.StandardOutput);

    public static AppOutputLine FromCowork(Guid appId, DateTimeOffset at, string text)
        => new(appId, at, text, LogSource.Cowork);
}
