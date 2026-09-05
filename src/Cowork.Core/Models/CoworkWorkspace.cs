using Cowork.Core.Localization;

namespace Cowork.Core.Models;

/// <summary>Bộ chủ đề màu có sẵn của Cowork.</summary>
public enum AppTheme
{
    /// <summary>Nền xám xanh đậm — mặc định.</summary>
    Dark = 0,

    /// <summary>Nền sáng cho phòng nhiều ánh sáng.</summary>
    Light = 1,

    /// <summary>Nền gần như đen, hợp màn OLED và làm việc buổi tối.</summary>
    Midnight = 2,

    /// <summary>Đen tuyền, chữ trắng, viền rõ — dành cho mắt kém hoặc màn chói.</summary>
    HighContrast = 3,
}

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

    /// <summary>Hiện thông báo ở khay hệ thống khi app chạy lỗi hoặc không giữ chạy được.</summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>Địa chỉ hub quản lý từ xa (https://…). Trống = không kết nối.</summary>
    public string HubUrl { get; set; } = string.Empty;

    /// <summary>Mã agent do hub cấp cho máy này.</summary>
    public string HubToken { get; set; } = string.Empty;

    /// <summary>Chủ đề màu đang dùng.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Ngôn ngữ giao diện đang dùng.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.Vietnamese;
}
