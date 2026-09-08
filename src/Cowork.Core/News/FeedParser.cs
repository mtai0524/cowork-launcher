using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Cowork.Core.News;

/// <summary>
/// Đọc một feed thành danh sách bài. Nhận cả ba định dạng đang lưu hành —
/// RSS 2.0, RSS 1.0/RDF (arXiv dùng) và Atom — bằng cách bỏ qua namespace và
/// tra theo tên cục bộ của thẻ. Ba định dạng đặt tên khác nhau nhưng chỗ chứa
/// tiêu đề, liên kết, tóm tắt và ngày thì tương ứng một-một.
///
/// Hàm thuần: vào là chuỗi XML, ra là danh sách. Không chạm mạng, không chạm đồng hồ —
/// thời điểm dự phòng cho bài thiếu ngày được truyền vào.
/// </summary>
public static class FeedParser
{
    /// <summary>Đủ để nhận ra bài nói gì, không đủ để một feed đăng nguyên bài làm vỡ bố cục.</summary>
    public const int MaxSummaryLength = 280;

    private static readonly string[] SummaryTags = { "description", "summary", "content", "encoded", "subtitle" };

    private static readonly string[] DateTags = { "pubDate", "published", "updated", "date", "created" };

    /// <summary>Ngày của cả feed, dùng khi từng bài không mang ngày riêng.</summary>
    private static readonly string[] FeedDateTags = { "pubDate", "lastBuildDate", "updated" };

    private static readonly Regex ScriptOrStyle =
        new(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Đọc nội dung feed của một nguồn. XML hỏng trả về danh sách rỗng chứ không ném:
    /// một nguồn trả rác không được làm hỏng cả bảng tin.
    /// </summary>
    /// <param name="xml">Nội dung feed tải về.</param>
    /// <param name="source">Nguồn đã khai — cấp chủ đề và vùng cho mọi bài trong feed.</param>
    /// <param name="fetchedAt">Ngày dùng cho bài mà feed không nói thời điểm đăng.</param>
    public static IReadOnlyList<NewsItem> Parse(string? xml, NewsSource source, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(xml))
            return Array.Empty<NewsItem>();

        XDocument document;
        try
        {
            // Feed là dữ liệu của người lạ: cấm DTD và bộ phân giải ngoài, nếu không một
            // thực thể ngoài có thể kéo Cowork đi đọc file trên máy hoặc phình bộ nhớ.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };

            using var reader = XmlReader.Create(new StringReader(xml), settings);
            document = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return Array.Empty<NewsItem>();
        }

        // Vài feed chỉ ghi ngày một lần cho cả danh sách — GitHub Trending là ví dụ: nó là
        // ảnh chụp bảng xếp hạng của một ngày, từng repo trong đó không có ngày riêng. Lấy
        // ngày của feed còn hơn lùi thẳng về giờ tải, vì giờ tải luôn là "vừa xong" và đẩy
        // cả feed lên đầu bảng tin sau mỗi lần làm tươi.
        var feedDate = ReadFeedDate(document);

        var items = new List<NewsItem>();

        foreach (var entry in document.Descendants().Where(IsEntry))
        {
            var link = ReadLink(entry);
            var title = Clean(Text(entry, "title"));

            // Không có tiêu đề lẫn địa chỉ thì không hiển thị được gì — bỏ luôn.
            if (title.Length == 0 || link.Length == 0)
                continue;

            var published = ReadDate(entry);

            items.Add(new NewsItem
            {
                Title = title,
                Link = link,
                Summary = Truncate(Clean(ReadSummary(entry)), MaxSummaryLength),
                PublishedAt = published ?? feedDate ?? fetchedAt,

                // Ngày của cả feed vẫn không phải ngày của bài này, nên nó vẫn tính là phỏng đoán.
                DateEstimated = published is null,
                SourceId = source.Id,
                SourceName = source.Name,
                Region = source.Region,
                Topics = source.Topics,
            });
        }

        return items;
    }

    private static bool IsEntry(XElement element)
        => element.Name.LocalName is "item" or "entry";

    /// <summary>
    /// RSS để địa chỉ trong nội dung thẻ <c>link</c>, Atom để trong thuộc tính <c>href</c>
    /// và có thể có nhiều thẻ (bản thay thế, bản rút gọn, trang bình luận).
    /// </summary>
    private static string ReadLink(XElement entry)
    {
        string? fallback = null;

        foreach (var element in entry.Elements().Where(e => e.Name.LocalName == "link"))
        {
            var rel = (string?)element.Attribute("rel");

            // Atom: chỉ "alternate" (hoặc không khai) mới là trang bài; "replies", "edit"… thì không.
            if (rel is not null && rel is not "alternate")
                continue;

            var candidate = (string?)element.Attribute("href") ?? element.Value;
            if (!IsUsableLink(candidate))
                continue;

            if (rel == "alternate")
                return candidate!.Trim();

            fallback ??= candidate!.Trim();
        }

        if (fallback is not null)
            return fallback;

        // RSS 2.0 cho phép thay link bằng guid có isPermaLink="true".
        var guid = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "guid");
        var guidValue = guid?.Value;

        return IsUsableLink(guidValue) && (string?)guid!.Attribute("isPermaLink") != "false"
            ? guidValue!.Trim()
            : string.Empty;
    }

    /// <summary>
    /// Chỉ nhận http/https. Bảng tin biến chuỗi này thành liên kết bấm được, nên một
    /// <c>javascript:</c> hay <c>file:</c> lọt qua đây là lỗ hổng, không phải bài lỗi.
    /// </summary>
    private static bool IsUsableLink(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string ReadSummary(XElement entry)
    {
        foreach (var tag in SummaryTags)
        {
            var value = Text(entry, tag);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static DateTimeOffset? ReadDate(XElement entry)
    {
        foreach (var tag in DateTags)
        {
            var value = Text(entry, tag);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (TryParseDate(value.Trim(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryParseDate(string value, out DateTimeOffset parsed)
    {
        const System.Globalization.DateTimeStyles Styles = System.Globalization.DateTimeStyles.AllowWhiteSpaces;
        var invariant = System.Globalization.CultureInfo.InvariantCulture;

        if (DateTimeOffset.TryParse(value, invariant, Styles, out parsed))
            return true;

        // .NET đối chiếu thứ trong tuần của dạng RFC 1123 và từ chối cả chuỗi nếu nó sai —
        // mà "Mon" cho một ngày thứ Ba là lỗi quen thuộc của trình sinh feed. Phần thứ không
        // mang thông tin nào mà phần còn lại chưa có, nên bỏ nó đi rồi đọc lại.
        var comma = value.IndexOf(',');
        if (comma > 0 && comma <= 4)
            return DateTimeOffset.TryParse(value[(comma + 1)..], invariant, Styles, out parsed);

        return false;
    }

    /// <summary>
    /// Ngày ghi ở mức channel/feed. Bỏ qua mọi thẻ nằm bên trong một item/entry — ở đó
    /// <c>updated</c> là ngày của chính bài, không phải của feed.
    /// </summary>
    private static DateTimeOffset? ReadFeedDate(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (Array.IndexOf(FeedDateTags, element.Name.LocalName) < 0)
                continue;

            if (element.Ancestors().Any(IsEntry))
                continue;

            if (TryParseDate(element.Value.Trim(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static string Text(XElement entry, string localName)
        => entry.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value ?? string.Empty;

    /// <summary>Gỡ HTML, giải mã thực thể, gộp khoảng trắng — tóm tắt của feed hầu như luôn là HTML.</summary>
    internal static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = ScriptOrStyle.Replace(value, " ");
        text = Tag.Replace(text, " ");

        // Giải mã hai lần: nhiều feed bọc HTML đã escape bên trong CDATA đã escape.
        text = WebUtility.HtmlDecode(text);
        if (text.Contains('&'))
            text = WebUtility.HtmlDecode(text);

        return Whitespace.Replace(text, " ").Trim();
    }

    internal static string Truncate(string value, int max)
    {
        if (value.Length <= max)
            return value;

        // Cắt ở khoảng trắng gần nhất để không đứt giữa từ.
        var cut = value.LastIndexOf(' ', Math.Min(max, value.Length - 1));
        if (cut < max / 2)
            cut = max;

        return new StringBuilder(value[..cut].TrimEnd()).Append('…').ToString();
    }
}
