using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.News;
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

    /// <summary>
    /// Chủ đề tin nằm trong cookie, nên giá trị vào cũng có thể là bất cứ thứ gì —
    /// kể cả cookie do một bản Cowork cũ hơn để lại.
    /// </summary>
    [Fact]
    public void ParseNews_ReadsTopicsAndTheVietnamFlag()
    {
        var selection = UiPreferences.ParseNews("Ai,Agents|vn");

        Assert.Equal(new[] { NewsTopic.Ai, NewsTopic.Agents }, selection.Topics);
        Assert.True(selection.IncludeVietnam);
    }

    [Fact]
    public void ParseNews_WithoutTheFlag_LeavesVietnamOut()
        => Assert.False(UiPreferences.ParseNews("Technology").IncludeVietnam);

    [Fact]
    public void ParseNews_IgnoresNamesItDoesNotKnow()
    {
        var selection = UiPreferences.ParseNews("Ai,ChuDeKhongTonTai,99|vn");

        Assert.Equal(NewsTopic.Ai, Assert.Single(selection.Topics));
    }

    /// <summary>Chưa chọn gì thì lấy mặc định của bảng tin, không phải một trang trống.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseNews_WithNoCookie_UsesTheSameDefaultsAsTheApp(string? value)
    {
        var selection = UiPreferences.ParseNews(value);
        var defaults = new NewsSettings();

        Assert.Equal(defaults.Topics, selection.Topics);
        Assert.Equal(defaults.IncludeVietnam, selection.IncludeVietnam);
    }

    /// <summary>Bỏ hết tick là một lựa chọn có thật — không được lẳng lặng quay về mặc định.</summary>
    [Fact]
    public void ParseNews_WithAnEmptyTopicList_StaysEmpty()
        => Assert.Empty(UiPreferences.ParseNews("|vn").Topics);

    [Fact]
    public void FormatNews_AndParseNews_RoundTrip()
    {
        var cookie = UiPreferences.FormatNews(new[] { NewsTopic.Security, NewsTopic.Ai }, includeVietnam: false);
        var selection = UiPreferences.ParseNews(cookie);

        Assert.Equal(new[] { NewsTopic.Security, NewsTopic.Ai }, selection.Topics);
        Assert.False(selection.IncludeVietnam);
    }

    /// <summary>Lựa chọn trên web phải ra cùng một bảng tin như lựa chọn tương đương trên máy.</summary>
    [Fact]
    public void Selection_TurnsIntoTheSameDigestOptionsAsTheApp()
    {
        var options = new NewsSelection(new[] { NewsTopic.Ai }, IncludeVietnam: true).ToDigestOptions();
        var fromApp = new NewsSettings { Topics = new() { NewsTopic.Ai } }.ToDigestOptions();

        // So từng phần chứ không so cả bản ghi: Topics là một danh sách, mà record so
        // danh sách bằng tham chiếu — hai bản ghi giống hệt nội dung vẫn báo khác nhau.
        Assert.Equal(fromApp.Topics, options.Topics);
        Assert.Equal(fromApp.IncludeVietnam, options.IncludeVietnam);
        Assert.Equal(fromApp.VietnamPercent, options.VietnamPercent);
        Assert.Equal(fromApp.MaxItems, options.MaxItems);
        Assert.Equal(fromApp.MaxAgeDays, options.MaxAgeDays);
        Assert.Equal(fromApp.MaxPerSource, options.MaxPerSource);
    }
}
