using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>Quyết định khởi động lại là hàm thuần — kiểm từng nhánh mà không cần tiến trình thật.</summary>
public class KeepAlivePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7));

    private static ManagedApp App(bool keepAlive = true) => new()
    {
        Name = "server",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        KeepAlive = keepAlive,
    };

    private static AppRunRecord Ended(RunOutcome outcome) => new() { Outcome = outcome, ExitCode = 1 };

    private static readonly DateTimeOffset[] NoRestarts = Array.Empty<DateTimeOffset>();

    [Theory]
    [InlineData(RunOutcome.Failed)]
    [InlineData(RunOutcome.Succeeded)]
    [InlineData(RunOutcome.TimedOut)]
    public void Restarts_WheneverTheProcessIsGoneOnItsOwn(RunOutcome outcome)
    {
        var decision = KeepAlivePolicy.Decide(App(), Ended(outcome), NoRestarts, Now);

        Assert.True(decision.ShouldRestart);
        Assert.Equal(RestartRefusal.None, decision.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.Delay);
    }

    [Fact]
    public void DoesNotRestart_WhenTheUserStoppedIt()
    {
        var decision = KeepAlivePolicy.Decide(App(), Ended(RunOutcome.Cancelled), NoRestarts, Now);

        Assert.False(decision.ShouldRestart);
        Assert.Equal(RestartRefusal.StoppedByUser, decision.Refusal);
    }

    [Fact]
    public void DoesNotRestart_WhenItNeverStarted()
    {
        // Thiếu file thì chạy lại cũng thiếu — khởi động lại chỉ tạo vòng lặp vô nghĩa.
        var decision = KeepAlivePolicy.Decide(App(), Ended(RunOutcome.NotStarted), NoRestarts, Now);

        Assert.Equal(RestartRefusal.NeverStarted, decision.Refusal);
    }

    [Fact]
    public void DoesNotRestart_WhenKeepAliveIsOff()
        => Assert.Equal(RestartRefusal.NotKeepAlive,
            KeepAlivePolicy.Decide(App(keepAlive: false), Ended(RunOutcome.Failed), NoRestarts, Now).Refusal);

    [Fact]
    public void DoesNotRestart_WhenTheAppIsDisabled()
    {
        var app = App();
        app.Enabled = false;

        Assert.Equal(RestartRefusal.CannotRun,
            KeepAlivePolicy.Decide(app, Ended(RunOutcome.Failed), NoRestarts, Now).Refusal);
    }

    [Fact]
    public void GivesUp_OnceTheHourlyLimitIsReached()
    {
        var app = App();
        app.MaxRestartsPerHour = 3;
        var recent = Enumerable.Range(1, 3).Select(i => Now.AddMinutes(-i * 5)).ToList();

        var decision = KeepAlivePolicy.Decide(app, Ended(RunOutcome.Failed), recent, Now);

        Assert.False(decision.ShouldRestart);
        Assert.Equal(RestartRefusal.LimitReached, decision.Refusal);
    }

    [Fact]
    public void OldRestarts_FallOutOfTheWindow()
    {
        var app = App();
        app.MaxRestartsPerHour = 3;

        // Ba lần cách đây hơn một giờ không còn được tính.
        var stale = Enumerable.Range(1, 3).Select(i => Now.AddMinutes(-61 - i)).ToList();

        Assert.True(KeepAlivePolicy.Decide(app, Ended(RunOutcome.Failed), stale, Now).ShouldRestart);
    }

    [Fact]
    public void ZeroLimit_MeansUnlimited()
    {
        var app = App();
        app.MaxRestartsPerHour = 0;
        var many = Enumerable.Range(1, 500).Select(i => Now.AddSeconds(-i)).ToList();

        Assert.True(KeepAlivePolicy.Decide(app, Ended(RunOutcome.Failed), many, Now).ShouldRestart);
    }

    [Fact]
    public void NegativeDelay_IsTreatedAsZero()
    {
        var app = App();
        app.RestartDelaySeconds = -7;

        Assert.Equal(TimeSpan.Zero, KeepAlivePolicy.Decide(app, Ended(RunOutcome.Failed), NoRestarts, Now).Delay);
    }
}

/// <summary>Bộ giả lập tiến trình: chỉ để bắn RunCompleted theo ý test.</summary>
internal sealed class FakeProcessManager : IProcessManager
{
    public bool IsRunning(Guid appId) => false;
    public IReadOnlyCollection<Guid> RunningAppIds => Array.Empty<Guid>();
    public StartResult Start(ManagedApp app, RunTrigger trigger) => StartResult.Ok(1);
    public Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task StopAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public IReadOnlyList<AppOutputLine> GetOutput(Guid appId) => Array.Empty<AppOutputLine>();

#pragma warning disable CS0067 // Sự kiện không dùng trong bộ giả lập.
    public event EventHandler<AppStatusChanged>? StatusChanged;
    public event EventHandler<AppOutputLine>? OutputReceived;
#pragma warning restore CS0067
    public event EventHandler<AppRunRecord>? RunCompleted;

    public void Complete(ManagedApp app, RunOutcome outcome)
        => RunCompleted?.Invoke(this, new AppRunRecord { AppId = app.Id, AppName = app.Name, Outcome = outcome, ExitCode = 1 });
}

internal sealed class ListAppSource : IAppSource
{
    public List<ManagedApp> Apps { get; } = new();

    public IReadOnlyList<ManagedApp> GetApps() => Apps.ToList();

    public void MarkScheduled(Guid appId, DateTimeOffset at)
    {
    }
}

/// <summary>
/// Supervisor dùng timer thật, nên các test này chờ sự kiện với thời hạn ngắn.
/// Đồng hồ giả giữ nguyên thời gian để mọi lần khởi động lại đều rơi vào cùng một cửa sổ đếm.
/// </summary>
public class KeepAliveSupervisorTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private static ManagedApp KeepAliveApp(int delaySeconds = 0, int limit = 10) => new()
    {
        Name = "server",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        KeepAlive = true,
        RestartDelaySeconds = delaySeconds,
        MaxRestartsPerHour = limit,
    };

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(EventTimeout));
        Assert.Same(task, finished);
        return await task;
    }

    private static (KeepAliveSupervisor Supervisor, FakeProcessManager Processes, ListAppSource Source) Build(ManagedApp app)
    {
        var processes = new FakeProcessManager();
        var source = new ListAppSource();
        source.Apps.Add(app);
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7)));

        return (new KeepAliveSupervisor(processes, source, clock, NullLogger.Instance), processes, source);
    }

    [Fact]
    public async Task RestartDue_FiresAfterTheDelay_WithTheAttemptNumber()
    {
        var app = KeepAliveApp();
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var due = new TaskCompletionSource<RestartDueEventArgs>();
            supervisor.RestartDue += (_, e) => due.TrySetResult(e);

            processes.Complete(app, RunOutcome.Failed);

            var args = await WaitAsync(due.Task);
            Assert.Equal(app.Id, args.App.Id);
            Assert.Equal(1, args.Attempt);
            Assert.False(supervisor.IsWaiting(app.Id));
            Assert.Equal(1, supervisor.RecentRestartCount(app.Id));
        }
    }

    [Fact]
    public async Task RestartScheduled_ReportsAttemptAndLimit()
    {
        var app = KeepAliveApp(delaySeconds: 1, limit: 7);
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var scheduled = new TaskCompletionSource<RestartScheduledEventArgs>();
            supervisor.RestartScheduled += (_, e) => scheduled.TrySetResult(e);

            processes.Complete(app, RunOutcome.Failed);

            var args = await WaitAsync(scheduled.Task);
            Assert.Equal(1, args.Attempt);
            Assert.Equal(7, args.Limit);
            Assert.True(supervisor.IsWaiting(app.Id));
        }
    }

    [Fact]
    public async Task Cancel_PreventsThePendingRestart()
    {
        var app = KeepAliveApp(delaySeconds: 1);
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RestartDue += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Failed);
            Assert.True(supervisor.Cancel(app.Id));
            Assert.False(supervisor.IsWaiting(app.Id));

            await Task.Delay(1500);
            Assert.False(fired);
        }
    }

    [Fact]
    public async Task TurningKeepAliveOffWhileWaiting_SkipsTheRestart()
    {
        var app = KeepAliveApp(delaySeconds: 1);
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RestartDue += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Failed);
            app.KeepAlive = false;

            await Task.Delay(1500);
            Assert.False(fired);
        }
    }

    [Fact]
    public async Task StoppedByUser_DoesNotScheduleAnything()
    {
        var app = KeepAliveApp();
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RestartScheduled += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Cancelled);

            await Task.Delay(300);
            Assert.False(fired);
            Assert.False(supervisor.IsWaiting(app.Id));
        }
    }

    [Fact]
    public async Task GivesUp_AfterTheLimit_AndReportsHowManyTimesItTried()
    {
        var app = KeepAliveApp(limit: 2);
        var (supervisor, processes, _) = Build(app);
        using (supervisor)
        {
            var gaveUp = new TaskCompletionSource<KeepAliveGaveUpEventArgs>();
            supervisor.GaveUp += (_, e) => gaveUp.TrySetResult(e);

            // Hai lần đầu được khởi động lại.
            for (var i = 0; i < 2; i++)
            {
                var due = new TaskCompletionSource<RestartDueEventArgs>();
                void Handler(object? _, RestartDueEventArgs e) => due.TrySetResult(e);
                supervisor.RestartDue += Handler;

                processes.Complete(app, RunOutcome.Failed);
                Assert.Equal(i + 1, (await WaitAsync(due.Task)).Attempt);

                supervisor.RestartDue -= Handler;
            }

            // Lần thứ ba chạm trần: không hẹn giờ nữa mà báo bỏ cuộc.
            processes.Complete(app, RunOutcome.Failed);

            var args = await WaitAsync(gaveUp.Task);
            Assert.Equal(2, args.Attempts);
            Assert.Equal(KeepAlivePolicy.Window, args.Window);
            Assert.False(supervisor.IsWaiting(app.Id));
        }
    }

    [Fact]
    public async Task Dispose_CancelsPendingRestarts()
    {
        var app = KeepAliveApp(delaySeconds: 1);
        var (supervisor, processes, _) = Build(app);

        var fired = false;
        supervisor.RestartDue += (_, _) => fired = true;

        processes.Complete(app, RunOutcome.Failed);
        supervisor.Dispose();

        await Task.Delay(1500);
        Assert.False(fired);
    }
}
