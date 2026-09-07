using System.Text.Json.Serialization;

namespace Cowork.Core.Models;

/// <summary>Cách thăm dò định kỳ xem app còn phản hồi không.</summary>
public enum HealthProbeKind
{
    /// <summary>Không thăm dò; chỉ còn watchdog theo output nếu có đặt.</summary>
    None = 0,

    /// <summary>Mở kết nối TCP tới host:cổng; nối được là còn sống.</summary>
    TcpPort = 1,

    /// <summary>Gửi GET tới một URL; mã 2xx là còn sống.</summary>
    HttpGet = 2,
}

/// <summary>
/// Kiểm tra sức khoẻ của một app đang chạy. Keep-alive chỉ thấy tiến trình <em>thoát</em>; app còn
/// sống mà đơ (deadlock, treo mạng) thì vẫn bị coi là đang chạy. Hai cách bịt: thăm dò cổng/URL
/// định kỳ, và theo dõi output — im lặng quá lâu hoặc in ra mẫu báo lỗi thì coi là treo.
/// </summary>
public sealed class HealthCheck
{
    public HealthProbeKind Probe { get; set; } = HealthProbeKind.None;

    /// <summary>TCP: "8080" hoặc "host:8080". HTTP: "http://localhost:8080/health".</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Chu kỳ kiểm tra, dùng cho cả thăm dò lẫn xét im lặng.</summary>
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>Thăm dò quá ngần này giây không có phản hồi thì tính là một lần lỗi.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Số lần thăm dò lỗi liên tiếp trước khi coi là treo; một lần lẻ có thể chỉ là mạng chập.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>Bỏ qua mọi kiểm tra trong ngần này giây đầu sau khi khởi chạy, để app kịp lên.</summary>
    public int StartupGraceSeconds { get; set; } = 30;

    /// <summary>Không có dòng output nào trong ngần này phút thì coi là treo. 0 = tắt.</summary>
    public int SilenceMinutes { get; set; }

    /// <summary>
    /// Mẫu regex (không phân biệt hoa thường) mà hễ xuất hiện trong output là app đã hỏng.
    /// Mẫu không phải regex hợp lệ được so như chuỗi thường. Rỗng = tắt.
    /// </summary>
    public List<string> FailurePatterns { get; set; } = new();

    [JsonIgnore]
    public bool HasProbe => Probe != HealthProbeKind.None;

    [JsonIgnore]
    public bool HasOutputWatch => SilenceMinutes > 0 || FailurePatterns.Count > 0;

    [JsonIgnore]
    public bool IsEnabled => HasProbe || HasOutputWatch;

    public HealthCheck Clone() => new()
    {
        Probe = Probe,
        Target = Target,
        IntervalSeconds = IntervalSeconds,
        TimeoutSeconds = TimeoutSeconds,
        FailureThreshold = FailureThreshold,
        StartupGraceSeconds = StartupGraceSeconds,
        SilenceMinutes = SilenceMinutes,
        FailurePatterns = new List<string>(FailurePatterns),
    };
}
