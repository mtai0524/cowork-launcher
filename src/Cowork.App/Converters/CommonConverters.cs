using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.App.Converters;

/// <summary>bool -> Visibility. Truyền "invert" qua parameter để đảo.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Chuỗi rỗng/null -> Collapsed. Dùng cho các ô thông báo lỗi.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Đổi enum thành nhãn theo ngôn ngữ đang bật.
///
/// Binding có converter không tự chạy lại khi đổi ngôn ngữ, nên mọi danh sách dùng
/// converter này phải được view-model nạp lại (xem <c>RefreshLocalizedText</c>).
/// </summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RunOutcome outcome => Loc.T("Outcome." + outcome),
        RunTrigger trigger => Loc.T("Trigger." + trigger),
        AppWindowStyle style => Loc.T("WindowStyle." + style),
        AppTheme theme => Loc.T("Theme." + theme),
        AppLanguage language => Loc.NativeName(language),
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>So sánh giá trị binding với parameter — dùng cho RadioButton gắn với enum.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null
            ? Enum.Parse(targetType, parameter.ToString()!)
            : Binding.DoNothing;
}

/// <summary>TimeSpan? -> chuỗi thời lượng ngắn gọn.</summary>
public sealed class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan duration)
            return Loc.T("Common.Dash");

        if (duration.TotalSeconds < 60)
            return Loc.T("Duration.Seconds", duration.TotalSeconds.ToString("0.#", culture));

        if (duration.TotalMinutes < 60)
            return Loc.T("Duration.Minutes", (int)duration.TotalMinutes, duration.Seconds.ToString("00"));

        return Loc.T("Duration.Hours", (int)duration.TotalHours, duration.Minutes.ToString("00"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
