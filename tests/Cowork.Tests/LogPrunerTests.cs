using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

public class LogPrunerTests
{
    private static readonly DateTime Today = new(2026, 9, 5);
    private const string AppId = "3f2a1c889b0e4d7a9c112b6e5a4d0e13";

    [Fact]
    public void SelectStale_KeepsRecentFiles_AndTheOneExactlyAtTheBoundary()
    {
        var files = new[]
        {
            "cowork-20260905.log",         // hôm nay
            "cowork-20260806.log",         // đúng 30 ngày — vẫn giữ
            "cowork-20260805.log",         // 31 ngày — xoá
            $"app-{AppId}-20260904.log",   // hôm qua
            $"app-{AppId}-20260701.log",   // hai tháng — xoá
        };

        var stale = LogPruner.SelectStale(files, Today, retentionDays: 30);

        Assert.Equal(new[] { "cowork-20260805.log", $"app-{AppId}-20260701.log" }, stale);
    }

    [Fact]
    public void SelectStale_IgnoresFilesCoworkDidNotCreate()
    {
        // Thứ gì không đúng mẫu tên của Cowork thì để nguyên, dù cũ đến đâu.
        var files = new[]
        {
            "notes.log",
            "app-short-20200101.log",
            "cowork-2020.log",
            "backup-20200101.log",
            "cowork-20200101.txt",
        };

        Assert.Empty(LogPruner.SelectStale(files, Today, retentionDays: 1));
    }

    [Fact]
    public void SelectStale_ZeroRetention_MeansKeepEverything()
        => Assert.Empty(LogPruner.SelectStale(new[] { "cowork-20200101.log" }, Today, retentionDays: 0));

    [Fact]
    public void SelectStale_ReturnsTheFullPathItWasGiven()
    {
        var path = Path.Combine("C:\\", "x", "logs", "cowork-20200101.log");

        Assert.Equal(new[] { path }, LogPruner.SelectStale(new[] { path }, Today, retentionDays: 30));
    }

    [Fact]
    public void Prune_DeletesOnlyStaleLogFiles()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);

        var stale = Path.Combine(paths.LogDirectory, $"app-{AppId}-20260101.log");
        var fresh = Path.Combine(paths.LogDirectory, $"app-{AppId}-20260904.log");
        var foreign = Path.Combine(paths.LogDirectory, "ghi-chu-20200101.log");
        foreach (var file in new[] { stale, fresh, foreign })
            File.WriteAllText(file, "x");

        var removed = LogPruner.Prune(paths, Today, retentionDays: 30, NullLogger.Instance);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(foreign));
    }
}
