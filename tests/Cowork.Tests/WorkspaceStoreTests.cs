using Cowork.Core.Configuration;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

/// <summary>Thư mục tạm tự dọn, để test không đụng vào %APPDATA% thật.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cowork-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class JsonWorkspaceStoreTests
{
    [Fact]
    public void RoundTrips_AppWithScheduleEnvAndConfigFiles()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var store = new JsonWorkspaceStore(paths, NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        var app = new ManagedApp
        {
            Name = "Sao luu CSDL",
            Group = "Backup",
            ExecutablePath = @"C:\tools\backup.bat",
            Arguments = "--full",
            WorkingDirectory = @"C:\tools",
            TimeoutMinutes = 45,
            WindowStyle = AppWindowStyle.Hidden,
        };
        app.EnvironmentVariables["DB_HOST"] = "10.0.0.1";
        app.ConfigFiles.Add(new ConfigFileRef { Path = "config.ini", Format = ConfigFormat.Ini });
        app.Schedule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(2, 15, 0) },
            DaysOfWeek = { DayOfWeek.Monday, DayOfWeek.Friday },
            WindowStart = new TimeSpan(1, 0, 0),
            CatchUpMissedRun = true,
        };
        workspace.Apps.Add(app);

        store.Save(workspace);
        var loaded = store.Load();

        var reloaded = Assert.Single(loaded.Apps);
        Assert.Equal("Sao luu CSDL", reloaded.Name);
        Assert.Equal(AppWindowStyle.Hidden, reloaded.WindowStyle);
        Assert.Equal(45, reloaded.TimeoutMinutes);
        Assert.Equal("10.0.0.1", reloaded.EnvironmentVariables["DB_HOST"]);
        Assert.Equal(ConfigFormat.Ini, Assert.Single(reloaded.ConfigFiles).Format);
        Assert.Equal(new TimeSpan(2, 15, 0), Assert.Single(reloaded.Schedule.Times));
        Assert.Equal(new TimeSpan(1, 0, 0), reloaded.Schedule.WindowStart);
        Assert.Equal(2, reloaded.Schedule.DaysOfWeek.Count);
        Assert.True(reloaded.Schedule.CatchUpMissedRun);
    }

    [Fact]
    public void EnvironmentVariables_AreCaseInsensitiveAfterReload()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        var app = new ManagedApp { Name = "A", ExecutablePath = "a.exe" };
        app.EnvironmentVariables["Path_Extra"] = @"C:\bin";
        workspace.Apps.Add(app);

        store.Save(workspace);

        var reloaded = store.Load().Apps[0];
        Assert.True(reloaded.EnvironmentVariables.ContainsKey("PATH_EXTRA"));
    }

    [Fact]
    public void Save_KeepsDailySnapshotOfThePreviousContent()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var store = new JsonWorkspaceStore(paths, NullLogger.Instance);

        var first = new CoworkWorkspace();
        first.Apps.Add(new ManagedApp { Name = "App dau tien", ExecutablePath = "a.exe" });
        store.Save(first);

        // Lần lưu thứ hai phải chụp lại nội dung của lần đầu trước khi ghi đè.
        var second = new CoworkWorkspace();
        second.Apps.Add(new ManagedApp { Name = "App khac han", ExecutablePath = "b.exe" });
        store.Save(second);

        var snapshot = paths.WorkspaceBackupFile(DateTime.Now);
        Assert.True(File.Exists(snapshot));
        Assert.Contains("App dau tien", File.ReadAllText(snapshot));
        Assert.Contains("App khac han", File.ReadAllText(paths.WorkspaceFile));
    }

    [Fact]
    public void Save_DoesNotOverwriteAnExistingSnapshotForToday()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var store = new JsonWorkspaceStore(paths, NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(new ManagedApp { Name = "Ban goc", ExecutablePath = "a.exe" });
        store.Save(workspace);

        // Ba lần lưu tiếp theo trong cùng ngày không được đè lên bản chụp đầu tiên.
        for (var i = 0; i < 3; i++)
        {
            workspace.Apps[0].Name = "Doi ten lan " + i;
            store.Save(workspace);
        }

        Assert.Contains("Ban goc", File.ReadAllText(paths.WorkspaceBackupFile(DateTime.Now)));
    }

    [Fact]
    public void FirstSave_OnEmptyFolder_CreatesNoSnapshot()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);

        new JsonWorkspaceStore(paths, NullLogger.Instance).Save(new CoworkWorkspace());

        // Chưa có gì để mất thì không cần bản chụp.
        Assert.Empty(Directory.GetFiles(paths.BackupDirectory, "workspace-*.json"));
    }

    [Fact]
    public void MissingFile_ReturnsEmptyWorkspace()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        Assert.Empty(store.Load().Apps);
    }

    [Fact]
    public void CorruptFile_IsQuarantinedAndWorkspaceStartsEmpty()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, "{ khong phai json");

        var store = new JsonWorkspaceStore(paths, NullLogger.Instance);
        var workspace = store.Load();

        Assert.Empty(workspace.Apps);
        Assert.False(File.Exists(paths.WorkspaceFile));
        Assert.NotEmpty(Directory.GetFiles(temp.Path, "workspace.json.corrupt-*"));
    }
}

public class ConfigFileServiceTests
{
    [Fact]
    public void SaveChanges_WritesToDiskAndCreatesBackup()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(file, "{\n  \"retry\": 3\n}");

        var service = new ConfigFileService();
        var document = service.Load(file, ConfigFormat.Auto);

        service.SaveChanges(document, new Dictionary<string, string> { ["retry"] = "7" }, backup: true);

        Assert.Contains("\"retry\": 7", File.ReadAllText(file));
        Assert.True(File.Exists(file + ".cowork.bak"));
        Assert.Contains("\"retry\": 3", File.ReadAllText(file + ".cowork.bak"));
    }

    [Fact]
    public void Load_MissingFile_ReportsErrorWithoutThrowing()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();

        var document = service.Load(Path.Combine(temp.Path, "khong-ton-tai.json"), ConfigFormat.Auto);

        Assert.NotNull(document.ParseError);
    }

    [Fact]
    public void Validate_RejectsBrokenJson()
    {
        var service = new ConfigFileService();

        Assert.NotNull(service.Validate("{ \"a\": ", ConfigFormat.Json, "x.json"));
        Assert.Null(service.Validate("{ \"a\": 1 }", ConfigFormat.Json, "x.json"));
    }

    [Theory]
    [InlineData("a.json", ConfigFormat.Json)]
    [InlineData("a.ini", ConfigFormat.Ini)]
    [InlineData("a.env", ConfigFormat.Ini)]
    [InlineData("App.config", ConfigFormat.Xml)]
    [InlineData("a.txt", ConfigFormat.PlainText)]
    public void AutoFormat_ResolvesByExtension(string fileName, ConfigFormat expected)
        => Assert.Equal(expected, ConfigFormat.Auto.Resolve(fileName));

    [Fact]
    public void SaveRaw_WritesFileWithoutLosingContent()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "notes.txt");
        File.WriteAllText(file, "cu");

        new ConfigFileService().SaveRaw(file, "moi", backup: false);

        Assert.Equal("moi", File.ReadAllText(file));
    }
}

public class AppValidatorTests
{
    [Fact]
    public void ReportsErrorWhenNameOrExecutableMissing()
    {
        var issues = AppValidator.Validate(new ManagedApp { Name = "", ExecutablePath = "" });

        Assert.True(AppValidator.HasErrors(issues));
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.Name));
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.ExecutablePath));
    }

    [Fact]
    public void MissingExecutableOnDisk_IsWarningNotError()
    {
        var issues = AppValidator.Validate(new ManagedApp
        {
            Name = "A",
            ExecutablePath = @"C:\khong\ton\tai.exe",
        });

        Assert.False(AppValidator.HasErrors(issues));
        Assert.Contains(issues, i => !i.IsError && i.Field == nameof(ManagedApp.ExecutablePath));
    }

    [Fact]
    public void DailyScheduleWithoutTimes_IsError()
    {
        var app = new ManagedApp
        {
            Name = "A",
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Schedule = new ScheduleRule { Enabled = true, Kind = ScheduleKind.DailyAtTimes },
        };

        Assert.True(AppValidator.HasErrors(AppValidator.Validate(app)));
    }

    [Fact]
    public void IntervalWindow_MustStartBeforeItEnds()
    {
        var app = new ManagedApp
        {
            Name = "A",
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Schedule = new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleKind.Interval,
                Interval = TimeSpan.FromMinutes(30),
                WindowStart = new TimeSpan(18, 0, 0),
                WindowEnd = new TimeSpan(8, 0, 0),
            },
        };

        Assert.True(AppValidator.HasErrors(AppValidator.Validate(app)));
    }

    [Fact]
    public void ValidApp_HasNoErrors()
    {
        var app = new ManagedApp
        {
            Name = "Chay cmd",
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            CaptureOutput = true,
            Schedule = new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleKind.DailyAtTimes,
                Times = { new TimeSpan(9, 0, 0) },
            },
        };

        Assert.False(AppValidator.HasErrors(AppValidator.Validate(app)));
    }
}

public class RunHistoryStoreTests
{
    [Fact]
    public void AddAndReload_KeepsRecords()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        var appId = Guid.NewGuid();

        var store = new JsonRunHistoryStore(paths, NullLogger.Instance);
        store.Add(new AppRunRecord
        {
            AppId = appId,
            AppName = "A",
            Outcome = RunOutcome.Succeeded,
            ExitCode = 0,
        });

        var reloaded = new JsonRunHistoryStore(paths, NullLogger.Instance);
        Assert.Single(reloaded.ForApp(appId));
    }

    [Fact]
    public void Add_WithSameId_UpdatesInsteadOfDuplicating()
    {
        using var temp = new TempDirectory();
        var store = new JsonRunHistoryStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var record = new AppRunRecord { AppName = "A", Outcome = RunOutcome.Running };
        store.Add(record);

        record.Outcome = RunOutcome.Succeeded;
        store.Add(record);

        Assert.Equal(RunOutcome.Succeeded, Assert.Single(store.All()).Outcome);
    }

    [Fact]
    public void Prune_DropsRecordsOlderThanRetention()
    {
        using var temp = new TempDirectory();
        var store = new JsonRunHistoryStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        store.Add(new AppRunRecord { AppName = "cu", StartedAt = DateTimeOffset.Now.AddDays(-40) });
        store.Add(new AppRunRecord { AppName = "moi", StartedAt = DateTimeOffset.Now });

        store.Prune(retentionDays: 30);

        Assert.Equal("moi", Assert.Single(store.All()).AppName);
    }
}
