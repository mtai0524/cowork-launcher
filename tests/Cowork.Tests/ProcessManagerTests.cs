using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>
/// Test tích hợp: chạy tiến trình thật (cmd.exe) để kiểm chứng luồng khởi chạy,
/// thu output và báo kết thúc — phần dễ hỏng nhất của Cowork.
/// </summary>
public class ProcessManagerTests
{
    private static readonly string Cmd =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static ManagedApp EchoApp(string message) => new()
    {
        Name = "echo",
        ExecutablePath = Cmd,
        Arguments = $"/c echo {message}",
        CaptureOutput = true,
        WindowStyle = AppWindowStyle.Hidden,
    };

    /// <summary>Chờ tiến trình kết thúc, tránh test treo vô hạn nếu có lỗi.</summary>
    private static async Task<AppRunRecord> RunAndWaitAsync(
        ProcessManager manager, ManagedApp app, RunTrigger trigger = RunTrigger.Manual)
    {
        var completion = new TaskCompletionSource<AppRunRecord>();
        void Handler(object? _, AppRunRecord record) => completion.TrySetResult(record);

        manager.RunCompleted += Handler;
        try
        {
            var start = manager.Start(app, trigger);
            Assert.True(start.Started, start.Error);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(completion.Task, finished);
            return await completion.Task;
        }
        finally
        {
            manager.RunCompleted -= Handler;
        }
    }

    [Fact]
    public async Task Start_RunsProcessAndReportsSuccess()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var record = await RunAndWaitAsync(manager, EchoApp("xin chao"));

        Assert.Equal(RunOutcome.Succeeded, record.Outcome);
        Assert.Equal(0, record.ExitCode);
        Assert.Equal(RunTrigger.Manual, record.Trigger);
        Assert.NotNull(record.FinishedAt);
    }

    [Fact]
    public async Task Start_CapturesStandardOutput()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var lines = new List<AppOutputLine>();
        manager.OutputReceived += (_, line) => { lock (lines) lines.Add(line); };

        await RunAndWaitAsync(manager, EchoApp("COWORK_OK"));

        // Sự kiện output có thể tới ngay sau sự kiện Exited; cho một nhịp ngắn để xả nốt.
        await Task.Delay(300);

        lock (lines)
            Assert.Contains(lines, l => l.Text.Contains("COWORK_OK"));
    }

    [Fact]
    public async Task NonZeroExitCode_IsReportedAsFailure()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c exit 3";

        var record = await RunAndWaitAsync(manager, app);

        Assert.Equal(RunOutcome.Failed, record.Outcome);
        Assert.Equal(3, record.ExitCode);
    }

    [Fact]
    public void Start_MissingExecutable_FailsWithReadableMessage()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var result = manager.Start(new ManagedApp
        {
            Name = "khong ton tai",
            ExecutablePath = Path.Combine(temp.Path, "khong-co-that.exe"),
        }, RunTrigger.Manual);

        Assert.False(result.Started);
        Assert.NotNull(result.Error);
        Assert.Contains("Không tìm thấy file", result.Error);
    }

    [Fact]
    public void Start_DisabledApp_IsRefused()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Enabled = false;

        Assert.False(manager.Start(app, RunTrigger.Manual).Started);
    }

    [Fact]
    public async Task SingleInstance_BlocksSecondStartWhileFirstIsAlive()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c ping -n 6 127.0.0.1 > nul";
        app.SingleInstance = true;

        var first = manager.Start(app, RunTrigger.Manual);
        Assert.True(first.Started, first.Error);

        var second = manager.Start(app, RunTrigger.Manual);
        Assert.False(second.Started);
        Assert.Contains("đang chạy", second.Error);

        await manager.StopAsync(app.Id, graceMs: 1000);
    }

    [Fact]
    public async Task StopAsync_TerminatesLongRunningProcess()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c ping -n 60 127.0.0.1 > nul";

        Assert.True(manager.Start(app, RunTrigger.Manual).Started);
        Assert.True(manager.IsRunning(app.Id));

        await manager.StopAsync(app.Id, graceMs: 1500);
        await Task.Delay(500);

        Assert.False(manager.IsRunning(app.Id));
    }

    [Fact]
    public async Task EnvironmentVariables_ReachTheChildProcess()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c echo %COWORK_TEST_VAR%";
        app.EnvironmentVariables["COWORK_TEST_VAR"] = "gia-tri-rieng";

        var lines = new List<AppOutputLine>();
        manager.OutputReceived += (_, line) => { lock (lines) lines.Add(line); };

        await RunAndWaitAsync(manager, app);
        await Task.Delay(300);

        lock (lines)
            Assert.Contains(lines, l => l.Text.Contains("gia-tri-rieng"));
    }

    [Fact]
    public async Task WorkingDirectory_IsAppliedToChildProcess()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c cd";
        app.WorkingDirectory = temp.Path;

        var lines = new List<AppOutputLine>();
        manager.OutputReceived += (_, line) => { lock (lines) lines.Add(line); };

        await RunAndWaitAsync(manager, app);
        await Task.Delay(300);

        lock (lines)
            Assert.Contains(lines, l => l.Text.Trim().Equals(temp.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExitCodeOnTheSuccessList_IsReportedAsSuccess()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        // robocopy trả 1 khi đã sao chép được file — với app này, 1 là thành công.
        var app = EchoApp("x");
        app.Arguments = "/c exit 1";
        app.SuccessExitCodes = new List<int> { 0, 1 };

        var record = await RunAndWaitAsync(manager, app);

        Assert.Equal(RunOutcome.Succeeded, record.Outcome);
        Assert.Equal(1, record.ExitCode);
    }

    [Fact]
    public async Task ExitCodeOffTheSuccessList_IsReportedAsFailure_EvenWhenItIsZero()
    {
        using var temp = new TempDirectory();
        using var manager = new ProcessManager(NullLogger.Instance, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c exit 0";
        app.SuccessExitCodes = new List<int> { 1 };

        var record = await RunAndWaitAsync(manager, app);

        Assert.Equal(RunOutcome.Failed, record.Outcome);
    }

    private sealed class ListLogger : ICoworkLogger
    {
        public List<string> Lines { get; } = new();

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            lock (Lines)
                Lines.Add($"[{level}] {message}{(exception is null ? string.Empty : " :: " + exception.Message)}");
        }

        public override string ToString()
        {
            lock (Lines)
                return string.Join(Environment.NewLine, Lines);
        }
    }

    [Fact]
    public async Task StopAsync_SendsCtrlC_SoAConsoleAppExitsBeforeTheKillDeadline()
    {
        using var temp = new TempDirectory();
        var logger = new ListLogger();
        using var manager = new ProcessManager(logger, new CoworkPaths(temp.Path));

        var app = EchoApp("x");
        app.Arguments = "/c ping -n 60 127.0.0.1 > nul";

        var completion = new TaskCompletionSource<AppRunRecord>();
        manager.RunCompleted += (_, record) => completion.TrySetResult(record);

        Assert.True(manager.Start(app, RunTrigger.Manual).Started);
        await Task.Delay(500); // để cmd kịp khởi chạy ping

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await manager.StopAsync(app.Id, graceMs: 10_000);
        stopwatch.Stop();

        var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(completion.Task, finished);
        var completed = await completion.Task;

        // Kill() để lại mã thoát -1; nhận Ctrl+C thì cmd/ping tự thoát với mã khác, và thoát ngay
        // chứ không đợi hết 10 giây ân hạn.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Dừng mất {stopwatch.Elapsed}, mã thoát {completed.ExitCode}.{Environment.NewLine}{logger}");
        Assert.NotEqual(-1, completed.ExitCode);
        Assert.Equal(RunOutcome.Cancelled, completed.Outcome);
    }

    [Fact]
    public void ConsoleSignal_ReportsWhyItCouldNotSend()
    {
        // PID 0 là System Idle Process, không có console để nối vào.
        Assert.False(ConsoleSignal.TrySendCtrlC(0, out var failure));
        Assert.Contains("AttachConsole", failure);
    }
}
