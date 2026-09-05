using System.Globalization;

namespace Cowork.Core.Localization;

/// <summary>Các ngôn ngữ giao diện Cowork hỗ trợ.</summary>
public enum AppLanguage
{
    Vietnamese = 0,
    English = 1,
}

/// <summary>
/// Bảng chuỗi giao diện. Cố ý dùng dictionary trong mã nguồn thay vì .resx:
/// tầng Core không được kéo theo WPF, và tra cứu tại thời điểm gọi cho phép
/// đổi ngôn ngữ ngay lúc chạy mà không cần dựng lại cửa sổ.
/// </summary>
public static class Loc
{
    private static AppLanguage _current = AppLanguage.Vietnamese;

    /// <summary>Bắn ra sau khi ngôn ngữ đổi, để tầng giao diện làm tươi các nhãn đã sinh.</summary>
    public static event EventHandler? LanguageChanged;

    public static AppLanguage Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static IReadOnlyList<AppLanguage> Available { get; } = new[]
    {
        AppLanguage.Vietnamese, AppLanguage.English,
    };

    /// <summary>Tên ngôn ngữ viết bằng chính ngôn ngữ đó — không dịch theo giao diện hiện tại.</summary>
    public static string NativeName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        _ => "Tiếng Việt",
    };

    /// <summary>Tra một chuỗi theo ngôn ngữ đang bật. Thiếu khoá thì lùi về tiếng Việt, cuối cùng trả chính khoá.</summary>
    public static string T(string key) => T(_current, key);

    public static string T(string key, params object?[] args) => T(_current, key, args);

    /// <summary>
    /// Tra theo một ngôn ngữ chỉ định, không đụng <see cref="Current"/>. Dành cho server phục vụ
    /// nhiều người cùng lúc, mỗi người một ngôn ngữ — ở đó biến tĩnh toàn cục là sai.
    /// </summary>
    public static string T(AppLanguage language, string key)
    {
        var table = language == AppLanguage.English ? StringsEn.Table : StringsVi.Table;
        if (table.TryGetValue(key, out var value))
            return value;

        return StringsVi.Table.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string T(AppLanguage language, string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(language, key), args);

    /// <summary>
    /// Mọi biến thể ngôn ngữ của một khoá. Dùng khi cần nhận ra chuỗi do chính Cowork
    /// sinh ra trước đó (ví dụ tên app mặc định) dù người dùng đã đổi ngôn ngữ từ lúc ấy.
    /// </summary>
    /// <summary>Nhãn ngắn của một ngày trong tuần theo ngôn ngữ hiện tại.</summary>
    public static string DayName(DayOfWeek day) => T("Day." + day);

    public static IEnumerable<string> AllVariants(string key)
    {
        if (StringsVi.Table.TryGetValue(key, out var vi))
            yield return vi;

        if (StringsEn.Table.TryGetValue(key, out var en) && en != vi)
            yield return en;
    }
}
