namespace Cowork.Core.Models;

/// <summary>Trạng thái tức thời của một app (không lưu xuống đĩa).</summary>
public enum AppRuntimeState
{
    Idle = 0,
    Starting = 1,
    Running = 2,
    Stopping = 3,
    Failed = 4,
}

public sealed record AppStatusChanged(Guid AppId, AppRuntimeState State, int ProcessId, string? Message);

/// <summary>Một dòng output do tiến trình con sinh ra.</summary>
public sealed record AppOutputLine(Guid AppId, DateTimeOffset Timestamp, string Text, bool IsError);
