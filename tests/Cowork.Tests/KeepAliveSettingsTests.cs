using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

/// <summary>Các trường mới phải đi hết vòng lưu → nạp, Clone, và kiểm tra hợp lệ.</summary>
public class KeepAliveSettingsTests
{
    [Fact]
    public void KeepAliveFields_RoundTripThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(new ManagedApp
        {
            Name = "server",
            ExecutablePath = "server.exe",
            KeepAlive = true,
            RestartDelaySeconds = 12,
            MaxRestartsPerHour = 3,
        });
        workspace.Settings.NotifyOnFailure = false;

        store.Save(workspace);
        var loaded = store.Load();

        var app = Assert.Single(loaded.Apps);
        Assert.True(app.KeepAlive);
        Assert.Equal(12, app.RestartDelaySeconds);
        Assert.Equal(3, app.MaxRestartsPerHour);
        Assert.False(loaded.Settings.NotifyOnFailure);
    }

    [Fact]
    public void OlderWorkspace_WithoutTheNewFields_GetsSafeDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Version": 1, "Apps": [ { "Name": "cu", "ExecutablePath": "a.exe" } ], "Settings": { } }
            """);

        var loaded = new JsonWorkspaceStore(paths, NullLogger.Instance).Load();

        var app = Assert.Single(loaded.Apps);
        Assert.False(app.KeepAlive);
        Assert.Equal(5, app.RestartDelaySeconds);
        Assert.Equal(10, app.MaxRestartsPerHour);
        Assert.True(loaded.Settings.NotifyOnFailure);
    }

    [Fact]
    public void Clone_CopiesKeepAliveFields()
    {
        var original = new ManagedApp { KeepAlive = true, RestartDelaySeconds = 30, MaxRestartsPerHour = 0 };

        var copy = original.Clone();

        Assert.True(copy.KeepAlive);
        Assert.Equal(30, copy.RestartDelaySeconds);
        Assert.Equal(0, copy.MaxRestartsPerHour);
    }

    [Fact]
    public void Validator_RejectsNegativeRestartSettings()
    {
        var app = new ManagedApp
        {
            Name = "x",
            ExecutablePath = "x.exe",
            RestartDelaySeconds = -1,
            MaxRestartsPerHour = -5,
        };

        var issues = AppValidator.Validate(app);

        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.RestartDelaySeconds) && i.IsError);
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.MaxRestartsPerHour) && i.IsError);
    }

    [Fact]
    public void Validator_AcceptsZeroAsUnlimited()
    {
        var app = new ManagedApp { Name = "x", ExecutablePath = "x.exe", MaxRestartsPerHour = 0, RestartDelaySeconds = 0 };

        Assert.DoesNotContain(AppValidator.Validate(app), i => i.Field is nameof(ManagedApp.RestartDelaySeconds)
                                                                      or nameof(ManagedApp.MaxRestartsPerHour));
    }
}
