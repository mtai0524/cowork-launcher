using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

/// <summary>Các trường thử lại, mã thoát, dừng lịch sự và giữ log phải đi hết vòng lưu → nạp, Clone, kiểm tra.</summary>
public class RunOptionSettingsTests
{
    [Fact]
    public void NewFields_RoundTripThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(new ManagedApp
        {
            Name = "robocopy",
            ExecutablePath = "robocopy.exe",
            RetryCount = 3,
            RetryDelaySeconds = 45,
            SuccessExitCodes = new List<int> { 3010, 1, 0 },
            StopGraceSeconds = 20,
        });
        workspace.Settings.LogRetentionDays = 7;

        store.Save(workspace);
        var loaded = store.Load();

        var app = Assert.Single(loaded.Apps);
        Assert.Equal(3, app.RetryCount);
        Assert.Equal(45, app.RetryDelaySeconds);
        Assert.Equal(new[] { 0, 1, 3010 }, app.SuccessExitCodes);
        Assert.Equal(20, app.StopGraceSeconds);
        Assert.Equal(7, loaded.Settings.LogRetentionDays);
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
        Assert.Equal(0, app.RetryCount);
        Assert.Equal(30, app.RetryDelaySeconds);
        Assert.Equal(new[] { 0 }, app.SuccessExitCodes);
        Assert.Equal(5, app.StopGraceSeconds);
        Assert.Equal(30, loaded.Settings.LogRetentionDays);
    }

    [Fact]
    public void EmptySuccessExitCodes_FallBackToZero()
    {
        // Người dùng sửa tay thành mảng rỗng: không có mã nào là thành công thì vô nghĩa.
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Apps": [ { "Name": "x", "ExecutablePath": "a.exe", "SuccessExitCodes": [] } ] }
            """);

        var app = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps);

        Assert.Equal(new[] { 0 }, app.SuccessExitCodes);
    }

    [Fact]
    public void Clone_CopiesTheNewFields_WithoutSharingTheExitCodeList()
    {
        var original = new ManagedApp
        {
            RetryCount = 2,
            RetryDelaySeconds = 10,
            SuccessExitCodes = new List<int> { 0, 1 },
            StopGraceSeconds = 15,
        };

        var copy = original.Clone();
        copy.SuccessExitCodes.Add(7);

        Assert.Equal(2, copy.RetryCount);
        Assert.Equal(10, copy.RetryDelaySeconds);
        Assert.Equal(15, copy.StopGraceSeconds);
        Assert.Equal(new[] { 0, 1 }, original.SuccessExitCodes);
    }

    [Fact]
    public void Validator_RejectsNegativeValues()
    {
        var app = new ManagedApp
        {
            Name = "x",
            ExecutablePath = "x.exe",
            RetryCount = -1,
            RetryDelaySeconds = -2,
            StopGraceSeconds = -3,
        };

        var issues = AppValidator.Validate(app);

        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.RetryCount) && i.IsError);
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.RetryDelaySeconds) && i.IsError);
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.StopGraceSeconds) && i.IsError);
    }

    [Fact]
    public void Validator_WarnsWhenRetryAndKeepAliveAreBothOn()
    {
        var app = new ManagedApp { Name = "x", ExecutablePath = "x.exe", RetryCount = 3, KeepAlive = true };

        var issues = AppValidator.Validate(app);

        // Cảnh báo chứ không chặn lưu: người dùng vẫn có thể muốn giữ số lần thử lại cho lúc tắt keep-alive.
        Assert.Contains(issues, i => i.Field == nameof(ManagedApp.RetryCount) && !i.IsError);
        Assert.False(AppValidator.HasErrors(issues));
    }

    [Fact]
    public void Validator_AcceptsRetriesWithoutKeepAlive()
    {
        var app = new ManagedApp { Name = "x", ExecutablePath = "x.exe", RetryCount = 3, RetryDelaySeconds = 0, StopGraceSeconds = 0 };

        Assert.DoesNotContain(AppValidator.Validate(app), i => i.Field is nameof(ManagedApp.RetryCount)
                                                                      or nameof(ManagedApp.RetryDelaySeconds)
                                                                      or nameof(ManagedApp.StopGraceSeconds));
    }
}
