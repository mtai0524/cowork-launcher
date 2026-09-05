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

/// <summary>Tên các phương thức SignalR — một chỗ duy nhất để hai đầu không lệch nhau.</summary>
public static class HubMethods
{
    public const string AgentPath = "/hubs/agent";

    // Agent -> hub
    public const string Register = "Register";
    public const string UpdateApp = "UpdateApp";
    public const string CommandResult = "ReportResult";

    // Hub -> agent
    public const string Execute = "Execute";
}
