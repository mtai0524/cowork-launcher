using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

/// <summary>Quyết định chạy theo sự kiện là hàm thuần — kiểm khoảng lặng mà không phải chờ phút thật.</summary>
public class SystemTriggerPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7));

    private static ManagedApp App(params SystemEventKind[] triggers) => new()
    {
        Name = "sync",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        SystemTriggers = triggers.ToList(),
        SystemTriggerDelaySeconds = 15,
    };

    [Fact]
    public void Runs_ForASubscribedEvent()
    {
        var decision = SystemTriggerPolicy.Decide(
            App(SystemEventKind.Resume), SystemEventKind.Resume, null, isRunning: false, Now);

        Assert.True(decision.ShouldRun);
        Assert.Equal(TimeSpan.FromSeconds(15), decision.Delay);
    }

    [Fact]
    public void Ignores_AnEventTheAppDidNotAskFor()
        => Assert.Equal(SystemTriggerRefusal.NotSubscribed,
            SystemTriggerPolicy.Decide(App(SystemEventKind.Resume), SystemEventKind.NetworkAvailable, null, false, Now).Refusal);

    [Fact]
    public void DoesNotRun_WhenTheAppIsDisabled()
    {
        var app = App(SystemEventKind.Resume);
        app.Enabled = false;

        Assert.Equal(SystemTriggerRefusal.CannotRun,
            SystemTriggerPolicy.Decide(app, SystemEventKind.Resume, null, false, Now).Refusal);
    }

    [Fact]
    public void DoesNotRun_WhenItIsAlreadyRunningAndSingleInstance()
    {
        var app = App(SystemEventKind.Resume);
        app.SingleInstance = true;

        Assert.Equal(SystemTriggerRefusal.AlreadyRunning,
            SystemTriggerPolicy.Decide(app, SystemEventKind.Resume, null, isRunning: true, Now).Refusal);
    }

    [Fact]
    public void RunsAlongsideItself_WhenSingleInstanceIsOff()
    {
        var app = App(SystemEventKind.Resume);
        app.SingleInstance = false;

        Assert.True(SystemTriggerPolicy.Decide(app, SystemEventKind.Resume, null, isRunning: true, Now).ShouldRun);
    }

    [Fact]
    public void Cooldown_SwallowsRepeatsOfTheSameEvent()
    {
        // Windows bắn Resume vài lần cho một lần mở nắp máy.
        var app = App(SystemEventKind.Resume);
        var justNow = Now.AddSeconds(-5);

        Assert.Equal(SystemTriggerRefusal.TooSoon,
            SystemTriggerPolicy.Decide(app, SystemEventKind.Resume, justNow, false, Now).Refusal);
    }

    [Fact]
    public void Cooldown_ExpiresAfterAMinute()
    {
        var app = App(SystemEventKind.Resume);

        Assert.True(SystemTriggerPolicy
            .Decide(app, SystemEventKind.Resume, Now - SystemTriggerPolicy.Cooldown, false, Now).ShouldRun);
    }

    [Fact]
    public void NegativeDelay_IsTreatedAsZero()
    {
        var app = App(SystemEventKind.Resume);
        app.SystemTriggerDelaySeconds = -3;

        Assert.Equal(TimeSpan.Zero,
            SystemTriggerPolicy.Decide(app, SystemEventKind.Resume, null, false, Now).Delay);
    }

    [Fact]
    public void AppliesTo_NeedsAtLeastOneTrigger()
    {
        Assert.False(SystemTriggerPolicy.AppliesTo(App()));
        Assert.True(SystemTriggerPolicy.AppliesTo(App(SystemEventKind.SessionUnlock)));
    }
}

/// <summary>Supervisor dùng timer thật, nên các test này đặt độ trễ 0 và chờ sự kiện với thời hạn ngắn.</summary>
public class SystemTriggerSupervisorTests
{
    // Thời hạn rộng tay: chờ kết thúc ngay khi sự kiện tới, nên số này chỉ là trần cho lúc
    // máy đang tải nặng — để chặt quá thì test đỏ vì máy bận chứ không phải vì code sai.
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);

    private sealed class Harness : IDisposable
    {
        public Harness(ManagedApp app)
        {
            Apps.Apps.Add(app);
            Supervisor = new SystemTriggerSupervisor(Source, Apps, Processes, Clock, NullLogger.Instance);
            Supervisor.Due += (_, e) =>
            {
                lock (Fired)
                    Fired.Add(e);
                Next.TrySetResult(e);
            };
        }

        public NullSystemEventSource Source { get; } = new();
        public ListAppSource Apps { get; } = new();
        public FakeProcessManager Processes { get; } = new();
        public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7)));
        public SystemTriggerSupervisor Supervisor { get; }
        public List<SystemTriggerDueEventArgs> Fired { get; } = new();

        public TaskCompletionSource<SystemTriggerDueEventArgs> Next { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SystemTriggerDueEventArgs> Expect()
        {
            Next = new TaskCompletionSource<SystemTriggerDueEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            return Next.Task;
        }

        public void Dispose()
        {
            Supervisor.Dispose();
            Source.Dispose();
        }
    }

    private static ManagedApp App(int delaySeconds = 0, params SystemEventKind[] triggers) => new()
    {
        Name = "sync",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        SystemTriggers = triggers.ToList(),
        SystemTriggerDelaySeconds = delaySeconds,
    };

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(EventTimeout));
        Assert.Same(task, finished);
        return await task;
    }

    [Fact]
    public async Task RaisesDue_ForASubscribedEvent()
    {
        var app = App(0, SystemEventKind.Resume);
        using var h = new Harness(app);

        var expected = h.Expect();
        h.Source.Raise(SystemEventKind.Resume);

        var args = await WaitAsync(expected);
        Assert.Equal(app.Id, args.App.Id);
        Assert.Equal(SystemEventKind.Resume, args.Kind);
    }

    [Fact]
    public async Task IgnoresEvents_TheAppDidNotSubscribeTo()
    {
        using var h = new Harness(App(0, SystemEventKind.Resume));

        h.Source.Raise(SystemEventKind.NetworkAvailable);
        h.Source.Raise(SystemEventKind.SessionUnlock);
        await Task.Delay(300);

        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task SwallowsARepeatOfTheSameEvent_WithinTheCooldown()
    {
        using var h = new Harness(App(0, SystemEventKind.Resume));

        h.Source.Raise(SystemEventKind.Resume);
        await WaitAsync(h.Next.Task);

        // Windows bắn thêm vài lần nữa cho cùng một lần thức dậy.
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        h.Source.Raise(SystemEventKind.Resume);
        h.Source.Raise(SystemEventKind.Resume);
        await Task.Delay(300);

        Assert.Single(h.Fired);
    }

    [Fact]
    public async Task RunsAgain_OnceTheCooldownHasPassed()
    {
        using var h = new Harness(App(0, SystemEventKind.Resume));

        h.Source.Raise(SystemEventKind.Resume);
        await WaitAsync(h.Next.Task);

        h.Clock.Advance(SystemTriggerPolicy.Cooldown);
        var again = h.Expect();
        h.Source.Raise(SystemEventKind.Resume);

        await WaitAsync(again);
        Assert.Equal(2, h.Fired.Count);
    }

    [Fact]
    public async Task DifferentEvents_HaveTheirOwnCooldown()
    {
        using var h = new Harness(App(0, SystemEventKind.Resume, SystemEventKind.NetworkAvailable));

        h.Source.Raise(SystemEventKind.Resume);
        await WaitAsync(h.Next.Task);

        var network = h.Expect();
        h.Source.Raise(SystemEventKind.NetworkAvailable);

        // Mở máy rồi mạng lên là hai chuyện khác nhau; khoảng lặng của cái này không chặn cái kia.
        Assert.Equal(SystemEventKind.NetworkAvailable, (await WaitAsync(network)).Kind);
    }

    [Fact]
    public async Task WaitsTheConfiguredDelay_BeforeRunning()
    {
        using var h = new Harness(App(1, SystemEventKind.Resume));

        h.Source.Raise(SystemEventKind.Resume);
        Assert.True(h.Supervisor.IsWaiting(h.Apps.Apps[0].Id, SystemEventKind.Resume));
        Assert.Empty(h.Fired);

        await WaitAsync(h.Next.Task);
        Assert.False(h.Supervisor.IsWaiting(h.Apps.Apps[0].Id, SystemEventKind.Resume));
    }

    [Fact]
    public async Task UnsubscribingWhileWaiting_SkipsTheRun()
    {
        var app = App(1, SystemEventKind.Resume);
        using var h = new Harness(app);

        h.Source.Raise(SystemEventKind.Resume);
        app.SystemTriggers.Clear();

        await Task.Delay(1500);
        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task DisablingTheAppWhileWaiting_SkipsTheRun()
    {
        var app = App(1, SystemEventKind.Resume);
        using var h = new Harness(app);

        h.Source.Raise(SystemEventKind.Resume);
        app.Enabled = false;

        await Task.Delay(1500);
        Assert.Empty(h.Fired);
    }

    [Fact]
    public async Task Dispose_CancelsWhatIsPending()
    {
        var h = new Harness(App(1, SystemEventKind.Resume));

        h.Source.Raise(SystemEventKind.Resume);
        h.Dispose();

        await Task.Delay(1500);
        Assert.Empty(h.Fired);
    }
}

/// <summary>Trường sự kiện hệ thống phải đi hết vòng lưu → nạp, Clone, kiểm tra.</summary>
public class SystemTriggerSettingsTests
{
    [Fact]
    public void SystemTriggers_RoundTripThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(new ManagedApp
        {
            Name = "sync",
            ExecutablePath = "sync.exe",
            SystemTriggers = new List<SystemEventKind> { SystemEventKind.Resume, SystemEventKind.NetworkAvailable },
            SystemTriggerDelaySeconds = 45,
        });

        store.Save(workspace);
        var app = Assert.Single(store.Load().Apps);

        Assert.Equal(new[] { SystemEventKind.Resume, SystemEventKind.NetworkAvailable }, app.SystemTriggers);
        Assert.Equal(45, app.SystemTriggerDelaySeconds);
    }

    [Fact]
    public void OlderWorkspace_WithoutTriggers_GetsSafeDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Version": 1, "Apps": [ { "Name": "cu", "ExecutablePath": "a.exe" } ], "Settings": { } }
            """);

        var app = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps);

        Assert.Empty(app.SystemTriggers);
        Assert.Equal(15, app.SystemTriggerDelaySeconds);
    }

    [Fact]
    public void DuplicateTriggers_AreCollapsedOnLoad()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Apps": [ { "Name": "x", "ExecutablePath": "a.exe",
                          "SystemTriggers": [ "Resume", "Resume", "SessionUnlock" ] } ] }
            """);

        var app = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps);

        Assert.Equal(new[] { SystemEventKind.Resume, SystemEventKind.SessionUnlock }, app.SystemTriggers);
    }

    [Fact]
    public void Clone_CopiesTriggers_WithoutSharingTheList()
    {
        var original = new ManagedApp
        {
            SystemTriggers = new List<SystemEventKind> { SystemEventKind.Resume },
            SystemTriggerDelaySeconds = 20,
        };

        var copy = original.Clone();
        copy.SystemTriggers.Add(SystemEventKind.SessionUnlock);

        Assert.Equal(20, copy.SystemTriggerDelaySeconds);
        Assert.Equal(new[] { SystemEventKind.Resume }, original.SystemTriggers);
    }

    [Fact]
    public void Validator_RejectsANegativeDelay()
    {
        var app = new ManagedApp { Name = "x", ExecutablePath = "x.exe", SystemTriggerDelaySeconds = -1 };

        Assert.Contains(AppValidator.Validate(app),
            i => i.Field == nameof(ManagedApp.SystemTriggerDelaySeconds) && i.IsError);
    }
}
