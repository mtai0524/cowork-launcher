using Cowork.Core.Localization;

namespace Cowork.Core.News;

/// <summary>
/// "Bài này đăng cách đây bao lâu" viết thành chữ. Một bảng tin đọc bằng khoảng cách,
/// không bằng dấu thời gian: "20 phút trước" nói ngay điều mà "14:35" bắt phải tính nhẩm.
///
/// <c>now</c> là tham số chứ không gọi đồng hồ bên trong — cùng lý do với phần còn lại
/// của Cowork: nhãn thời gian phải kiểm được mà không phải chờ đồng hồ chạy.
/// </summary>
public static class NewsTime
{
    public static string Describe(DateTimeOffset published, DateTimeOffset now)
        => Describe(Loc.Current, published, now);

    /// <summary>Bản nhận ngôn ngữ tường minh, cho hub phục vụ nhiều người mỗi người một ngôn ngữ.</summary>
    public static string Describe(AppLanguage language, DateTimeOffset published, DateTimeOffset now)
    {
        var elapsed = now - published;

        // Feed ghi ngày lệch múi giờ về phía trước thì "âm 3 phút" là vô nghĩa với người đọc.
        if (elapsed < TimeSpan.FromMinutes(1))
            return Loc.T(language, "News.JustNow");

        if (elapsed < TimeSpan.FromHours(1))
            return Loc.T(language, "News.MinutesAgo", (int)elapsed.TotalMinutes);

        if (elapsed < TimeSpan.FromDays(1))
            return Loc.T(language, "News.HoursAgo", (int)elapsed.TotalHours);

        return Loc.T(language, "News.DaysAgo", (int)elapsed.TotalDays);
    }
}
