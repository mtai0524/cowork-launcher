using System.Windows;
using Cowork.Core.Models;

namespace Cowork.App.Themes;

/// <summary>
/// Tráo bảng màu đang dùng.
///
/// Bảng màu luôn nằm ở vị trí đầu tiên trong danh sách dictionary hợp nhất của App:
/// Theme.xaml tra màu bằng DynamicResource nên chỉ cần thay đúng phần tử đó là
/// toàn bộ giao diện đang mở đổi màu theo, không cần dựng lại cửa sổ nào.
/// </summary>
public static class ThemeManager
{
    private const int PaletteSlot = 0;

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static IReadOnlyList<AppTheme> Available { get; } = new[]
    {
        AppTheme.Dark, AppTheme.Light, AppTheme.Midnight, AppTheme.HighContrast,
    };

    /// <summary>Khoá chuỗi hiển thị tên chủ đề, dùng với bảng ngôn ngữ.</summary>
    public static string LabelKey(AppTheme theme) => "Theme." + theme;

    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } application)
            return;

        var palette = new ResourceDictionary { Source = PaletteUri(theme) };
        var merged = application.Resources.MergedDictionaries;

        if (merged.Count > PaletteSlot)
            merged[PaletteSlot] = palette;
        else
            merged.Insert(PaletteSlot, palette);

        Current = theme;
    }

    private static Uri PaletteUri(AppTheme theme)
        => new($"pack://application:,,,/Themes/Palettes/{theme}.xaml", UriKind.Absolute);
}
