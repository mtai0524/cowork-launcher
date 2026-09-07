using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cowork.Core.Models;
using Cowork.Hub;

namespace Cowork.Tests;

/// <summary>
/// Giữ bảng màu của web khớp bảng màu của desktop.
///
/// Web không đọc được file XAML lúc chạy nên buộc phải chép giá trị sang CSS. Chép tay
/// thì sẽ trôi: sửa một màu bên desktop, bên web ở nguyên, và hai mặt dần khác nhau mà
/// không ai để ý. Test này chặn đúng chuyện đó, đồng thời kéo theo cả
/// <see cref="ThemeContrastTests"/> — CSS trùng XAML nghĩa là tương phản đã được kiểm.
/// </summary>
public class WebPaletteTests
{
    /// <summary>Biến CSS ↔ khoá màu trong XAML. Phải phủ hết, không được để khoá nào ngoài bảng.</summary>
    private static readonly Dictionary<string, string> Mapping = new(StringComparer.Ordinal)
    {
        ["--bg"] = "WindowBackgroundColor",
        ["--surface"] = "SurfaceColor",
        ["--surface-alt"] = "SurfaceAltColor",
        ["--hover"] = "SurfaceHoverColor",
        ["--pressed"] = "SurfacePressedColor",
        ["--selection"] = "SelectionColor",
        ["--row-alt"] = "AlternatingRowColor",
        ["--code-bg"] = "CodeBackgroundColor",
        ["--border"] = "BorderColor",
        ["--divider"] = "DividerColor",
        ["--text"] = "TextColor",
        ["--muted"] = "MutedColor",
        ["--subtle"] = "SubtleColor",
        ["--accent"] = "AccentColor",
        ["--accent-fill"] = "AccentFillColor",
        ["--accent-fill-hover"] = "AccentFillHoverColor",
        ["--accent-fill-pressed"] = "AccentFillPressedColor",
        ["--on-accent"] = "OnAccentColor",
        ["--success"] = "SuccessColor",
        ["--warning"] = "WarningColor",
        ["--danger"] = "DangerColor",
        ["--success-surface"] = "SuccessSurfaceColor",
        ["--warning-surface"] = "WarningSurfaceColor",
        ["--danger-surface"] = "DangerSurfaceColor",
        ["--state-idle"] = "StateIdleColor",
        ["--scroll-track"] = "ScrollBarTrackColor",
        ["--scroll-thumb"] = "ScrollBarThumbColor",
        ["--scroll-thumb-hover"] = "ScrollBarThumbHoverColor",
    };

    private static readonly Regex Block =
        new(@"(?<selector>[^{}]*)\{(?<body>[^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex Variable =
        new(@"(?<name>--[a-z-]+)\s*:\s*(?<value>#[0-9A-Fa-f]{6})\s*;", RegexOptions.Compiled);

    public static TheoryData<AppTheme> Themes()
    {
        var data = new TheoryData<AppTheme>();
        foreach (var theme in Enum.GetValues<AppTheme>())
            data.Add(theme);
        return data;
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void WebTheme_UsesTheSameColorsAsTheDesktopPalette(AppTheme theme)
    {
        var css = WebVariables(theme);
        var xaml = PaletteColors(theme.ToString());

        var mismatched = Mapping
            .Where(pair => !string.Equals(css.GetValueOrDefault(pair.Key), xaml[pair.Value], StringComparison.OrdinalIgnoreCase))
            .Select(pair => $"{pair.Key}: css={css.GetValueOrDefault(pair.Key) ?? "(thiếu)"} xaml={xaml[pair.Value]}")
            .ToList();

        Assert.Empty(mismatched);
    }

    /// <summary>Thiếu một phong cách trên web thì <c>data-theme</c> rơi về mặc định mà không báo gì.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryDesktopTheme_HasAWebBlock(AppTheme theme)
        => Assert.NotEmpty(WebVariables(theme));

    /// <summary>Mọi phong cách phải khai đủ cùng một bộ biến, nếu không một phong cách sẽ hở màu.</summary>
    [Fact]
    public void EveryWebTheme_DefinesTheSameVariables()
    {
        var expected = Mapping.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            var actual = WebVariables(theme).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>Bảng ánh xạ phải phủ hết màu XAML — thêm màu mới bên desktop thì web phải theo.</summary>
    [Fact]
    public void Mapping_CoversEveryPaletteColor()
    {
        var missing = PaletteColors("Dark").Keys
            .Where(key => !Mapping.ContainsValue(key))
            .OrderBy(k => k, StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    private static Dictionary<string, string> WebVariables(AppTheme theme)
    {
        var selector = $"[data-theme=\"{UiPreferences.Slug(theme)}\"]";

        var body = Block.Matches(ReadCss())
            .Where(m => m.Groups["selector"].Value.Contains(selector, StringComparison.Ordinal))
            .Select(m => m.Groups["body"].Value)
            .FirstOrDefault() ?? string.Empty;

        return Variable.Matches(body).ToDictionary(
            m => m.Groups["name"].Value,
            m => m.Groups["value"].Value,
            StringComparer.Ordinal);
    }

    private static string ReadCss()
    {
        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream("Cowork.Tests.Web.app.css")
                           ?? throw new InvalidOperationException("Không thấy tài nguyên nhúng app.css.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, string> PaletteColors(string palette)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream($"Cowork.Tests.Palettes.{palette}.xaml")
                           ?? throw new InvalidOperationException($"Không thấy bảng màu '{palette}'.");

        return XDocument.Load(stream).Root!.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => e.Attribute(xaml + "Key")!.Value, e => e.Value.Trim(), StringComparer.Ordinal);
    }
}
