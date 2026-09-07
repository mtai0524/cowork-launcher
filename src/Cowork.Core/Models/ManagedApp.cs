using System.Text.Json.Serialization;

namespace Cowork.Core.Models;

/// <summary>Cách hiển thị cửa sổ của tiến trình con.</summary>
public enum AppWindowStyle
{
    Normal = 0,
    Minimized = 1,
    Hidden = 2,
}

/// <summary>
/// Một app do Cowork quản lý: chạy bằng lệnh gì, với tham số/biến môi trường nào,
/// những file cấu hình nào thuộc về nó, và lịch chạy hằng ngày ra sao.
/// </summary>
public sealed class ManagedApp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "App mới";

    public string Description { get; set; } = string.Empty;

    /// <summary>Nhóm để gom app trên dashboard (vd: "Backup", "Báo cáo sáng").</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Đường dẫn tới exe/bat/ps1/cmd. Hỗ trợ biến môi trường dạng %VAR%.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Biến môi trường bổ sung, chỉ áp dụng cho tiến trình con của app này.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ConfigFileRef> ConfigFiles { get; set; } = new();

    public ScheduleRule Schedule { get; set; } = new();

    /// <summary>
    /// Cách nhận ra app còn sống nhưng đã treo: thăm dò cổng/URL định kỳ, hoặc theo dõi output.
    /// Treo thì Cowork dừng app với kết quả <see cref="RunOutcome.Unhealthy"/>; phần khởi động lại
    /// hay thử lại đi theo <see cref="KeepAlive"/> và <see cref="RetryCount"/> như mọi lần kết thúc khác.
    /// </summary>
    public HealthCheck HealthCheck { get; set; } = new();

    /// <summary>App bị tắt sẽ không chạy tay lẫn chạy theo lịch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Yêu cầu quyền admin (chạy qua ShellExecute verb runas).</summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>Thu output stdout/stderr vào log. Tắt nếu app cần cửa sổ console riêng.</summary>
    public bool CaptureOutput { get; set; } = true;

    public AppWindowStyle WindowStyle { get; set; } = AppWindowStyle.Normal;

    /// <summary>Chặn khởi chạy nếu instance trước còn đang chạy.</summary>
    public bool SingleInstance { get; set; } = true;

    /// <summary>Tự kết thúc tiến trình nếu vượt quá số phút này. 0 = không giới hạn.</summary>
    public int TimeoutMinutes { get; set; }

    /// <summary>
    /// Giữ app luôn chạy: khi tiến trình tự thoát (kể cả thoát êm hay bị timeout), Cowork
    /// khởi động lại nó. Bấm Dừng thì không — dừng tay là ý người dùng.
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>Chờ ngần này giây trước khi khởi động lại, để app kịp nhả cổng/file.</summary>
    public int RestartDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Trần số lần tự khởi động lại trong một giờ; chạm trần thì bỏ cuộc và báo lỗi.
    /// 0 = không giới hạn. Không có trần này, một app hỏng hẳn sẽ bị khởi động lại vô tận.
    /// </summary>
    public int MaxRestartsPerHour { get; set; } = 10;

    /// <summary>
    /// Số lần chạy lại khi một lần chạy kết thúc lỗi hoặc quá giờ. 0 = không thử lại.
    /// Dành cho job chạy xong là thoát; app bật <see cref="KeepAlive"/> đã được khởi động lại nên bỏ qua.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>Chờ ngần này giây trước mỗi lần thử lại.</summary>
    public int RetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Các mã thoát được coi là thành công. Mặc định chỉ có 0; robocopy chẳng hạn trả 1 khi đã sao chép.
    /// </summary>
    public List<int> SuccessExitCodes { get; set; } = new() { ExitCodes.Default };

    /// <summary>
    /// Khi bấm Dừng: sau khi đóng cửa sổ chính hoặc gửi Ctrl+C, chờ ngần này giây cho app tự thoát
    /// rồi mới kill.
    /// </summary>
    public int StopGraceSeconds { get; set; } = 5;

    /// <summary>Thứ tự hiển thị và thứ tự chạy khi bấm "Chạy tất cả".</summary>
    public int Order { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Lần cuối scheduler kích hoạt app này (dùng để tính mốc kế tiếp + chạy bù).</summary>
    public DateTimeOffset? LastScheduledRunAt { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    public int? LastExitCode { get; set; }

    [JsonIgnore]
    public bool CanRun => Enabled && !string.IsNullOrWhiteSpace(ExecutablePath);

    public ManagedApp Clone() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Group = Group,
        ExecutablePath = ExecutablePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        EnvironmentVariables = new Dictionary<string, string>(EnvironmentVariables, StringComparer.OrdinalIgnoreCase),
        ConfigFiles = ConfigFiles.Select(c => c.Clone()).ToList(),
        Schedule = Schedule.Clone(),
        HealthCheck = HealthCheck.Clone(),
        Enabled = Enabled,
        RunAsAdministrator = RunAsAdministrator,
        CaptureOutput = CaptureOutput,
        WindowStyle = WindowStyle,
        SingleInstance = SingleInstance,
        TimeoutMinutes = TimeoutMinutes,
        KeepAlive = KeepAlive,
        RestartDelaySeconds = RestartDelaySeconds,
        MaxRestartsPerHour = MaxRestartsPerHour,
        RetryCount = RetryCount,
        RetryDelaySeconds = RetryDelaySeconds,
        SuccessExitCodes = new List<int>(SuccessExitCodes),
        StopGraceSeconds = StopGraceSeconds,
        Order = Order,
        CreatedAt = CreatedAt,
        LastScheduledRunAt = LastScheduledRunAt,
        LastRunAt = LastRunAt,
        LastExitCode = LastExitCode,
    };

    /// <summary>Thư mục làm việc thực tế: ưu tiên cấu hình, nếu trống thì lấy thư mục chứa exe.</summary>
    public string ResolveWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(WorkingDirectory))
            return Environment.ExpandEnvironmentVariables(WorkingDirectory);

        var exe = Environment.ExpandEnvironmentVariables(ExecutablePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(exe))
            return Environment.CurrentDirectory;

        return Path.GetDirectoryName(Path.GetFullPath(exe)) ?? Environment.CurrentDirectory;
    }
}
