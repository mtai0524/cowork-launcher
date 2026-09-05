using System.Reflection;
using System.Xml.Linq;

namespace Cowork.Tests;

/// <summary>
/// Kiểm tra mọi bảng màu đều đọc được: chữ phải nổi rõ trên nền, không bị chìm.
///
/// Công thức là tỉ lệ tương phản của WCAG 2.1. Ngưỡng đặt cao hơn mức tối thiểu
/// 4.5:1 ở những cặp quan trọng nhất (chữ chính trên nền chính) vì đây là phần mềm
/// nhìn cả ngày, không phải một trang web đọc thoáng qua.
/// </summary>
public class ThemeContrastTests
{
    private static readonly string[] Palettes = { "Dark", "Light", "Midnight", "HighContrast" };

    /// <summary>(màu chữ, màu nền, tỉ lệ tối thiểu).</summary>
    public static readonly (string Foreground, string Background, double Minimum)[] Requirements =
    {
        ("TextColor", "WindowBackgroundColor", 7.0),
        ("TextColor", "SurfaceColor", 7.0),
        ("TextColor", "SurfaceAltColor", 6.0),
        ("TextColor", "SurfaceHoverColor", 4.5),
        ("TextColor", "SurfacePressedColor", 4.5),
        ("TextColor", "SelectionColor", 4.5),
        ("TextColor", "AlternatingRowColor", 6.0),
        ("TextColor", "CodeBackgroundColor", 6.0),
        ("TextColor", "DangerSurfaceColor", 4.5),
        ("TextColor", "WarningSurfaceColor", 4.5),
        ("TextColor", "SuccessSurfaceColor", 4.5),

        ("MutedColor", "WindowBackgroundColor", 4.5),
        ("MutedColor", "SurfaceColor", 4.5),
        ("MutedColor", "SurfaceAltColor", 4.5),

        ("SubtleColor", "SurfaceColor", 4.5),
        ("SubtleColor", "SurfaceAltColor", 4.5),
        ("SubtleColor", "CodeBackgroundColor", 4.5),

        ("AccentColor", "WindowBackgroundColor", 4.5),
        ("AccentColor", "SurfaceColor", 4.5),
        ("AccentColor", "SurfaceAltColor", 4.5),

        // Chữ trên nút chính, kể cả lúc di chuột và lúc bấm.
        ("OnAccentColor", "AccentFillColor", 4.5),
        ("OnAccentColor", "AccentFillHoverColor", 4.5),
        ("OnAccentColor", "AccentFillPressedColor", 4.5),

        ("SuccessColor", "SurfaceColor", 4.5),
        ("WarningColor", "SurfaceColor", 4.5),
        ("DangerColor", "SurfaceColor", 4.5),
        ("SuccessColor", "SuccessSurfaceColor", 4.5),
        ("WarningColor", "WarningSurfaceColor", 4.5),
        ("DangerColor", "DangerSurfaceColor", 4.5),

        // Chấm trạng thái và đường kẻ chỉ cần phân biệt được, không phải đọc.
        ("StateIdleColor", "SurfaceColor", 3.0),
        ("BorderColor", "SurfaceColor", 1.5),
        ("BorderColor", "WindowBackgroundColor", 1.5),
    };

    public static TheoryData<string> PaletteNames()
    {
        var data = new TheoryData<string>();
        foreach (var palette in Palettes)
            data.Add(palette);
        return data;
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void Palette_KeepsTextReadable(string palette)
    {
        var colors = LoadColors(palette);
        var failures = new List<string>();

        foreach (var (foreground, background, minimum) in Requirements)
        {
            var ratio = ContrastRatio(colors[foreground], colors[background]);
            if (ratio < minimum)
                failures.Add($"{foreground} trên {background}: {ratio:0.00} < {minimum:0.0}");
        }

        Assert.Empty(failures);
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void Palette_DefinesEveryColorTheStylesAskFor(string palette)
    {
        var expected = LoadColors("Dark").Keys.OrderBy(k => k, StringComparer.Ordinal);
        var actual = LoadColors(palette).Keys.OrderBy(k => k, StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void Palette_ExposesABrushForEveryColor(string palette)
    {
        var document = LoadDocument(palette);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        var colors = document.Root!.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .Select(e => e.Attribute(xaml + "Key")!.Value);

        var brushed = document.Root.Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => e.Attribute("Color")!.Value)
            .Select(ExtractStaticResourceKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(colors.Where(c => !brushed.Contains(c)));
    }

    private static string ExtractStaticResourceKey(string markup)
        => markup.Trim().TrimStart('{').TrimEnd('}').Replace("StaticResource", string.Empty).Trim();

    private static Dictionary<string, string> LoadColors(string palette)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        return LoadDocument(palette).Root!.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => e.Attribute(xaml + "Key")!.Value, e => e.Value.Trim(), StringComparer.Ordinal);
    }

    private static XDocument LoadDocument(string palette)
    {
        var name = $"Cowork.Tests.Palettes.{palette}.xaml";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Không thấy tài nguyên nhúng '{name}'.");

        return XDocument.Load(stream);
    }

    /// <summary>Tỉ lệ tương phản theo WCAG 2.1, luôn nằm trong khoảng 1 tới 21.</summary>
    private static double ContrastRatio(string first, string second)
    {
        var a = RelativeLuminance(first);
        var b = RelativeLuminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        var channels = new[]
        {
            Convert.ToInt32(value.Substring(0, 2), 16) / 255.0,
            Convert.ToInt32(value.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(value.Substring(4, 2), 16) / 255.0,
        }.Select(c => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)).ToArray();

        return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
    }
}
