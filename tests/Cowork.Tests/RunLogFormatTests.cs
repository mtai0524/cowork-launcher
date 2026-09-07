using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>
/// Định dạng file log một lần chạy. File này được mở lại nhiều tuần sau, và từ nay còn đọc
/// được qua web, nên hình dạng của nó là một hợp đồng chứ không phải chuyện trang trí.
/// </summary>
public class RunLogFormatTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 7, 10, 40, 12, 345, TimeSpan.FromHours(7));

    private static RunLogContext Context(
        IReadOnlyList<string>? environmentNames = null, string arguments = "--full") => new(
        "backup db notaion",
        Guid.Parse("3f2a1c88-9b0e-4d7a-9c11-2b6e5a4d0e13"),
        RunTrigger.Schedule,
        @"C:\tools\backup.bat",
        arguments,
        @"C:\tools",
        environmentNames ?? Array.Empty<string>(),
        new[] { 0, 1 },
        4242,
        Started);

    private static AppRunRecord Record(RunOutcome outcome = RunOutcome.Succeeded, int? exitCode = 0) => new()
    {
        AppName = "backup db notaion",
        Outcome = outcome,
        ExitCode = exitCode,
        StartedAt = Started,
        FinishedAt = Started.AddSeconds(9.5),
    };

    private static string Join(IEnumerable<string> lines) => string.Join("\n", lines);

    [Fact]
    public void Header_CarriesEverythingNeededToReproduceTheRun()
    {
        var text = Join(RunLogFormat.Header(Context()));

        Assert.Contains("backup db notaion", text);
        Assert.Contains("3f2a1c889b0e4d7a9c112b6e5a4d0e13", text);
        Assert.Contains(@"C:\tools\backup.bat", text);
        Assert.Contains("--full", text);
        Assert.Contains(@"C:\tools", text);
        Assert.Contains("4242", text);
        Assert.Contains("0, 1", text);
        Assert.Contains("2026-09-07 10:40:12.345", text);
    }

    /// <summary>
    /// Biến môi trường riêng của app là chỗ người ta để khoá API. Log này mở được từ web,
    /// nên chỉ ghi tên biến — ghi kèm giá trị là tự tay đẩy bí mật lên mạng.
    /// </summary>
    [Fact]
    public void Header_NamesEnvironmentVariables_ButNeverTheirValues()
    {
        var text = Join(RunLogFormat.Header(Context(new[] { "API_KEY", "DB_PASSWORD" })));

        Assert.Contains("API_KEY", text);
        Assert.Contains("DB_PASSWORD", text);
        Assert.DoesNotContain("=", text.Split('\n').First(l => l.StartsWith("Env vars")).Split(':', 2)[1]);
    }

    /// <summary>Dòng trống cho tham số rỗng chỉ làm loãng file.</summary>
    [Fact]
    public void Header_SkipsEmptyArguments()
        => Assert.DoesNotContain("Arguments", Join(RunLogFormat.Header(Context(arguments: string.Empty))));

    [Fact]
    public void Footer_ReportsHowItEnded()
    {
        var text = Join(RunLogFormat.Footer(Record(RunOutcome.Failed, exitCode: 2)));

        Assert.Contains("Exit code", text);
        Assert.Contains("2", text);
        Assert.Contains("Duration", text);
    }

    [Fact]
    public void Footer_HandlesAMissingExitCode()
        => Assert.Contains("?", Join(RunLogFormat.Footer(Record(RunOutcome.Failed, exitCode: null))));

    [Theory]
    [InlineData(LogSource.StandardOutput, "out")]
    [InlineData(LogSource.StandardError, "ERR")]
    [InlineData(LogSource.Cowork, "cowork")]
    public void EveryLine_SaysWhereItCameFrom(LogSource source, string tag)
    {
        var line = new AppOutputLine(Guid.NewGuid(), Started, "xin chao", source);

        Assert.Contains(tag, RunLogFormat.Line(line));
        Assert.EndsWith("xin chao", RunLogFormat.Line(line));
    }

    /// <summary>
    /// Mốc giờ phải có ngày và mili giây: đối chiếu với log của hệ thống khác mà chỉ có
    /// giờ:phút:giây thì không ghép được, và lần chạy vắt qua nửa đêm trông như đi lùi.
    /// </summary>
    [Fact]
    public void EveryLine_CarriesAFullTimestamp()
    {
        var text = RunLogFormat.Line(new AppOutputLine(Guid.NewGuid(), Started, "x", LogSource.StandardOutput));

        Assert.StartsWith("2026-09-07 10:40:12.345 ", text);
    }

    /// <summary>Cột nội dung phải thẳng hàng, nếu không mắt không lướt dọc file được.</summary>
    [Fact]
    public void Lines_LineUpAcrossSources()
    {
        var offsets = Enum.GetValues<LogSource>()
            .Select(source => RunLogFormat.Line(new AppOutputLine(Guid.NewGuid(), Started, "MOC", source)))
            .Select(line => line.IndexOf("MOC", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Single(offsets);
    }

    [Theory]
    [InlineData(120, "120 ms")]
    [InlineData(2500, "2.5 s")]
    public void ShortDurations_ReadInTheirOwnUnit(int milliseconds, string expected)
        => Assert.Equal(expected, RunLogFormat.Describe(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void LongDurations_ReadAsAClock()
    {
        Assert.Equal("02:30", RunLogFormat.Describe(new TimeSpan(0, 2, 30)));
        Assert.Equal("1:05:00", RunLogFormat.Describe(new TimeSpan(1, 5, 0)));
    }

    /// <summary>
    /// Nhãn cấu trúc cố ý không dịch — đó là định dạng file, grep được và không đổi nghĩa
    /// khi có người bật/tắt ngôn ngữ. Còn câu chữ thì phải theo bảng chuỗi.
    /// </summary>
    [Fact]
    public void StructuralLabels_StayTheSameInBothLanguages()
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = AppLanguage.Vietnamese;
            var vietnamese = Join(RunLogFormat.Header(Context()));

            Loc.Current = AppLanguage.English;
            var english = Join(RunLogFormat.Header(Context()));

            foreach (var label in new[] { "App", "Run id", "Program", "Working dir", "Process id", "Started" })
            {
                Assert.Contains(label + " ", vietnamese);
                Assert.Contains(label + " ", english);
            }

            // Nhưng nhãn nguồn kích hoạt thì có dịch.
            Assert.NotEqual(vietnamese, english);
        }
        finally
        {
            Loc.Current = previous;
        }
    }
}
