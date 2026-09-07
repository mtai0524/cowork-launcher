using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>Xem lại output của một lần chạy đã qua: mỗi lần chạy một file, đọc lại đúng file đó.</summary>
public class RunLogTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7));

    private static AppRunRecord Record(string? logFile) => new()
    {
        AppId = Guid.NewGuid(),
        AppName = "app",
        StartedAt = Started,
        OutputLogFile = logFile,
    };

    [Fact]
    public void FileName_StartsWithTheDay_SoThePrunerCanReadIt()
    {
        var id = Guid.Parse("3f2a1c88-9b0e-4d7a-9c11-2b6e5a4d0e13");

        Assert.Equal("run-20260905-3f2a1c889b0e4d7a9c112b6e5a4d0e13.log",
            CoworkPaths.RunLogFileName(id, Started));
    }

    [Fact]
    public void ARecordWithoutALogFile_ReadsAsMissing()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);

        var content = RunLog.Read(paths, Record(null));

        Assert.False(content.Exists);
        Assert.Empty(content.Lines);
        Assert.Null(RunLog.PathOf(paths, Record(null)));
    }

    [Fact]
    public void AMissingFile_ReadsAsMissing()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);

        Assert.False(RunLog.Read(paths, Record("run-20260905-deadbeef.log")).Exists);
    }

    [Fact]
    public void ReadsBackTheLinesThatWereWritten()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var record = Record(CoworkPaths.RunLogFileName(Guid.NewGuid(), Started));
        File.WriteAllLines(paths.RunLogFile(record.OutputLogFile!), new[] { "dong 1", "dong 2" });

        var content = RunLog.Read(paths, record);

        Assert.True(content.Exists);
        Assert.False(content.Truncated);
        Assert.Equal(new[] { "dong 1", "dong 2" }, content.Lines);
    }

    [Fact]
    public void ALongLog_KeepsTheTail_BecauseThatIsWhereTheErrorIs()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var record = Record(CoworkPaths.RunLogFileName(Guid.NewGuid(), Started));
        File.WriteAllLines(paths.RunLogFile(record.OutputLogFile!),
            Enumerable.Range(1, 50).Select(i => $"dong {i}"));

        var content = RunLog.Read(paths, record, maxLines: 10);

        Assert.True(content.Truncated);
        Assert.Equal(10, content.Lines.Count);
        Assert.Equal("dong 41", content.Lines[0]);
        Assert.Equal("dong 50", content.Lines[^1]);
    }

    [Fact]
    public void ReadsAFileThatIsStillBeingWritten()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var record = Record(CoworkPaths.RunLogFileName(Guid.NewGuid(), Started));
        var path = paths.RunLogFile(record.OutputLogFile!);
        File.WriteAllLines(path, new[] { "dang chay" });

        // App vẫn đang chạy và Cowork vẫn đang nối thêm vào file này.
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        Assert.Equal(new[] { "dang chay" }, RunLog.Read(paths, record).Lines);
    }
}

public class RunLogPruningTests
{
    private static readonly DateTime Today = new(2026, 9, 30);

    [Fact]
    public void PrunesRunLogs_ByTheDayInTheirName()
    {
        var files = new[]
        {
            @"C:\logs\run-20260901-3f2a1c889b0e4d7a9c112b6e5a4d0e13.log",
            @"C:\logs\run-20260929-3f2a1c889b0e4d7a9c112b6e5a4d0e13.log",
        };

        var stale = LogPruner.SelectStale(files, Today, retentionDays: 7);

        Assert.Equal(new[] { files[0] }, stale);
    }

    [Fact]
    public void StillPrunesTheOlderPerAppLogs()
    {
        // File sinh trước khi Cowork chuyển sang một file mỗi lần chạy vẫn phải được dọn.
        var files = new[] { @"C:\logs\app-3f2a1c889b0e4d7a9c112b6e5a4d0e13-20260901.log" };

        Assert.Equal(files, LogPruner.SelectStale(files, Today, retentionDays: 7));
    }

    [Fact]
    public void LeavesFilesItDidNotCreate()
    {
        var files = new[]
        {
            @"C:\logs\ghi-chu.log",
            @"C:\logs\run-khong-phai-ngay-3f2a.log",
            @"C:\logs\run-20260901-khong-du-hex.log",
        };

        Assert.Empty(LogPruner.SelectStale(files, Today, retentionDays: 7));
    }
}
