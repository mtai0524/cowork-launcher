namespace Cowork.Core.Models;

/// <summary>Toàn bộ dữ liệu người dùng của Cowork, tuần tự hoá thành workspace.json.</summary>
public sealed class CoworkWorkspace
{
    /// <summary>Phiên bản schema, dùng cho migration về sau.</summary>
    public int Version { get; set; } = 1;

    public List<ManagedApp> Apps { get; set; } = new();

    public WorkspaceSettings Settings { get; set; } = new();
}

public sealed class WorkspaceSettings
{
    /// <summary>Bật bộ đếm lịch nền.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Thu nhỏ xuống khay hệ thống thay vì thoát khi bấm X.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Khởi động cùng Windows (ghi khoá Run trong registry).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Số dòng output giữ trong bộ nhớ cho mỗi app.</summary>
    public int OutputBufferLines { get; set; } = 2000;

    /// <summary>Số ngày giữ lịch sử chạy.</summary>
    public int HistoryRetentionDays { get; set; } = 30;
}
