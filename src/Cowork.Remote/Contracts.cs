using Cowork.Core.Models;

namespace Cowork.Remote.Contracts;

/// <summary>
/// Ảnh chụp một app gửi từ agent lên hub. Cố ý gửi dữ liệu thô (enum, mốc giờ) thay vì
/// nhãn đã dịch, để web tự dịch theo ngôn ngữ của người đang xem chứ không theo máy agent.
/// </summary>
public sealed record AppSnapshot(
    Guid Id,
    string Name,
    string Group,
    bool Enabled,
    bool KeepAlive,
    AppRuntimeState State,
    int ProcessId,
    DateTimeOffset? LastRunAt,
    int? LastExitCode,
    DateTimeOffset? NextRunAt,
    string ScheduleSummary,
    string? LastError);

/// <summary>Toàn bộ trạng thái của một máy tại một thời điểm. Gửi lúc kết nối và định kỳ.</summary>
public sealed record MachineSnapshot(
    string HostName,
    string AgentVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<AppSnapshot> Apps);

public enum RemoteCommandKind
{
    Run = 0,
    Stop = 1,
    Restart = 2,
}

/// <summary>Lệnh từ web xuống agent. <see cref="RequestId"/> để ghép với kết quả trả về.</summary>
public sealed record RemoteCommand(Guid RequestId, Guid AppId, RemoteCommandKind Kind);

/// <summary>Agent báo lại kết quả một lệnh. <see cref="Message"/> viết bằng ngôn ngữ của agent.</summary>
public sealed record CommandResult(Guid RequestId, bool Ok, string Message);

/// <summary>Web hỏi agent xin một tấm ảnh cửa sổ của app.</summary>
public sealed record ScreenshotRequest(Guid RequestId, Guid AppId);

/// <summary>Vì sao không chụp được. Trả mã để web tự dịch, không gửi câu chữ của agent.</summary>
public enum ScreenshotFailure
{
    None = 0,

    /// <summary>Máy không nối vào hub nên không ai chụp được.</summary>
    MachineOffline,

    /// <summary>Hub không biết app này, hoặc agent đã bỏ nó khỏi danh sách.</summary>
    UnknownApp,

    /// <summary>App không chạy nên chẳng có gì để chụp.</summary>
    NotRunning,

    /// <summary>App chạy nhưng không có cửa sổ: dịch vụ nền, hoặc console đang ẩn.</summary>
    NoWindow,

    /// <summary>Có cửa sổ nhưng hệ điều hành không cho chụp.</summary>
    CaptureFailed,

    /// <summary>
    /// Máy đang nối nhưng không trả lời trong thời hạn. Hay gặp nhất khi agent chạy bản cũ
    /// chưa biết lệnh chụp — nó lặng lẽ bỏ qua, và bên này chỉ thấy im lặng.
    /// </summary>
    Timeout,
}

/// <summary>
/// Ảnh chụp trả về. <see cref="Png"/> rỗng khi <see cref="Failure"/> khác
/// <see cref="ScreenshotFailure.None"/>.
/// </summary>
public sealed record ScreenshotResult(
    Guid RequestId,
    ScreenshotFailure Failure,
    byte[] Png,
    int Width,
    int Height,
    DateTimeOffset TakenAt)
{
    public bool Ok => Failure == ScreenshotFailure.None && Png.Length > 0;

    public static ScreenshotResult Failed(Guid requestId, ScreenshotFailure failure, DateTimeOffset now)
        => new(requestId, failure, Array.Empty<byte>(), 0, 0, now);
}

/// <summary>Tên các phương thức SignalR — một chỗ duy nhất để hai đầu không lệch nhau.</summary>
public static class HubMethods
{
    public const string AgentPath = "/hubs/agent";

    /// <summary>
    /// Một ảnh PNG lớn hơn hẳn mọi thông điệp khác đi qua đây; mức mặc định 32 KB của
    /// SignalR sẽ cắt kết nối giữa chừng. Đặt trần đủ rộng cho ảnh, không rộng hơn.
    /// </summary>
    public const long MaxMessageBytes = 4L * 1024 * 1024;

    // Agent -> hub
    public const string Register = "Register";
    public const string UpdateApp = "UpdateApp";
    public const string CommandResult = "ReportResult";
    public const string ScreenshotResult = "ReportScreenshot";

    // Hub -> agent
    public const string Execute = "Execute";
    public const string Capture = "Capture";
}
