using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

internal static class DependencyBuilder
{
    public static ManagedApp App(string name, bool keepAlive = false, bool enabled = true) => new()
    {
        Name = name,
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        KeepAlive = keepAlive,
        Enabled = enabled,
    };

    public static ManagedApp After(this ManagedApp app, ManagedApp target, DependencyWait wait = DependencyWait.Completed)
    {
        app.DependsOn.Add(new AppDependency { AppId = target.Id, Wait = wait });
        return app;
    }

    public static IReadOnlyList<string> Names(this IEnumerable<ManagedApp> apps) => apps.Select(a => a.Name).ToList();
}

/// <summary>Thứ tự và vòng lặp là phần dễ sai nhất — kiểm bằng hàm thuần, không cần tiến trình nào.</summary>
public class DependencyGraphTests
{
    [Fact]
    public void KeepsTheUserOrder_WhenNothingDependsOnAnything()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        var c = DependencyBuilder.App("c");

        var ordered = DependencyGraph.TopologicalOrder(new[] { a, b, c }, out var cyclic);

        Assert.Equal(new[] { "a", "b", "c" }, ordered.Names());
        Assert.Empty(cyclic);
    }

    [Fact]
    public void PutsEachAppAfterWhatItWaitsFor()
    {
        var upload = DependencyBuilder.App("upload");
        var backup = DependencyBuilder.App("backup");
        var zip = DependencyBuilder.App("zip");

        upload.After(zip);
        zip.After(backup);

        var ordered = DependencyGraph.TopologicalOrder(new[] { upload, backup, zip }, out var cyclic);

        Assert.Equal(new[] { "backup", "zip", "upload" }, ordered.Names());
        Assert.Empty(cyclic);
    }

    [Fact]
    public void SetsAsideAppsThatWaitForEachOther()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        var free = DependencyBuilder.App("free");

        a.After(b);
        b.After(a);

        var ordered = DependencyGraph.TopologicalOrder(new[] { a, b, free }, out var cyclic);

        Assert.Equal(new[] { "free" }, ordered.Names());
        Assert.Equal(new[] { "a", "b" }, cyclic.Names());
        Assert.True(DependencyGraph.IsInCycle(new[] { a, b, free }, a.Id));
        Assert.False(DependencyGraph.IsInCycle(new[] { a, b, free }, free.Id));
    }

    [Fact]
    public void IgnoresSelfReferencesAndDuplicates()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");

        a.After(a).After(b).After(b);

        Assert.Single(DependencyGraph.EdgesOf(a));
        Assert.Equal(new[] { "b", "a" }, DependencyGraph.TopologicalOrder(new[] { a, b }, out _).Names());
    }

    [Fact]
    public void ADependencyOutsideTheListDoesNotBlockOrdering()
    {
        // Ai đó tắt hoặc bỏ app phụ thuộc ra khỏi lượt chạy: thứ tự vẫn tính được,
        // còn chuyện bỏ qua hay không là việc của hàng đợi.
        var a = DependencyBuilder.App("a");
        var missing = DependencyBuilder.App("missing");
        a.After(missing);

        Assert.Equal(new[] { "a" }, DependencyGraph.TopologicalOrder(new[] { a }, out var cyclic).Names());
        Assert.Empty(cyclic);
    }

    [Fact]
    public void DependentsOf_FindsDirectDependents()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b").After(a);
        var c = DependencyBuilder.App("c").After(a);
        var d = DependencyBuilder.App("d").After(b);

        Assert.Equal(new[] { "b", "c" }, DependencyGraph.DependentsOf(new[] { a, b, c, d }, a.Id).Names());
    }

    [Fact]
    public void Validate_IsQuiet_WhenTheGraphIsFine()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b").After(a);

        Assert.Empty(DependencyGraph.Validate(new[] { a, b }));
    }

    [Fact]
    public void Validate_ReportsAMissingTarget()
    {
        var a = DependencyBuilder.App("a");
        a.DependsOn.Add(new AppDependency { AppId = Guid.NewGuid() });

        var issue = Assert.Single(DependencyGraph.Validate(new[] { a }));
        Assert.True(issue.IsError);
    }

    [Fact]
    public void Validate_ErrorsOnWaitingForAKeepAliveAppToFinish()
    {
        var server = DependencyBuilder.App("server", keepAlive: true);
        var client = DependencyBuilder.App("client").After(server, DependencyWait.Completed);

        var issue = Assert.Single(DependencyGraph.Validate(new[] { server, client }));
        Assert.True(issue.IsError);
        Assert.Contains("server", issue.Message);
    }

    [Fact]
    public void Validate_AcceptsWaitingForAKeepAliveAppToBeUp()
    {
        var server = DependencyBuilder.App("server", keepAlive: true);
        var client = DependencyBuilder.App("client").After(server, DependencyWait.Running);

        Assert.Empty(DependencyGraph.Validate(new[] { server, client }));
    }

    [Fact]
    public void Validate_WarnsAboutADisabledTarget_WithoutBlocking()
    {
        var off = DependencyBuilder.App("off", enabled: false);
        var app = DependencyBuilder.App("app").After(off);

        var issues = DependencyGraph.Validate(new[] { off, app });

        Assert.Single(issues);
        Assert.False(issues[0].IsError);
        Assert.False(AppValidator.HasErrors(issues));
    }

    [Fact]
    public void Validate_ReportsACycle_NamingTheAppsInvolved()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        a.After(b);
        b.After(a);

        var issue = Assert.Single(DependencyGraph.Validate(new[] { a, b }));
        Assert.True(issue.IsError);
        Assert.Contains("a", issue.Message);
        Assert.Contains("b", issue.Message);
    }
}

/// <summary>
/// Hàng đợi chạy trên sự kiện của <see cref="IProcessManager"/>; bộ giả lập ở đây bắn các sự kiện
/// đó theo ý test, nên toàn bộ luồng kiểm được ngay lập tức, không tiến trình, không chờ.
/// </summary>
public class RunQueueTests
{
    private sealed class Harness : IDisposable
    {
        private readonly Dictionary<Guid, bool> _running = new();

        public Harness(bool startsSucceed = true) => StartsSucceed = startsSucceed;

        public QueueProcessManager Processes { get; } = new();
        public List<string> StartedOrder { get; } = new();
        public List<QueueSkippedEventArgs> Skips { get; } = new();
        public QueueFinishedEventArgs? Finished { get; private set; }
        public bool StartsSucceed { get; set; }

        /// <summary>App nào bị từ chối khởi chạy (cấu hình lỗi).</summary>
        public HashSet<Guid> RefuseToStart { get; } = new();

        public RunQueue Queue { get; private set; } = null!;

        public Harness Build()
        {
            Queue = new RunQueue(Processes, Run, NullLogger.Instance);
            Queue.Skipped += (_, e) => Skips.Add(e);
            Queue.Finished += (_, e) => Finished = e;
            return this;
        }

        private bool Run(ManagedApp app)
        {
            StartedOrder.Add(app.Name);
            if (!StartsSucceed || RefuseToStart.Contains(app.Id))
                return false;

            // ProcessManager phát "đang chạy" ngay trong lệnh khởi chạy — giữ đúng nhịp đó,
            // vì chính chỗ này là nơi hàng đợi có thể bị gọi lại lồng nhau.
            _running[app.Id] = true;
            Processes.SetRunning(app.Id, true);
            Processes.RaiseRunning(app.Id);
            return true;
        }

        public void Finish(ManagedApp app, RunOutcome outcome, RunTrigger trigger = RunTrigger.RunAll)
        {
            _running[app.Id] = false;
            Processes.SetRunning(app.Id, false);
            Processes.RaiseCompleted(app, outcome, trigger);
        }

        public void Dispose() => Queue.Dispose();
    }

    /// <summary>Bộ giả lập chỉ cần ba thứ: biết app nào đang chạy, và bắn hai sự kiện.</summary>
    internal sealed class QueueProcessManager : IProcessManager
    {
        private readonly HashSet<Guid> _running = new();

        public bool IsRunning(Guid appId) => _running.Contains(appId);

    public nint MainWindowHandle(Guid appId) => 0;
        public IReadOnlyCollection<Guid> RunningAppIds => _running.ToList();
        public StartResult Start(ManagedApp app, RunTrigger trigger) => StartResult.Ok(1);
        public Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TerminateAsync(Guid appId, RunOutcome outcome, string? reason, int graceMs = 5000, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task StopAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<AppOutputLine> GetOutput(Guid appId) => Array.Empty<AppOutputLine>();

#pragma warning disable CS0067 // Hàng đợi không nghe output.
        public event EventHandler<AppOutputLine>? OutputReceived;
#pragma warning restore CS0067
        public event EventHandler<AppStatusChanged>? StatusChanged;
        public event EventHandler<AppRunRecord>? RunCompleted;

        public void SetRunning(Guid appId, bool running)
        {
            if (running)
                _running.Add(appId);
            else
                _running.Remove(appId);
        }

        public void RaiseRunning(Guid appId)
            => StatusChanged?.Invoke(this, new AppStatusChanged(appId, AppRuntimeState.Running, 1, null));

        public void RaiseCompleted(ManagedApp app, RunOutcome outcome, RunTrigger trigger)
            => RunCompleted?.Invoke(this, new AppRunRecord
            {
                AppId = app.Id,
                AppName = app.Name,
                Outcome = outcome,
                Trigger = trigger,
                ExitCode = outcome == RunOutcome.Succeeded ? 0 : 1,
            });
    }

    [Fact]
    public void StartsEverythingAtOnce_WhenNothingDependsOnAnything()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        using var h = new Harness().Build();

        h.Queue.Start(new[] { a, b });

        Assert.Equal(new[] { "a", "b" }, h.StartedOrder);
        Assert.Empty(h.Skips);
    }

    [Fact]
    public void HoldsAnAppBack_UntilWhatItWaitsForSucceeds()
    {
        var backup = DependencyBuilder.App("backup");
        var upload = DependencyBuilder.App("upload").After(backup);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { upload, backup });

        Assert.Equal(new[] { "backup" }, h.StartedOrder);
        Assert.Equal(new[] { upload.Id }, h.Queue.Waiting);

        h.Finish(backup, RunOutcome.Succeeded);

        Assert.Equal(new[] { "backup", "upload" }, h.StartedOrder);

        h.Finish(upload, RunOutcome.Succeeded);
        Assert.Equal(new QueueFinishedEventArgs(2, 2, 0, 0), h.Finished);
    }

    [Fact]
    public void SkipsTheRest_WhenADependencyFails()
    {
        var backup = DependencyBuilder.App("backup");
        var upload = DependencyBuilder.App("upload").After(backup);
        var notify = DependencyBuilder.App("notify").After(upload);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { backup, upload, notify });
        h.Finish(backup, RunOutcome.Failed);

        // Cả chuỗi phía sau bị bỏ, không chạy vào khoảng không.
        Assert.Equal(new[] { "backup" }, h.StartedOrder);
        Assert.Equal(2, h.Skips.Count);
        Assert.All(h.Skips, s => Assert.Equal(QueueSkipReason.DependencyFailed, s.Reason));
        Assert.Equal(new QueueFinishedEventArgs(1, 0, 1, 2), h.Finished);
    }

    [Fact]
    public void RunningDependency_IsSatisfiedAsSoonAsTheServiceIsUp()
    {
        var server = DependencyBuilder.App("server", keepAlive: true);
        var client = DependencyBuilder.App("client").After(server, DependencyWait.Running);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { client, server });

        // Không phải chờ server kết thúc — nó không bao giờ kết thúc.
        Assert.Equal(new[] { "server", "client" }, h.StartedOrder);
        Assert.Empty(h.Skips);
    }

    [Fact]
    public void WaitingForAKeepAliveAppToFinish_IsRefusedInsteadOfHangingForever()
    {
        var server = DependencyBuilder.App("server", keepAlive: true);
        var client = DependencyBuilder.App("client").After(server, DependencyWait.Completed);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { server, client });

        Assert.Equal(new[] { "server" }, h.StartedOrder);
        var skip = Assert.Single(h.Skips);
        Assert.Equal(QueueSkipReason.DependencyNeverCompletes, skip.Reason);
        Assert.Equal("client", skip.App.Name);
    }

    [Fact]
    public void ADependencyOutsideTheRun_IsAcceptedOnlyWhenItIsAlreadyUp()
    {
        var outsider = DependencyBuilder.App("outsider");
        var app = DependencyBuilder.App("app").After(outsider, DependencyWait.Running);
        using var h = new Harness().Build();

        h.Processes.SetRunning(outsider.Id, true);
        h.Queue.Start(new[] { app });

        Assert.Equal(new[] { "app" }, h.StartedOrder);
        Assert.Empty(h.Skips);
    }

    [Fact]
    public void ADependencyOutsideTheRun_AndNotRunning_SkipsTheApp()
    {
        var outsider = DependencyBuilder.App("outsider");
        var app = DependencyBuilder.App("app").After(outsider);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { app });

        Assert.Empty(h.StartedOrder);
        Assert.Equal(QueueSkipReason.DependencyMissing, Assert.Single(h.Skips).Reason);
    }

    [Fact]
    public void ACycle_IsSkippedUpFront_WithoutBlockingTheOthers()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        var free = DependencyBuilder.App("free");
        a.After(b);
        b.After(a);

        using var h = new Harness().Build();
        h.Queue.Start(new[] { a, b, free });

        Assert.Equal(new[] { "free" }, h.StartedOrder);
        Assert.Equal(2, h.Skips.Count);
        Assert.All(h.Skips, s => Assert.Equal(QueueSkipReason.DependencyCycle, s.Reason));
    }

    [Fact]
    public void AnAppThatCannotStart_CountsAsAFailure_SoTheRestDoNotWaitForever()
    {
        var first = DependencyBuilder.App("first");
        var second = DependencyBuilder.App("second").After(first);
        using var h = new Harness().Build();
        h.RefuseToStart.Add(first.Id);

        h.Queue.Start(new[] { first, second });

        Assert.Equal(new[] { "first" }, h.StartedOrder);
        Assert.Equal(new[] { QueueSkipReason.CannotStart, QueueSkipReason.DependencyFailed },
            h.Skips.Select(s => s.Reason));
        Assert.False(h.Queue.IsRunning);
    }

    [Fact]
    public void AnAlreadyRunningApp_IsNotStartedAgain_ButIsStillWaitedFor()
    {
        var server = DependencyBuilder.App("server");
        var client = DependencyBuilder.App("client").After(server);
        using var h = new Harness().Build();

        h.Processes.SetRunning(server.Id, true);
        h.Queue.Start(new[] { server, client });

        // "Không chạy chồng" đã chặn lần khởi chạy thứ hai; hàng đợi chờ đúng instance đang sống.
        Assert.Empty(h.StartedOrder);
        Assert.Equal(new[] { client.Id }, h.Queue.Waiting);

        h.Finish(server, RunOutcome.Succeeded);
        Assert.Equal(new[] { "client" }, h.StartedOrder);
    }

    [Fact]
    public void WaitsForTheWholeRetryChain_BeforeCallingItAFailure()
    {
        var flaky = DependencyBuilder.App("flaky");
        flaky.RetryCount = 2;
        var after = DependencyBuilder.App("after").After(flaky);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { flaky, after });

        // Lần chạy gốc lỗi: bộ thử lại sẽ chạy lại, nên chưa kết luận gì.
        h.Finish(flaky, RunOutcome.Failed);
        Assert.Empty(h.Skips);
        Assert.Equal(new[] { after.Id }, h.Queue.Waiting);

        // Lần thử lại đầu cũng lỗi — vẫn còn một lượt nữa.
        h.Finish(flaky, RunOutcome.Failed, RunTrigger.Retry);
        Assert.Empty(h.Skips);

        // Lần thử lại cuối thành công: app phía sau được chạy.
        h.Finish(flaky, RunOutcome.Succeeded, RunTrigger.Retry);
        Assert.Equal(new[] { "flaky", "after" }, h.StartedOrder);
    }

    [Fact]
    public void GivesUp_WhenTheRetryChainIsExhausted()
    {
        var flaky = DependencyBuilder.App("flaky");
        flaky.RetryCount = 1;
        var after = DependencyBuilder.App("after").After(flaky);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { flaky, after });
        h.Finish(flaky, RunOutcome.Failed);
        h.Finish(flaky, RunOutcome.Failed, RunTrigger.Retry);

        Assert.Equal(QueueSkipReason.DependencyFailed, Assert.Single(h.Skips).Reason);
        Assert.Equal(new[] { "flaky" }, h.StartedOrder);
    }

    [Fact]
    public void Cancel_StopsStartingAnythingElse()
    {
        var first = DependencyBuilder.App("first");
        var second = DependencyBuilder.App("second").After(first);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { first, second });
        h.Queue.Cancel();
        h.Finish(first, RunOutcome.Succeeded);

        Assert.Equal(new[] { "first" }, h.StartedOrder);
        Assert.False(h.Queue.IsRunning);
    }

    [Fact]
    public void ANewRun_ReplacesTheOldOne()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b").After(a);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { a, b });
        h.Finish(a, RunOutcome.Succeeded);
        h.Finish(b, RunOutcome.Succeeded);

        h.Queue.Start(new[] { a, b });

        // Lượt mới bắt đầu lại từ đầu chứ không cộng dồn vào lượt cũ.
        Assert.Equal(new[] { "a", "b", "a" }, h.StartedOrder);
        Assert.Equal(new[] { b.Id }, h.Queue.Waiting);
    }

    [Fact]
    public void RunsALongChainInOrder()
    {
        var one = DependencyBuilder.App("1");
        var two = DependencyBuilder.App("2").After(one);
        var three = DependencyBuilder.App("3").After(two);
        var four = DependencyBuilder.App("4").After(three);
        using var h = new Harness().Build();

        // Cố tình đưa vào ngược thứ tự để chắc chắn hàng đợi tự sắp lại.
        h.Queue.Start(new[] { four, three, two, one });

        foreach (var app in new[] { one, two, three })
            h.Finish(app, RunOutcome.Succeeded);

        Assert.Equal(new[] { "1", "2", "3", "4" }, h.StartedOrder);

        h.Finish(four, RunOutcome.Succeeded);
        Assert.Equal(new QueueFinishedEventArgs(4, 4, 0, 0), h.Finished);
    }

    [Fact]
    public void OneAppCanWaitForSeveral()
    {
        var a = DependencyBuilder.App("a");
        var b = DependencyBuilder.App("b");
        var last = DependencyBuilder.App("last").After(a).After(b);
        using var h = new Harness().Build();

        h.Queue.Start(new[] { a, b, last });
        h.Finish(a, RunOutcome.Succeeded);

        Assert.DoesNotContain("last", h.StartedOrder);

        h.Finish(b, RunOutcome.Succeeded);
        Assert.Contains("last", h.StartedOrder);
    }
}

/// <summary>Trường phụ thuộc phải đi hết vòng lưu → nạp và Clone.</summary>
public class DependencySettingsTests
{
    [Fact]
    public void DependsOn_RoundTripsThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var first = DependencyBuilder.App("first");
        var second = DependencyBuilder.App("second").After(first, DependencyWait.Running);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(first);
        workspace.Apps.Add(second);
        store.Save(workspace);

        var loaded = store.Load();
        var edge = Assert.Single(loaded.Apps.Single(a => a.Name == "second").DependsOn);

        Assert.Equal(first.Id, edge.AppId);
        Assert.Equal(DependencyWait.Running, edge.Wait);
    }

    [Fact]
    public void OlderWorkspace_WithoutDependencies_LoadsWithNone()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Version": 1, "Apps": [ { "Name": "cu", "ExecutablePath": "a.exe" } ], "Settings": { } }
            """);

        var app = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps);

        Assert.NotNull(app.DependsOn);
        Assert.Empty(app.DependsOn);
    }

    [Fact]
    public void SelfReferences_AreDroppedOnLoad()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Apps": [ { "Id": "3f2a1c88-9b0e-4d7a-9c11-2b6e5a4d0e13", "Name": "x", "ExecutablePath": "a.exe",
                          "DependsOn": [ { "AppId": "3f2a1c88-9b0e-4d7a-9c11-2b6e5a4d0e13" },
                                         { "AppId": "00000000-0000-0000-0000-000000000000" } ] } ] }
            """);

        Assert.Empty(Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps).DependsOn);
    }

    [Fact]
    public void Clone_CopiesDependencies_WithoutSharingTheList()
    {
        var target = DependencyBuilder.App("target");
        var original = DependencyBuilder.App("original").After(target, DependencyWait.Running);

        var copy = original.Clone();
        copy.DependsOn.Add(new AppDependency { AppId = Guid.NewGuid() });

        Assert.Single(original.DependsOn);
        Assert.Equal(target.Id, copy.DependsOn[0].AppId);
        Assert.Equal(DependencyWait.Running, copy.DependsOn[0].Wait);
        Assert.NotSame(original.DependsOn[0], copy.DependsOn[0]);
    }
}
