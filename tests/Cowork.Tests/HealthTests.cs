using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Core.Validation;
using Xunit;

namespace Cowork.Tests;

/// <summary>Quyết định kiểm tra sức khoẻ là hàm thuần — kiểm từng ngưỡng với đồng hồ cố định.</summary>
public class HealthPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void StartupGrace_CoversTheFirstSeconds()
    {
        var check = new HealthCheck { StartupGraceSeconds = 30 };

        Assert.True(HealthPolicy.InStartupGrace(check, Now.AddSeconds(-10), Now));
        Assert.False(HealthPolicy.InStartupGrace(check, Now.AddSeconds(-30), Now));
    }

    [Fact]
    public void Silence_CountsFromTheLastOutput()
    {
        var check = new HealthCheck { SilenceMinutes = 5 };

        Assert.False(HealthPolicy.IsSilentTooLong(check, Now.AddMinutes(-4), Now));
        Assert.True(HealthPolicy.IsSilentTooLong(check, Now.AddMinutes(-5), Now));
    }

    [Fact]
    public void Silence_IsOffAtZero()
        => Assert.False(HealthPolicy.IsSilentTooLong(new HealthCheck { SilenceMinutes = 0 }, Now.AddDays(-1), Now));

    [Fact]
    public void Threshold_TreatsZeroAsOne()
    {
        Assert.True(HealthPolicy.ReachedFailureThreshold(new HealthCheck { FailureThreshold = 0 }, 1));
        Assert.False(HealthPolicy.ReachedFailureThreshold(new HealthCheck { FailureThreshold = 3 }, 2));
        Assert.True(HealthPolicy.ReachedFailureThreshold(new HealthCheck { FailureThreshold = 3 }, 3));
    }

    [Fact]
    public void CheckInterval_NeverGoesBelowTheFloor()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), HealthPolicy.CheckInterval(new HealthCheck { IntervalSeconds = 0 }));
        Assert.Equal(TimeSpan.FromSeconds(45), HealthPolicy.CheckInterval(new HealthCheck { IntervalSeconds = 45 }));
        Assert.Equal(TimeSpan.FromMilliseconds(50),
            HealthPolicy.CheckInterval(new HealthCheck { IntervalSeconds = 0 }, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void ProbeTimeout_IsAtLeastOneSecond()
        => Assert.Equal(TimeSpan.FromSeconds(1), HealthPolicy.ProbeTimeout(new HealthCheck { TimeoutSeconds = 0 }));

    [Fact]
    public void OnTick_WaitsDuringStartupGrace_EvenWhenSilent()
    {
        var check = new HealthCheck
        {
            StartupGraceSeconds = 60, SilenceMinutes = 1, Probe = HealthProbeKind.TcpPort, Target = "8080",
        };

        var decision = HealthPolicy.OnTick(check, outputObservable: true,
            startedAt: Now.AddSeconds(-30), lastOutputAt: Now.AddMinutes(-5), now: Now);

        Assert.Equal(HealthTickAction.Wait, decision.Action);
    }

    [Fact]
    public void OnTick_TerminatesForSilence_BeforeProbing()
    {
        var check = new HealthCheck
        {
            StartupGraceSeconds = 0, SilenceMinutes = 1, Probe = HealthProbeKind.TcpPort, Target = "8080",
        };

        var decision = HealthPolicy.OnTick(check, true, Now.AddMinutes(-10), Now.AddMinutes(-2), Now);

        Assert.Equal(HealthTickAction.Terminate, decision.Action);
        Assert.Equal(HealthFailure.Silence, decision.Failure);
    }

    [Fact]
    public void OnTick_IgnoresSilence_WhenOutputIsNotObservable()
    {
        var check = new HealthCheck
        {
            StartupGraceSeconds = 0, SilenceMinutes = 1, Probe = HealthProbeKind.TcpPort, Target = "8080",
        };

        var decision = HealthPolicy.OnTick(check, outputObservable: false, Now.AddMinutes(-10), Now.AddMinutes(-2), Now);

        Assert.Equal(HealthTickAction.Probe, decision.Action);
    }

    [Fact]
    public void OnTick_WaitsWhenThereIsNothingToProbe()
    {
        var check = new HealthCheck { StartupGraceSeconds = 0, SilenceMinutes = 1 };

        Assert.Equal(HealthTickAction.Wait, HealthPolicy.OnTick(check, true, Now.AddMinutes(-10), Now, Now).Action);
    }

    [Fact]
    public void AppliesTo_RequiresSomethingObservable()
    {
        var app = new ManagedApp { ExecutablePath = "x.exe" };
        Assert.False(HealthPolicy.AppliesTo(app));

        app.HealthCheck.SilenceMinutes = 5;
        Assert.True(HealthPolicy.AppliesTo(app));

        // Không thu output thì watchdog theo output mù — không còn gì để theo dõi.
        app.CaptureOutput = false;
        Assert.False(HealthPolicy.AppliesTo(app));

        app.HealthCheck.Probe = HealthProbeKind.HttpGet;
        Assert.True(HealthPolicy.AppliesTo(app));
    }

    [Fact]
    public void OutputIsObservable_NeedsCaptureWithoutElevation()
    {
        Assert.True(HealthPolicy.OutputIsObservable(new ManagedApp { CaptureOutput = true }));
        Assert.False(HealthPolicy.OutputIsObservable(new ManagedApp { CaptureOutput = true, RunAsAdministrator = true }));
        Assert.False(HealthPolicy.OutputIsObservable(new ManagedApp { CaptureOutput = false }));
    }
}

public class HealthTargetTests
{
    [Theory]
    [InlineData("8080", "localhost", 8080)]
    [InlineData(":8080", "localhost", 8080)]
    [InlineData("db.local:5432", "db.local", 5432)]
    [InlineData("[::1]:6379", "::1", 6379)]
    [InlineData(" 127.0.0.1:80 ", "127.0.0.1", 80)]
    public void Tcp_AcceptsPortAndHostPort(string target, string host, int port)
    {
        Assert.True(HealthTarget.TryParseTcp(target, out var parsedHost, out var parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("localhost:0")]
    [InlineData("localhost:70000")]
    [InlineData("http://localhost:8080")]
    public void Tcp_RejectsWhatIsNotHostAndPort(string target)
        => Assert.False(HealthTarget.TryParseTcp(target, out _, out _));

    [Theory]
    [InlineData("http://localhost:8080/health", "http")]
    [InlineData("https://example.com/", "https")]
    [InlineData("localhost:8080/health", "http")]
    public void Http_AcceptsUrls_AndAssumesHttpWithoutScheme(string target, string scheme)
    {
        Assert.True(HealthTarget.TryParseHttp(target, out var uri));
        Assert.Equal(scheme, uri.Scheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://x")]
    [InlineData("http://")]
    // Cổng gõ nhầm vào ô HTTP: "http://8080" hợp lệ với Uri nhưng chắc chắn không phải ý người dùng.
    [InlineData("8080")]
    public void Http_RejectsOtherSchemesEmptyHostsAndBarePorts(string target)
        => Assert.False(HealthTarget.TryParseHttp(target, out _));

    [Fact]
    public void Http_StillAcceptsBareIpAddresses()
        => Assert.True(HealthTarget.TryParseHttp("127.0.0.1:8080/health", out _));

    [Fact]
    public void IsValid_FollowsTheProbeKind()
    {
        Assert.True(HealthTarget.IsValid(HealthProbeKind.None, string.Empty));
        Assert.True(HealthTarget.IsValid(HealthProbeKind.TcpPort, "8080"));
        Assert.False(HealthTarget.IsValid(HealthProbeKind.TcpPort, "http://x"));
        Assert.True(HealthTarget.IsValid(HealthProbeKind.HttpGet, "http://x"));
    }
}

public class FailurePatternSetTests
{
    [Fact]
    public void Matches_CaseInsensitively()
    {
        var set = new FailurePatternSet(new[] { "fatal" });

        Assert.Equal("fatal", set.Match("2026 FATAL: boom"));
        Assert.Null(set.Match("all good"));
    }

    [Fact]
    public void InvalidRegex_IsMatchedAsPlainText()
    {
        Assert.False(FailurePatternSet.IsValidRegex("error("));

        var set = new FailurePatternSet(new[] { "error(" });
        Assert.Equal("error(", set.Match("an error( happened"));
    }

    [Fact]
    public void BlankPatterns_AreIgnored()
        => Assert.Equal(0, new FailurePatternSet(new[] { " ", string.Empty }).Count);

    [Fact]
    public void ReturnsTheFirstMatchingPattern()
    {
        var set = new FailurePatternSet(new[] { "timeout", @"out\s+of\s+memory" });

        Assert.Equal(@"out\s+of\s+memory", set.Match("Out Of Memory while allocating"));
    }
}

/// <summary>
/// Bộ theo dõi dùng timer thật nên các test này hạ sàn chu kỳ xuống vài chục mili giây; thời gian
/// trong quyết định (ân hạn, im lặng) vẫn đi qua đồng hồ giả để không phải chờ phút thật.
/// </summary>
public class HealthMonitorTests
{
    // Thời hạn rộng tay: chờ kết thúc ngay khi sự kiện tới, nên số này chỉ là trần cho lúc
    // máy đang tải nặng — để chặt quá thì test đỏ vì máy bận chứ không phải vì code sai.
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(40);

    private static ManagedApp App(Action<HealthCheck>? configure = null)
    {
        var app = new ManagedApp
        {
            Name = "server",
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            StopGraceSeconds = 2,
        };

        app.HealthCheck.StartupGraceSeconds = 0;
        app.HealthCheck.IntervalSeconds = 0; // bị kéo lên sàn 40ms của test
        configure?.Invoke(app.HealthCheck);
        return app;
    }

    private sealed class FakeProbe : IHealthProbe
    {
        private int _calls;

        public Func<string?> Next { get; set; } = () => null;

        public int Calls => Volatile.Read(ref _calls);

        public Task<string?> ProbeAsync(HealthProbeKind kind, string target, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Next());
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness(ManagedApp app)
        {
            Source.Apps.Add(app);
            Monitor = new HealthMonitor(Processes, Source, Clock, NullLogger.Instance, Probe, Tick);
            Monitor.Unhealthy += (_, e) => Unhealthy.TrySetResult(e);
        }

        public FakeProcessManager Processes { get; } = new();
        public ListAppSource Source { get; } = new();
        public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(7)));
        public FakeProbe Probe { get; } = new();
        public HealthMonitor Monitor { get; }

        public TaskCompletionSource<HealthFailedEventArgs> Unhealthy { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Monitor.Dispose();
    }

    private static async Task<T> WaitAsync<T>(Task<T> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(EventTimeout));
        Assert.Same(task, finished);
        return await task;
    }

    [Fact]
    public async Task ProbeFailures_ReachingTheThreshold_TerminateTheApp()
    {
        var app = App(h =>
        {
            h.Probe = HealthProbeKind.TcpPort;
            h.Target = "8080";
            h.FailureThreshold = 2;
        });
        using var harness = new Harness(app);
        harness.Probe.Next = () => "connection refused";

        harness.Processes.Started(app, processId: 42);
        Assert.True(harness.Monitor.IsWatching(app.Id));

        var args = await WaitAsync(harness.Unhealthy.Task);
        Assert.Equal(app.Id, args.App.Id);
        Assert.Equal(HealthFailure.ProbeFailed, args.Failure);
        Assert.Contains("8080", args.Reason);
        Assert.Contains("connection refused", args.Reason);

        var termination = Assert.Single(harness.Processes.Terminations);
        Assert.Equal(app.Id, termination.AppId);
        Assert.Equal(RunOutcome.Unhealthy, termination.Outcome);
        Assert.Equal(args.Reason, termination.Reason);
        Assert.Equal(2000, termination.GraceMs);
        Assert.False(harness.Monitor.IsWatching(app.Id));
    }

    [Fact]
    public async Task ASuccessfulProbe_ResetsTheFailureCount()
    {
        var app = App(h =>
        {
            h.Probe = HealthProbeKind.HttpGet;
            h.Target = "http://localhost/health";
            h.FailureThreshold = 2;
        });
        using var harness = new Harness(app);

        // Lỗi, khoẻ, lỗi, khoẻ… — không bao giờ có hai lỗi liên tiếp.
        var calls = 0;
        harness.Probe.Next = () => Interlocked.Increment(ref calls) % 2 == 1 ? "HTTP 503" : null;

        harness.Processes.Started(app, 1);
        await Task.Delay(400);

        Assert.True(harness.Probe.Calls >= 4, $"Chỉ thăm dò {harness.Probe.Calls} lần.");
        Assert.False(harness.Unhealthy.Task.IsCompleted);
        Assert.Empty(harness.Processes.Terminations);
        Assert.True(harness.Monitor.IsWatching(app.Id));
    }

    [Fact]
    public async Task Silence_TerminatesOnceTheClockPassesTheLimit()
    {
        var app = App(h => h.SilenceMinutes = 1);
        using var harness = new Harness(app);

        harness.Processes.Started(app, 1);
        await Task.Delay(150);
        Assert.False(harness.Unhealthy.Task.IsCompleted);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));

        var args = await WaitAsync(harness.Unhealthy.Task);
        Assert.Equal(HealthFailure.Silence, args.Failure);
        Assert.Equal(0, harness.Probe.Calls);
        Assert.Single(harness.Processes.Terminations);
    }

    [Fact]
    public async Task Output_ResetsTheSilenceClock()
    {
        var app = App(h => h.SilenceMinutes = 1);
        using var harness = new Harness(app);

        harness.Processes.Started(app, 1);
        harness.Clock.Advance(TimeSpan.FromSeconds(50));
        harness.Processes.Output(app, "still alive");
        harness.Clock.Advance(TimeSpan.FromSeconds(50));
        await Task.Delay(150);

        Assert.False(harness.Unhealthy.Task.IsCompleted);
        Assert.True(harness.Monitor.IsWatching(app.Id));
    }

    [Fact]
    public async Task AFailurePatternInOutput_TerminatesImmediately()
    {
        var app = App(h => h.FailurePatterns = new List<string> { "FATAL", "out of memory" });
        using var harness = new Harness(app);

        harness.Processes.Started(app, 7);
        harness.Processes.Output(app, "12:00 fatal: listener died");

        var args = await WaitAsync(harness.Unhealthy.Task);
        Assert.Equal(HealthFailure.FailurePattern, args.Failure);
        Assert.Contains("FATAL", args.Reason);
        Assert.Single(harness.Processes.Terminations);
    }

    [Fact]
    public async Task TerminatesOnlyOnce_EvenWhenManyLinesMatch()
    {
        var app = App(h => h.FailurePatterns = new List<string> { "FATAL" });
        using var harness = new Harness(app);

        harness.Processes.Started(app, 7);
        harness.Processes.Output(app, "FATAL 1");
        harness.Processes.Output(app, "FATAL 2");

        await WaitAsync(harness.Unhealthy.Task);
        Assert.Single(harness.Processes.Terminations);
    }

    [Fact]
    public async Task StartupGrace_DefersEveryCheck()
    {
        var app = App(h =>
        {
            h.StartupGraceSeconds = 3600;
            h.Probe = HealthProbeKind.TcpPort;
            h.Target = "1";
            h.FailureThreshold = 1;
        });
        using var harness = new Harness(app);
        harness.Probe.Next = () => "down";

        harness.Processes.Started(app, 1);
        await Task.Delay(200);
        Assert.Equal(0, harness.Probe.Calls);
        Assert.False(harness.Unhealthy.Task.IsCompleted);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await WaitAsync(harness.Unhealthy.Task);
    }

    [Fact]
    public void NothingToWatch_MeansNoWatch()
    {
        var app = App();
        using var harness = new Harness(app);

        harness.Processes.Started(app, 1);
        Assert.False(harness.Monitor.IsWatching(app.Id));

        // Watchdog theo output mà không thu output: cũng không có gì để theo dõi.
        var deaf = App(h => h.SilenceMinutes = 1);
        deaf.CaptureOutput = false;
        harness.Source.Apps.Add(deaf);

        harness.Processes.Started(deaf, 2);
        Assert.False(harness.Monitor.IsWatching(deaf.Id));
    }

    [Fact]
    public void RunCompleted_StopsWatching_OnlyForTheSameProcess()
    {
        var app = App(h => h.SilenceMinutes = 1);
        using var harness = new Harness(app);

        harness.Processes.Started(app, processId: 10);

        // Tiến trình cũ báo kết thúc muộn, sau khi tiến trình mới đã được theo dõi.
        harness.Processes.Complete(app, RunOutcome.Failed, processId: 9);
        Assert.True(harness.Monitor.IsWatching(app.Id));

        harness.Processes.Complete(app, RunOutcome.Failed, processId: 10);
        Assert.False(harness.Monitor.IsWatching(app.Id));
    }

    [Fact]
    public async Task TurningTheCheckOffWhileRunning_StopsWatching()
    {
        var app = App(h => h.SilenceMinutes = 1);
        using var harness = new Harness(app);

        harness.Processes.Started(app, 1);
        Assert.True(harness.Monitor.IsWatching(app.Id));

        app.HealthCheck.SilenceMinutes = 0;
        await Task.Delay(200);

        Assert.False(harness.Monitor.IsWatching(app.Id));
    }

    [Fact]
    public async Task Dispose_StopsChecking()
    {
        var app = App(h =>
        {
            h.Probe = HealthProbeKind.TcpPort;
            h.Target = "1";
            h.FailureThreshold = 1;
        });
        var harness = new Harness(app);
        harness.Probe.Next = () => "down";

        harness.Processes.Started(app, 1);
        harness.Monitor.Dispose();
        await Task.Delay(200);

        Assert.Equal(0, harness.Probe.Calls);
        Assert.False(harness.Unhealthy.Task.IsCompleted);
    }
}

/// <summary>Trường kiểm tra sức khoẻ phải đi hết vòng lưu → nạp, Clone, và kiểm tra hợp lệ.</summary>
public class HealthSettingsTests
{
    [Fact]
    public void HealthCheck_RoundTripsThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Apps.Add(new ManagedApp
        {
            Name = "api",
            ExecutablePath = "api.exe",
            HealthCheck = new HealthCheck
            {
                Probe = HealthProbeKind.HttpGet,
                Target = "http://localhost:5000/health",
                IntervalSeconds = 20,
                TimeoutSeconds = 3,
                FailureThreshold = 4,
                StartupGraceSeconds = 90,
                SilenceMinutes = 15,
                FailurePatterns = new List<string> { "FATAL", "OutOfMemory" },
            },
        });

        store.Save(workspace);
        var loaded = store.Load();

        var health = Assert.Single(loaded.Apps).HealthCheck;
        Assert.Equal(HealthProbeKind.HttpGet, health.Probe);
        Assert.Equal("http://localhost:5000/health", health.Target);
        Assert.Equal(20, health.IntervalSeconds);
        Assert.Equal(3, health.TimeoutSeconds);
        Assert.Equal(4, health.FailureThreshold);
        Assert.Equal(90, health.StartupGraceSeconds);
        Assert.Equal(15, health.SilenceMinutes);
        Assert.Equal(new[] { "FATAL", "OutOfMemory" }, health.FailurePatterns);
    }

    [Fact]
    public void OlderWorkspace_WithoutHealthCheck_GetsSafeDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Version": 1, "Apps": [ { "Name": "cu", "ExecutablePath": "a.exe" } ], "Settings": { } }
            """);

        var health = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps).HealthCheck;

        Assert.False(health.IsEnabled);
        Assert.Equal(HealthProbeKind.None, health.Probe);
        Assert.Equal(30, health.IntervalSeconds);
        Assert.Equal(5, health.TimeoutSeconds);
        Assert.Equal(3, health.FailureThreshold);
        Assert.Equal(30, health.StartupGraceSeconds);
        Assert.Equal(0, health.SilenceMinutes);
        Assert.Empty(health.FailurePatterns);
    }

    [Fact]
    public void HandEditedNulls_AreRepaired()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Apps": [ { "Name": "x", "ExecutablePath": "a.exe",
                          "HealthCheck": { "Probe": "TcpPort", "Target": null, "FailurePatterns": null } } ] }
            """);

        var health = Assert.Single(new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Apps).HealthCheck;

        Assert.Equal(HealthProbeKind.TcpPort, health.Probe);
        Assert.Equal(string.Empty, health.Target);
        Assert.NotNull(health.FailurePatterns);
        Assert.Empty(health.FailurePatterns);
    }

    [Fact]
    public void Clone_CopiesHealthCheck_WithoutSharingThePatternList()
    {
        var original = new ManagedApp
        {
            HealthCheck = new HealthCheck
            {
                Probe = HealthProbeKind.TcpPort,
                Target = "8080",
                SilenceMinutes = 10,
                FailurePatterns = new List<string> { "FATAL" },
            },
        };

        var copy = original.Clone();
        copy.HealthCheck.FailurePatterns.Add("PANIC");

        Assert.NotSame(original.HealthCheck, copy.HealthCheck);
        Assert.Equal(HealthProbeKind.TcpPort, copy.HealthCheck.Probe);
        Assert.Equal("8080", copy.HealthCheck.Target);
        Assert.Equal(10, copy.HealthCheck.SilenceMinutes);
        Assert.Equal(new[] { "FATAL" }, original.HealthCheck.FailurePatterns);
    }

    private static ManagedApp ValidApp() => new() { Name = "x", ExecutablePath = "x.exe" };

    private static IEnumerable<ValidationIssue> HealthIssues(ManagedApp app)
        => AppValidator.Validate(app).Where(i => i.Field == nameof(ManagedApp.HealthCheck));

    [Fact]
    public void Validator_IsQuiet_WhenHealthIsOff()
        => Assert.Empty(HealthIssues(ValidApp()));

    [Fact]
    public void Validator_RequiresAUsableTargetForProbes()
    {
        var app = ValidApp();
        app.HealthCheck.Probe = HealthProbeKind.TcpPort;

        Assert.Contains(HealthIssues(app), i => i.IsError);

        app.HealthCheck.Target = "http://not-a-port";
        Assert.Contains(HealthIssues(app), i => i.IsError);

        app.HealthCheck.Target = "8080";
        Assert.Empty(HealthIssues(app));

        app.HealthCheck.Probe = HealthProbeKind.HttpGet;
        Assert.Contains(HealthIssues(app), i => i.IsError);

        app.HealthCheck.Target = "http://localhost:8080/health";
        Assert.Empty(HealthIssues(app));
    }

    [Fact]
    public void Validator_RejectsUnusableNumbers()
    {
        var app = ValidApp();
        app.HealthCheck.Probe = HealthProbeKind.TcpPort;
        app.HealthCheck.Target = "8080";
        app.HealthCheck.IntervalSeconds = 0;
        app.HealthCheck.TimeoutSeconds = 0;
        app.HealthCheck.FailureThreshold = 0;
        app.HealthCheck.StartupGraceSeconds = -1;
        app.HealthCheck.SilenceMinutes = -1;

        var errors = HealthIssues(app).Where(i => i.IsError).ToList();

        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void Validator_WarnsWhenTheOutputWatchdogIsBlind()
    {
        var app = ValidApp();
        app.HealthCheck.SilenceMinutes = 5;
        app.CaptureOutput = false;

        var issues = HealthIssues(app).ToList();

        Assert.Contains(issues, i => !i.IsError);
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    [Fact]
    public void Validator_WarnsAboutAnInvalidRegex_ButDoesNotBlock()
    {
        var app = ValidApp();
        app.HealthCheck.FailurePatterns = new List<string> { "error(", "FATAL" };

        var issues = HealthIssues(app).ToList();

        Assert.Single(issues);
        Assert.False(issues[0].IsError);
        Assert.Contains("error(", issues[0].Message);
    }
}
