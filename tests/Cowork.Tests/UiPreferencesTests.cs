using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Hub;

namespace Cowork.Tests;

/// <summary>Lựa chọn hiển thị đọc từ cookie, nên giá trị vào có thể là bất cứ thứ gì.</summary>
public class UiPreferencesTests
{
    [Theory]
    [InlineData("Light", AppTheme.Light)]
    [InlineData("midnight", AppTheme.Midnight)]
    [InlineData("HIGHCONTRAST", AppTheme.HighContrast)]
    public void ParseTheme_AcceptsAnyCasing(string value, AppTheme expected)
        => Assert.Equal(expected, UiPreferences.ParseTheme(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khong-ton-tai")]
    [InlineData("99")]
    public void ParseTheme_FallsBackToDark(string? value)
        => Assert.Equal(AppTheme.Dark, UiPreferences.ParseTheme(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("klingon")]
    [InlineData("42")]
    public void ParseLanguage_FallsBackToVietnamese(string? value)
        => Assert.Equal(AppLanguage.Vietnamese, UiPreferences.ParseLanguage(value));

    [Fact]
    public void ParseLanguage_AcceptsEnglish()
        => Assert.Equal(AppLanguage.English, UiPreferences.ParseLanguage("english"));

    /// <summary>Slug phải là chữ thường vì app.css dùng đúng dạng đó trong selector.</summary>
    [Fact]
    public void Slug_IsLowercase()
        => Assert.Equal("highcontrast", UiPreferences.Slug(AppTheme.HighContrast));

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/agents", "/agents")]
    [InlineData("/agents?x=1", "/agents?x=1")]
    public void SafeReturnUrl_KeepsLocalPaths(string value, string expected)
        => Assert.Equal(expected, UiPreferences.SafeReturnUrl(value));

    /// <summary>
    /// Nút đổi giao diện nhận đường quay lại từ trang, nên một liên kết dựng sẵn có thể
    /// nhét địa chỉ ngoài vào đó. Dạng <c>//host</c> nguy hiểm nhất: trình duyệt hiểu là
    /// tuyệt đối nhưng nhìn thì giống đường dẫn nội bộ.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("agents")]
    [InlineData("https://evil.example.com/x")]
    [InlineData("//evil.example.com")]
    [InlineData("\\\\evil.example.com")]
    public void SafeReturnUrl_RefusesAnythingNotLocal(string? value)
        => Assert.Equal("/", UiPreferences.SafeReturnUrl(value));

    /// <summary>Mọi phong cách đều phải bày ra được, nếu không có cái không ai chọn tới.</summary>
    [Fact]
    public void Available_ListsEveryTheme()
        => Assert.Equal(
            Enum.GetValues<AppTheme>().OrderBy(t => t),
            UiTheme.Available.OrderBy(t => t));
}
