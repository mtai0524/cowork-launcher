using Cowork.Core.News;

namespace Cowork.Core.Models;

/// <summary>
/// Thiết lập bảng tin, lưu trong workspace.json.
///
/// Mặc định đúng với cách dùng thường gặp: đọc AI, agent và công nghệ, phần lớn là tin
/// nước ngoài, vẫn còn một phần tin trong nước.
/// </summary>
public sealed class NewsSettings
{
    /// <summary>Tắt hẳn thì Cowork không gọi ra mạng để lấy tin.</summary>
    public bool Enabled { get; set; } = true;

    public List<NewsTopic> Topics { get; set; } = new()
    {
        NewsTopic.Ai, NewsTopic.Agents, NewsTopic.Technology,
    };

    /// <summary>Có lấy báo trong nước không.</summary>
    public bool IncludeVietnam { get; set; } = true;

    /// <summary>Phần trăm chỗ dành cho tin trong nước. Phần còn lại là tin nước ngoài.</summary>
    public int VietnamPercent { get; set; } = 25;

    /// <summary>Số bài tối đa của một lần đọc.</summary>
    public int MaxItems { get; set; } = 60;

    /// <summary>Bài cũ hơn ngần này ngày thì không hiện nữa — đây là bảng tin theo ngày.</summary>
    public int MaxAgeDays { get; set; } = 3;

    /// <summary>Số bài tối đa lấy từ một nguồn, để một feed đăng dày không nuốt cả trang.</summary>
    public int MaxPerSource { get; set; } = 6;

    /// <summary>Bao lâu thì tự lấy lại. 0 = chỉ lấy khi bấm nút.</summary>
    public int RefreshMinutes { get; set; } = 60;

    /// <summary>Mã những nguồn dựng sẵn mà người dùng đã tắt.</summary>
    public List<string> DisabledSourceIds { get; set; } = new();

    /// <summary>Feed người dùng tự thêm.</summary>
    public List<CustomNewsSource> CustomSources { get; set; } = new();

    /// <summary>Đưa thiết lập sang dạng mà bộ chọn bài dùng, đã kẹp mọi con số về khoảng hợp lệ.</summary>
    public NewsDigestOptions ToDigestOptions() => new()
    {
        Topics = Topics.Distinct().ToList(),
        IncludeVietnam = IncludeVietnam,
        VietnamPercent = Math.Clamp(VietnamPercent, 0, 100),
        MaxItems = Math.Clamp(MaxItems, 1, 500),
        MaxAgeDays = Math.Clamp(MaxAgeDays, 1, 90),
        MaxPerSource = Math.Clamp(MaxPerSource, 1, 100),
    };
}

/// <summary>Một feed người dùng tự khai. Giữ riêng khỏi <see cref="NewsSource"/> vì nó phải chịu được dữ liệu sai.</summary>
public sealed class CustomNewsSource
{
    public string Name { get; set; } = string.Empty;

    public string FeedUrl { get; set; } = string.Empty;

    public NewsRegion Region { get; set; } = NewsRegion.Global;

    public List<NewsTopic> Topics { get; set; } = new();

    /// <summary>
    /// Đổi sang nguồn dùng được, hoặc null nếu khai thiếu. Mã sinh từ địa chỉ nên nó ổn định
    /// qua các lần chạy — bộ nhớ đệm dựa vào đó để giữ lại bài của lần lấy trước.
    /// </summary>
    public NewsSource? ToSource()
    {
        var url = FeedUrl?.Trim() ?? string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (Topics.Count == 0)
            return null;

        var name = string.IsNullOrWhiteSpace(Name) ? uri.Host : Name.Trim();

        return new NewsSource("custom:" + url.ToLowerInvariant(), name, url, Region, Topics.Distinct().ToList());
    }
}
