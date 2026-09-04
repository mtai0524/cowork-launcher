using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Khởi chạy, theo dõi và dừng tiến trình con của từng app.
/// Mỗi app chỉ có tối đa một tiến trình đang được theo dõi tại một thời điểm.
/// </summary>
public sealed class ProcessManager : IProcessManager, IDisposable
{
    private readonly ICoworkLogger _logger;
    private readonly CoworkPaths _paths;
    private readonly int _outputBufferLines;
    private readonly ConcurrentDictionary<Guid, RunningApp> _running = new();
    private bool _disposed;

    public ProcessManager(ICoworkLogger logger, CoworkPaths paths, int outputBufferLines = 2000)
    {
        _logger = logger;
        _paths = paths;
        _outputBufferLines = Math.Max(100, outputBufferLines);
    }

    public event EventHandler<AppStatusChanged>? StatusChanged;
    public event EventHandler<AppOutputLine>? OutputReceived;
    public event EventHandler<AppRunRecord>? RunCompleted;

    public bool IsRunning(Guid appId)
        => _running.TryGetValue(appId, out var entry) && !entry.Process.HasExited;

    public IReadOnlyCollection<Guid> RunningAppIds => _running.Keys.ToList();

    public IReadOnlyList<AppOutputLine> GetOutput(Guid appId)
        => _running.TryGetValue(appId, out var entry)
            ? entry.Snapshot()
            : Array.Empty<AppOutputLine>();

    public StartResult Start(ManagedApp app, RunTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Enabled)
            return StartResult.Fail("App đang bị tắt.");

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            return StartResult.Fail("Chưa khai báo đường dẫn chương trình.");

        if (app.SingleInstance && IsRunning(app.Id))
            return StartResult.Fail("App đang chạy, bỏ qua lần khởi chạy này.");

        // Instance cũ đã thoát nhưng chưa được dọn — dọn trước khi chạy mới.
        if (_running.TryGetValue(app.Id, out var stale) && stale.Process.HasExited)
            Cleanup(app.Id, stale);

        var exePath = Environment.ExpandEnvironmentVariables(app.ExecutablePath);
        var workingDirectory = app.ResolveWorkingDirectory();

        var record = new AppRunRecord
        {
            AppId = app.Id,
            AppName = app.Name,
            Trigger = trigger,
            StartedAt = DateTimeOffset.Now,
        };

        try
        {
            var startInfo = BuildStartInfo(app, exePath, workingDirectory);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var entry = new RunningApp(app.Id, app.Name, process, record, _outputBufferLines);

            if (startInfo.RedirectStandardOutput)
            {
                process.OutputDataReceived += (_, e) => HandleOutput(entry, e.Data, isError: false);
                process.ErrorDataReceived += (_, e) => HandleOutput(entry, e.Data, isError: true);
            }

            process.Exited += (_, _) => HandleExited(entry);

            if (!process.Start())
                return StartResult.Fail("Windows từ chối khởi chạy tiến trình.");

            if (startInfo.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            record.ProcessId = process.Id;
            _running[app.Id] = entry;

            if (app.TimeoutMinutes > 0)
                entry.ArmTimeout(TimeSpan.FromMinutes(app.TimeoutMinutes), () => KillForTimeout(entry));

            _logger.Info($"Chạy '{app.Name}' (PID {process.Id}, nguồn {trigger}).");
            RaiseStatus(app.Id, AppRuntimeState.Running, process.Id, null);

            return StartResult.Ok(process.Id);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException
                                       or DirectoryNotFoundException or PlatformNotSupportedException)
        {
            var message = DescribeStartFailure(ex, exePath);
            _logger.Error($"Không chạy được '{app.Name}': {message}", ex);

            record.Outcome = RunOutcome.NotStarted;
            record.FinishedAt = DateTimeOffset.Now;
            record.Error = message;
            RunCompleted?.Invoke(this, record);

            RaiseStatus(app.Id, AppRuntimeState.Failed, 0, message);
            return StartResult.Fail(message);
        }
    }

    private ProcessStartInfo BuildStartInfo(ManagedApp app, string exePath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = Environment.ExpandEnvironmentVariables(app.Arguments ?? string.Empty),
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            WindowStyle = app.WindowStyle switch
            {
                AppWindowStyle.Minimized => ProcessWindowStyle.Minimized,
                AppWindowStyle.Hidden => ProcessWindowStyle.Hidden,
                _ => ProcessWindowStyle.Normal,
            },
        };

        // Chạy quyền admin bắt buộc dùng ShellExecute, mà ShellExecute thì không
        // chuyển hướng được stdout — hai chế độ loại trừ lẫn nhau.
        if (app.RunAsAdministrator)
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            return startInfo;
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = app.WindowStyle == AppWindowStyle.Hidden;
        startInfo.RedirectStandardOutput = app.CaptureOutput;
        startInfo.RedirectStandardError = app.CaptureOutput;

        foreach (var pair in app.EnvironmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
                startInfo.Environment[pair.Key] = Environment.ExpandEnvironmentVariables(pair.Value ?? string.Empty);
        }

        return startInfo;
    }

    private static string DescribeStartFailure(Exception ex, string exePath) => ex switch
    {
        Win32Exception { NativeErrorCode: 2 } => $"Không tìm thấy file: {exePath}",
        Win32Exception { NativeErrorCode: 5 } => "Bị từ chối quyền truy cập. Thử bật 'Chạy quyền admin'.",
        Win32Exception { NativeErrorCode: 1223 } => "Người dùng đã huỷ hộp thoại nâng quyền (UAC).",
        Win32Exception w32 => $"Lỗi Windows {w32.NativeErrorCode}: {w32.Message}",
        _ => ex.Message,
    };

    private void HandleOutput(RunningApp entry, string? text, bool isError)
    {
        if (text is null)
            return;

        var line = new AppOutputLine(entry.AppId, DateTimeOffset.Now, text, isError);
        entry.Append(line);
        entry.WriteToLog(_paths.OutputLogFile(entry.AppId), isError);
        OutputReceived?.Invoke(this, line);
    }

    private void HandleExited(RunningApp entry)
    {
        entry.DisarmTimeout();

        var record = entry.Record;
        record.FinishedAt = DateTimeOffset.Now;

        try
        {
            record.ExitCode = entry.Process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            record.ExitCode = null;
        }

        if (record.Outcome == RunOutcome.Running)
        {
            record.Outcome = record.ExitCode == 0 ? RunOutcome.Succeeded : RunOutcome.Failed;
        }

        _logger.Info($"'{entry.AppName}' kết thúc, mã thoát {record.ExitCode?.ToString() ?? "?"} ({record.Outcome}).");

        Cleanup(entry.AppId, entry);

        RaiseStatus(entry.AppId,
            record.Outcome == RunOutcome.Succeeded ? AppRuntimeState.Idle : AppRuntimeState.Failed,
            record.ProcessId,
            record.Error);

        RunCompleted?.Invoke(this, record);
    }

    private void KillForTimeout(RunningApp entry)
    {
        if (entry.Process.HasExited)
            return;

        entry.Record.Outcome = RunOutcome.TimedOut;
        entry.Record.Error = "Vượt quá thời gian chạy tối đa.";
        _logger.Warning($"'{entry.AppName}' bị dừng do quá thời gian cho phép.");
        TryKill(entry.Process);
    }

    public async Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(appId, out var entry) || entry.Process.HasExited)
            return false;

        RaiseStatus(appId, AppRuntimeState.Stopping, entry.Record.ProcessId, null);
        entry.Record.Outcome = RunOutcome.Cancelled;

        try
        {
            // Đóng cửa sổ chính trước; app có cơ hội lưu dữ liệu.
            if (!entry.Process.CloseMainWindow())
                TryKill(entry.Process);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(graceMs);

            try
            {
                await entry.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(entry.Process);
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // Tiến trình đã thoát ngay trước khi ta gọi.
            return false;
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _running.Keys.Select(id => StopAsync(id, cancellationToken: cancellationToken)).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void TryKill(Process process)
    {
        try
        {
            // entireProcessTree: script .bat thường sinh tiến trình con, phải diệt cả cây.
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            _logger.Warning("Không kill được tiến trình: " + ex.Message);
        }
    }

    private void Cleanup(Guid appId, RunningApp entry)
    {
        if (_running.TryRemove(new KeyValuePair<Guid, RunningApp>(appId, entry)))
            entry.Dispose();
    }

    private void RaiseStatus(Guid appId, AppRuntimeState state, int processId, string? message)
        => StatusChanged?.Invoke(this, new AppStatusChanged(appId, state, processId, message));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var entry in _running.Values)
            entry.Dispose();

        _running.Clear();
    }

    /// <summary>Trạng thái sống của một app đang chạy: tiến trình, bộ đệm output, hẹn giờ timeout.</summary>
    private sealed class RunningApp : IDisposable
    {
        private readonly Queue<AppOutputLine> _buffer = new();
        private readonly int _capacity;
        private readonly object _gate = new();
        private readonly List<string> _pendingLogLines = new();
        private Timer? _timeoutTimer;

        public RunningApp(Guid appId, string appName, Process process, AppRunRecord record, int capacity)
        {
            AppId = appId;
            AppName = appName;
            Process = process;
            Record = record;
            _capacity = capacity;
        }

        public Guid AppId { get; }
        public string AppName { get; }
        public Process Process { get; }
        public AppRunRecord Record { get; }

        public void Append(AppOutputLine line)
        {
            lock (_gate)
            {
                _buffer.Enqueue(line);
                while (_buffer.Count > _capacity)
                    _buffer.Dequeue();

                _pendingLogLines.Add(
                    $"{line.Timestamp:HH:mm:ss} {(line.IsError ? "[ERR] " : string.Empty)}{line.Text}");
            }
        }

        /// <summary>Xả bộ đệm log xuống đĩa; lỗi ghi file không được ảnh hưởng tới app.</summary>
        public void WriteToLog(string path, bool _)
        {
            List<string> batch;
            lock (_gate)
            {
                if (_pendingLogLines.Count == 0)
                    return;
                batch = new List<string>(_pendingLogLines);
                _pendingLogLines.Clear();
            }

            try
            {
                File.AppendAllLines(path, batch, System.Text.Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }

        public IReadOnlyList<AppOutputLine> Snapshot()
        {
            lock (_gate)
                return _buffer.ToList();
        }

        public void ArmTimeout(TimeSpan after, Action onTimeout)
            => _timeoutTimer = new Timer(_ => onTimeout(), null, after, Timeout.InfiniteTimeSpan);

        public void DisarmTimeout()
        {
            _timeoutTimer?.Dispose();
            _timeoutTimer = null;
        }

        public void Dispose()
        {
            DisarmTimeout();
            Process.Dispose();
        }
    }
}
