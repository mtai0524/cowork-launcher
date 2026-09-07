using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>Quyết định thử lại là hàm thuần — kiểm từng nhánh mà không cần tiến trình thật.</summary>
public class RetryPolicyTests
{
    private static ManagedApp App(int retries = 3, int delaySeconds = 30, bool keepAlive = false) => new()
    {
        Name = "backup",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        RetryCount = retries,
        RetryDelaySeconds = delaySeconds,
        KeepAlive = keepAlive,
    };

    private static AppRunRecord Ended(RunOutcome outcome) => new() { Outcome = outcome, ExitCode = 1 };

    [Theory]
    [InlineData(RunOutcome.Failed)]
    [InlineData(RunOutcome.TimedOut)]
    [InlineData(RunOutcome.Unhealthy)]
    public void Retries_FailedAndTimedOutRuns(RunOutcome outcome)
    {
        var decision = RetryPolicy.Decide(App(), Ended(outcome), attemptsSoFar: 0);

        Assert.True(decision.ShouldRetry);
        Assert.Equal(RetryRefusal.None, decision.Refusal);
        Assert.Equal(TimeSpan.FromSeconds(30), decision.Delay);
    }

    [Theory]
    [InlineData(RunOutcome.Succeeded)]
    [InlineData(RunOutcome.Cancelled)]
    [InlineData(RunOutcome.NotStarted)]
    [InlineData(RunOutcome.Running)]
    public void DoesNotRetry_WhatIsNotAFailure(RunOutcome outcome)
    {
        // Dừng tay là ý người dùng; thiếu file thì chạy lại cũng thiếu.
        var decision = RetryPolicy.Decide(App(), Ended(outcome), attemptsSoFar: 0);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(RetryRefusal.NotAFailure, decision.Refusal);
    }

    [Fact]
    public void DoesNotRetry_WhenNoRetriesAreConfigured()
        => Assert.Equal(RetryRefusal.NotConfigured,
            RetryPolicy.Decide(App(retries: 0), Ended(RunOutcome.Failed), 0).Refusal);

    [Fact]
    public void KeepAlive_OwnsRestarts()
    {
        // Hai cơ chế cùng chạy lại một app sẽ chồng lên nhau; keep-alive thắng.
        var decision = RetryPolicy.Decide(App(keepAlive: true), Ended(RunOutcome.Failed), 0);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(RetryRefusal.KeepAliveOwnsRestarts, decision.Refusal);
        Assert.False(RetryPolicy.AppliesTo(App(keepAlive: true)));
    }

    [Fact]
    public void DoesNotRetry_WhenTheAppIsDisabled()
    {
        var app = App();
        app.Enabled = false;

        Assert.Equal(RetryRefusal.CannotRun, RetryPolicy.Decide(app, Ended(RunOutcome.Failed), 0).Refusal);
        Assert.False(RetryPolicy.AppliesTo(app));
    }

    [Fact]
    public void GivesUp_OnceTheRetriesAreUsedUp()
    {
        var decision = RetryPolicy.Decide(App(retries: 3), Ended(RunOutcome.Failed), attemptsSoFar: 3);

        Assert.False(decision.ShouldRetry);
        Assert.Equal(RetryRefusal.LimitReached, decision.Refusal);
    }

    [Fact]
    public void StillRetries_JustBelowTheLimit()
        => Assert.True(RetryPolicy.Decide(App(retries: 3), Ended(RunOutcome.Failed), attemptsSoFar: 2).ShouldRetry);

    [Fact]
    public void NegativeDelay_IsTreatedAsZero()
        => Assert.Equal(TimeSpan.Zero, RetryPolicy.Decide(App(delaySeconds: -4), Ended(RunOutcome.Failed), 0).Delay);

    [Fact]
    public void AppliesTo_RequiresRetriesWithoutKeepAlive()
    {
        Assert.True(RetryPolicy.AppliesTo(App()));
        Assert.False(RetryPolicy.AppliesTo(App(retries: 0)));
    }
}

/// <summary>Supervisor dùng timer thật, nên các test này chờ sự kiện với thời hạn ngắn.</summary>
public class RetrySupervisorTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private static ManagedApp RetryApp(int retries = 2, int delaySeconds = 0) => new()
    {
        Name = "backup",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        RetryCount = retries,
        RetryDelaySeconds = delaySeconds,
    };

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(EventTimeout));
        Assert.Same(task, finished);
        return await task;
    }

    private static (RetrySupervisor Supervisor, FakeProcessManager Processes) Build(ManagedApp app)
    {
        var processes = new FakeProcessManager();
        var source = new ListAppSource();
        source.Apps.Add(app);

        return (new RetrySupervisor(processes, source, NullLogger.Instance), processes);
    }

    /// <summary>Bắn RunCompleted lỗi rồi chờ đúng một lần RetryDue.</summary>
    private static async Task<RetryDueEventArgs> FailAndWaitForRetryAsync(
        RetrySupervisor supervisor, FakeProcessManager processes, ManagedApp app, RunTrigger trigger)
    {
        var due = new TaskCompletionSource<RetryDueEventArgs>();
        void Handler(object? _, RetryDueEventArgs e) => due.TrySetResult(e);
        supervisor.RetryDue += Handler;
        try
        {
            processes.Complete(app, RunOutcome.Failed, trigger);
            return await WaitAsync(due.Task);
        }
        finally
        {
            supervisor.RetryDue -= Handler;
        }
    }

    [Fact]
    public async Task RetryDue_FiresAfterTheDelay_WithAttemptAndLimit()
    {
        var app = RetryApp(retries: 2);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var args = await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Schedule);

            Assert.Equal(app.Id, args.App.Id);
            Assert.Equal(1, args.Attempt);
            Assert.Equal(2, args.Limit);
            Assert.False(supervisor.IsWaiting(app.Id));
            Assert.Equal(1, supervisor.AttemptsFor(app.Id));
        }
    }

    [Fact]
    public async Task RetryScheduled_ReportsDelayAttemptAndLimit()
    {
        var app = RetryApp(retries: 3, delaySeconds: 1);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var scheduled = new TaskCompletionSource<RetryScheduledEventArgs>();
            supervisor.RetryScheduled += (_, e) => scheduled.TrySetResult(e);

            processes.Complete(app, RunOutcome.Failed, RunTrigger.Schedule);

            var args = await WaitAsync(scheduled.Task);
            Assert.Equal(TimeSpan.FromSeconds(1), args.Delay);
            Assert.Equal(1, args.Attempt);
            Assert.Equal(3, args.Limit);
            Assert.True(supervisor.IsWaiting(app.Id));
        }
    }

    [Fact]
    public async Task Exhausts_AfterTheConfiguredRetries_AndReportsTheCount()
    {
        var app = RetryApp(retries: 2);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var exhausted = new TaskCompletionSource<RetriesExhaustedEventArgs>();
            supervisor.RetriesExhausted += (_, e) => exhausted.TrySetResult(e);

            // Lần chạy gốc lỗi → thử lại lần 1; lần 1 lỗi → thử lại lần 2.
            Assert.Equal(1, (await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Schedule)).Attempt);
            Assert.Equal(2, (await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Retry)).Attempt);

            // Lần 2 cũng lỗi: hết lượt, báo bỏ cuộc thay vì hẹn giờ tiếp.
            processes.Complete(app, RunOutcome.Failed, RunTrigger.Retry);

            var args = await WaitAsync(exhausted.Task);
            Assert.Equal(2, args.Attempts);
            Assert.Equal(RunOutcome.Failed, args.Record.Outcome);
            Assert.False(supervisor.IsWaiting(app.Id));
            Assert.Equal(0, supervisor.AttemptsFor(app.Id));
        }
    }

    [Fact]
    public async Task AFreshRun_StartsANewChain()
    {
        var app = RetryApp(retries: 1);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var exhausted = new TaskCompletionSource<RetriesExhaustedEventArgs>();
            supervisor.RetriesExhausted += (_, e) => exhausted.TrySetResult(e);

            await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Schedule);
            processes.Complete(app, RunOutcome.Failed, RunTrigger.Retry);
            await WaitAsync(exhausted.Task);

            // Người dùng bấm Chạy: chuỗi cũ đã khép, chuỗi mới lại được thử từ đầu.
            var again = await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Manual);
            Assert.Equal(1, again.Attempt);
        }
    }

    [Fact]
    public async Task ASuccessfulRetry_ClosesTheChain()
    {
        var app = RetryApp(retries: 3);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            await FailAndWaitForRetryAsync(supervisor, processes, app, RunTrigger.Schedule);
            Assert.Equal(1, supervisor.AttemptsFor(app.Id));

            processes.Complete(app, RunOutcome.Succeeded, RunTrigger.Retry);

            Assert.Equal(0, supervisor.AttemptsFor(app.Id));
            Assert.False(supervisor.IsWaiting(app.Id));
        }
    }

    [Fact]
    public async Task Cancel_PreventsThePendingRetry()
    {
        var app = RetryApp(delaySeconds: 1);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RetryDue += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Failed, RunTrigger.Schedule);
            Assert.True(supervisor.Cancel(app.Id));
            Assert.False(supervisor.IsWaiting(app.Id));

            await Task.Delay(1500);
            Assert.False(fired);
        }
    }

    [Fact]
    public async Task TurningRetriesOffWhileWaiting_SkipsTheRetry()
    {
        var app = RetryApp(delaySeconds: 1);
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RetryDue += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Failed, RunTrigger.Schedule);
            app.RetryCount = 0;

            await Task.Delay(1500);
            Assert.False(fired);
            Assert.Equal(0, supervisor.AttemptsFor(app.Id));
        }
    }

    [Theory]
    [InlineData(RunOutcome.Cancelled)]
    [InlineData(RunOutcome.Succeeded)]
    [InlineData(RunOutcome.NotStarted)]
    public async Task NonFailures_ScheduleNothing(RunOutcome outcome)
    {
        var app = RetryApp();
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RetryScheduled += (_, _) => fired = true;
            supervisor.RetriesExhausted += (_, _) => fired = true;

            processes.Complete(app, outcome, RunTrigger.Schedule);

            await Task.Delay(300);
            Assert.False(fired);
        }
    }

    [Fact]
    public async Task KeepAliveApp_IsLeftToTheKeepAliveSupervisor()
    {
        var app = RetryApp();
        app.KeepAlive = true;
        var (supervisor, processes) = Build(app);
        using (supervisor)
        {
            var fired = false;
            supervisor.RetryScheduled += (_, _) => fired = true;

            processes.Complete(app, RunOutcome.Failed, RunTrigger.Schedule);

            await Task.Delay(300);
            Assert.False(fired);
        }
    }

    [Fact]
    public async Task Dispose_CancelsPendingRetries()
    {
        var app = RetryApp(delaySeconds: 1);
        var (supervisor, processes) = Build(app);

        var fired = false;
        supervisor.RetryDue += (_, _) => fired = true;

        processes.Complete(app, RunOutcome.Failed, RunTrigger.Schedule);
        supervisor.Dispose();

        await Task.Delay(1500);
        Assert.False(fired);
    }
}
