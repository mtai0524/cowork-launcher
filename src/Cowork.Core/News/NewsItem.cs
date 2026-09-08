using System.Text;

namespace Cowork.Core.News;

/// <summary>
/// Một bài trong bảng tin, đã tách khỏi định dạng gốc của feed.
///
/// Chỉ giữ những gì đủ để quyết định có đọc hay không — tiêu đề, một đoạn tóm tắt ngắn,
/// nguồn và thời điểm. Nội dung đầy đủ nằm ở trang gốc; Cowork không sao chép bài về.
/// </summary>
public sealed record NewsItem
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Địa chỉ bài gốc. Luôn là http/https tuyệt đối — <see cref="FeedParser"/> loại phần còn lại.</summary>
    public string Link { get; init; } = string.Empty;

    /// <summary>Tóm tắt đã gỡ thẻ HTML và cắt ngắn.</summary>
    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Feed không nói thời điểm đăng, giá trị trên là lúc lấy về.</summary>
    public bool DateEstimated { get; init; }

    public string SourceId { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public NewsRegion Region { get; init; }

    public IReadOnlyList<NewsTopic> Topics { get; init; } = Array.Empty<NewsTopic>();

    /// <summary>
    /// Khoá gộp trùng. Cùng một bài hay đi qua vài nguồn với đuôi theo dõi khác nhau
    /// (<c>?utm_source=…</c>), nên khoá bỏ chuỗi truy vấn và chuẩn hoá phần còn lại.
    /// </summary>
    public string DedupeKey => NormalizeLink(Link) is { Length: > 0 } normalized
        ? normalized
        : NormalizeTitle(Title);

    internal static string NormalizeLink(string link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return string.Empty;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');

        return (host + path).ToLowerInvariant();
    }

    /// <summary>Dự phòng khi bài không có địa chỉ dùng được: gộp theo tiêu đề đã bỏ dấu câu.</summary>
    internal static string NormalizeTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        var lastWasSpace = false;

        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
