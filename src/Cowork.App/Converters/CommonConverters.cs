using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Cowork.Core.Models;

namespace Cowork.App.Converters;

/// <summary>bool -> Visibility. Truyền ParameterInvert=true qua parameter để đảo.</summary>
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

/// <summary>Trạng thái chạy -> màu chấm tròn trên danh sách app.</summary>
public sealed class RuntimeStateToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Idle = Frozen("#9AA4B2");
    public static readonly SolidColorBrush Running = Frozen("#22A06B");
    public static readonly SolidColorBrush Starting = Frozen("#E2B203");
    public static readonly SolidColorBrush Failed = Frozen("#E5484D");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AppRuntimeState.Running => Running,
            AppRuntimeState.Starting or AppRuntimeState.Stopping => Starting,
            AppRuntimeState.Failed => Failed,
            _ => Idle,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Dòng output lỗi hiển thị màu đỏ để dễ soi trong nhật ký.</summary>
public sealed class OutputErrorToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ErrorBrush = Freeze("#E5484D");
    private static readonly SolidColorBrush NormalBrush = Freeze("#D8DEE9");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ErrorBrush : NormalBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Kết quả một lần chạy -> màu chữ trong bảng lịch sử.</summary>
public sealed class RunOutcomeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            RunOutcome.Succeeded => RuntimeStateToBrushConverter.Running,
            RunOutcome.Running => RuntimeStateToBrushConverter.Starting,
            RunOutcome.Failed or RunOutcome.NotStarted or RunOutcome.TimedOut => RuntimeStateToBrushConverter.Failed,
            _ => RuntimeStateToBrushConverter.Idle,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Đổi enum thành nhãn tiếng Việt cho bảng lịch sử.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RunOutcome.Running => "Đang chạy",
        RunOutcome.Succeeded => "Thành công",
        RunOutcome.Failed => "Lỗi",
        RunOutcome.Cancelled => "Đã dừng",
        RunOutcome.TimedOut => "Quá giờ",
        RunOutcome.NotStarted => "Không chạy được",
        RunTrigger.Manual => "Thủ công",
        RunTrigger.Schedule => "Theo lịch",
        RunTrigger.Startup => "Khi mở app",
        RunTrigger.RunAll => "Chạy tất cả",
        AppWindowStyle.Normal => "Bình thường",
        AppWindowStyle.Minimized => "Thu nhỏ",
        AppWindowStyle.Hidden => "Ẩn hoàn toàn",
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
            return "—";

        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:0.#}s";
        if (duration.TotalMinutes < 60)
            return $"{(int)duration.TotalMinutes}p{duration.Seconds:00}";
        return $"{(int)duration.TotalHours}g{duration.Minutes:00}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
