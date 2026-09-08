using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.News;

namespace Cowork.Hub;

/// <summary>
/// Lựa chọn hiển thị của một trình duyệt: ngôn ngữ và phong cách màu.
///
/// Desktop dùng biến tĩnh vì chỉ có một người dùng; server phục vụ nhiều người nên mỗi
/// phiên phải giữ lựa chọn riêng. Đọc từ cookie ngay lúc dựng trang thay vì giữ trong
/// bộ nhớ: giữ trong bộ nhớ thì tải lại trang là mất, mà đổi phong cách thì phải tải lại.
/// </summary>
public static class UiPreferences
{
    public const string LanguageCookie = "cowork.lang";
    public const string ThemeCookie = "cowork.theme";
    public const string NewsCookie = "cowork.news";

    /// <summary>Một năm — đây là sở thích hiển thị, không phải phiên đăng nhập.</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(365);

    public static AppLanguage ParseLanguage(string? value)
        => Enum.TryParse<AppLanguage>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AppLanguage.Vietnamese;

    public static AppTheme ParseTheme(string? value)
        => Enum.TryParse<AppTheme>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : AppTheme.Dark;

    /// <summary>Giá trị cho thuộc tính <c>data-theme</c>, khớp với khoá trong app.css.</summary>
    public static string Slug(AppTheme theme) => theme.ToString().ToLowerInvariant();

    /// <summary>
    /// Chủ đề tin của trình duyệt này. Giá trị trong cookie có dạng
    /// <c>Ai,Agents,Technology|vn</c> — danh sách chủ đề, rồi cờ có kèm tin trong nước.
    /// Tên nào không nhận ra thì bỏ qua: cookie cũ còn sót lại không được làm hỏng trang.
    /// </summary>
    public static NewsSelection ParseNews(string? value)
    {
        var defaults = new NewsSettings();

        if (string.IsNullOrWhiteSpace(value))
            return new NewsSelection(defaults.Topics, defaults.IncludeVietnam);

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var topics = (parts.Length > 0 ? parts[0] : string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.TryParse<NewsTopic>(name, ignoreCase: true, out var topic) && Enum.IsDefined(topic)
                ? topic
                : (NewsTopic?)null)
            .Where(topic => topic is not null)
            .Select(topic => topic!.Value)
            .Distinct()
            .ToList();

        var vietnam = parts.Skip(1).Any(p => p.Equals("vn", StringComparison.OrdinalIgnoreCase));

        return new NewsSelection(topics, vietnam);
    }

    public static string FormatNews(IEnumerable<NewsTopic> topics, bool includeVietnam)
        => string.Join(",", topics.Distinct()) + (includeVietnam ? "|vn" : string.Empty);

    /// <summary>
    /// Chỉ nhận đường dẫn nội bộ. Nếu không, một liên kết dựng sẵn có thể lái người dùng
    /// sang trang ngoài ngay sau khi họ bấm đổi giao diện.
    /// </summary>
    public static string SafeReturnUrl(string? value)
        => !string.IsNullOrEmpty(value) && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal)
            ? value
            : "/";
}

/// <summary>Ngôn ngữ của phiên trình duyệt hiện tại. Tra bảng chuỗi bằng bản nhận ngôn ngữ tường minh.</summary>
public sealed class UiLanguage
{
    public UiLanguage(IHttpContextAccessor accessor)
        => Current = UiPreferences.ParseLanguage(accessor.HttpContext?.Request.Cookies[UiPreferences.LanguageCookie]);

    public AppLanguage Current { get; }

    /// <summary>Ngôn ngữ mà nút đổi ngôn ngữ sẽ chuyển sang.</summary>
    public AppLanguage Other => Current == AppLanguage.Vietnamese ? AppLanguage.English : AppLanguage.Vietnamese;

    /// <summary>Mã cho thuộc tính <c>lang</c> của thẻ html, để trình duyệt biết ngắt dòng và đọc màn hình đúng.</summary>
    public string HtmlLang => Current == AppLanguage.English ? "en" : "vi";

    public string T(string key) => Loc.T(Current, key);

    public string T(string key, params object?[] args) => Loc.T(Current, key, args);
}

/// <summary>Phong cách màu của phiên trình duyệt hiện tại.</summary>
public sealed class UiTheme
{
    public UiTheme(IHttpContextAccessor accessor)
        => Current = UiPreferences.ParseTheme(accessor.HttpContext?.Request.Cookies[UiPreferences.ThemeCookie]);

    public AppTheme Current { get; }

    public string Slug => UiPreferences.Slug(Current);

    /// <summary>Thứ tự bày trên thanh chọn: từ tối vừa tới sáng, rồi tới bản tương phản cao.</summary>
    public static IReadOnlyList<AppTheme> Available { get; } = new[]
    {
        AppTheme.Dark, AppTheme.Midnight, AppTheme.Light, AppTheme.HighContrast,
    };
}

/// <summary>Chủ đề và vùng mà một trình duyệt đang chọn đọc.</summary>
public sealed record NewsSelection(IReadOnlyList<NewsTopic> Topics, bool IncludeVietnam)
{
    /// <summary>Đưa về dạng bộ chọn bài dùng, giữ nguyên các con số mặc định của bảng tin.</summary>
    public NewsDigestOptions ToDigestOptions()
    {
        var defaults = new NewsSettings { Topics = Topics.ToList(), IncludeVietnam = IncludeVietnam };
        return defaults.ToDigestOptions();
    }
}

/// <summary>Lựa chọn bảng tin của phiên trình duyệt hiện tại.</summary>
public sealed class UiNews
{
    public UiNews(IHttpContextAccessor accessor)
        => Selection = UiPreferences.ParseNews(accessor.HttpContext?.Request.Cookies[UiPreferences.NewsCookie]);

    public NewsSelection Selection { get; }
}
