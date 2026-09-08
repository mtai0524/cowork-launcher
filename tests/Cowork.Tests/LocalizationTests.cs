using System.Text.RegularExpressions;
using Cowork.Core.Configuration;
using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.News;

namespace Cowork.Tests;

/// <summary>
/// Giữ hai bảng chuỗi khớp nhau. Lệch khoá không gây lỗi build — nó chỉ hiện ra
/// lúc chạy dưới dạng một nhãn tiếng Việt lọt giữa giao diện tiếng Anh, hoặc tệ hơn
/// là <see cref="FormatException"/> khi số chỗ chèn hai bên không bằng nhau.
/// </summary>
public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}", RegexOptions.Compiled);

    [Fact]
    public void BothTables_HaveTheSameKeys()
    {
        var missingInEnglish = StringsVi.Table.Keys.Except(StringsEn.Table.Keys).OrderBy(k => k).ToList();
        var missingInVietnamese = StringsEn.Table.Keys.Except(StringsVi.Table.Keys).OrderBy(k => k).ToList();

        Assert.Empty(missingInEnglish);
        Assert.Empty(missingInVietnamese);
    }

    [Fact]
    public void NoTranslation_IsEmpty()
    {
        var blank = StringsVi.Table.Concat(StringsEn.Table)
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(blank);
    }

    [Fact]
    public void Translations_UseTheSamePlaceholders()
    {
        var mismatched = StringsVi.Table
            .Where(pair => StringsEn.Table.ContainsKey(pair.Key))
            .Where(pair => !PlaceholderSet(pair.Value).SetEquals(PlaceholderSet(StringsEn.Table[pair.Key])))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(mismatched);
    }

    [Theory]
    [InlineData(AppLanguage.Vietnamese)]
    [InlineData(AppLanguage.English)]
    public void EveryEnumValue_HasALabel(AppLanguage language)
    {
        WithLanguage(language, () =>
        {
            AssertAllNamed<RunOutcome>("Outcome.");
            AssertAllNamed<RunTrigger>("Trigger.");
            AssertAllNamed<AppWindowStyle>("WindowStyle.");
            AssertAllNamed<AppTheme>("Theme.");
            AssertAllNamed<DayOfWeek>("Day.");
            AssertAllNamed<ScanConfidence>("Confidence.");
            AssertAllNamed<AppRuntimeState>("WebState.");
            AssertAllNamed<HealthProbeKind>("HealthProbe.");
            AssertAllNamed<DependencyWait>("DependencyWait.");
            AssertAllNamed<SystemEventKind>("SystemEvent.");
            AssertAllNamed<NewsTopic>("Topic.");
            AssertAllNamed<NewsRegion>("Region.");
        });
    }

    [Fact]
    public void MissingKey_FallsBackInsteadOfThrowing()
        => Assert.Equal("Khong.Ton.Tai", Loc.T("Khong.Ton.Tai"));

    [Fact]
    public void ScheduleSummary_FollowsTheActiveLanguage()
    {
        var schedule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(7, 30, 0) },
        };

        var vietnamese = WithLanguage(AppLanguage.Vietnamese, schedule.Describe);
        var english = WithLanguage(AppLanguage.English, schedule.Describe);

        Assert.Contains("07:30", vietnamese);
        Assert.Contains("mỗi ngày", vietnamese);
        Assert.Contains("every day", english);
    }

    private static HashSet<string> PlaceholderSet(string text)
        => Placeholder.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static void AssertAllNamed<TEnum>(string prefix)
        where TEnum : struct, Enum
    {
        foreach (var name in Enum.GetNames<TEnum>())
        {
            var key = prefix + name;

            // Loc trả về chính khoá khi tra hụt — dùng đúng dấu hiệu đó làm khẳng định.
            Assert.NotEqual(key, Loc.T(key));
        }
    }

    /// <summary>Chạy một đoạn với ngôn ngữ chỉ định rồi trả lại ngôn ngữ cũ.</summary>
    private static void WithLanguage(AppLanguage language, Action action)
        => WithLanguage(language, () =>
        {
            action();
            return 0;
        });

    private static T WithLanguage<T>(AppLanguage language, Func<T> action)
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = language;
            return action();
        }
        finally
        {
            Loc.Current = previous;
        }
    }
}
