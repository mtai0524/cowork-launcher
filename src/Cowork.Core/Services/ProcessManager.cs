using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Cowork.Core.Models;
using Cowork.Core.Localization;

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

    /// <summary>
    /// Phải gọi <see cref="Process.Refresh"/> trước: <see cref="Process.MainWindowHandle"/> được
    /// nhớ lại từ lần đọc đầu, mà app mở cửa sổ vài giây sau khi khởi động là chuyện thường —
    /// không làm mới thì mãi mãi thấy 0.
    /// </summary>
    public nint MainWindowHandle(Guid appId)
    {
        if (!_running.TryGetValue(appId, out var entry))
            return 0;

        try
        {
            if (entry.Process.HasExited)
                return 0;

            entry.Process.Refresh();
            return entry.Process.MainWindowHandle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or PlatformNotSupportedException)
        {
            return 0;
        }
    }

    public StartResult Start(ManagedApp app, RunTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Enabled)
            return StartResult.Fail(Loc.T("Proc.Disabled"));

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            return StartResult.Fail(Loc.T("Proc.NoExecutable"));

        if (app.SingleInstance && IsRunning(app.Id))
            return StartResult.Fail(Loc.T("Proc.AlreadyRunning"));

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
            var entry = new RunningApp(app.Id, app.Name, process, record, _outputBufferLines,
                ExitCodes.Normalize(app.SuccessExitCodes).ToHashSet());

            if (startInfo.RedirectStandardOutput)
            {
                // Chốt tên file log ngay từ đầu: tính lại mỗi lần ghi thì một lần chạy vắt qua
                // nửa đêm sẽ rơi vào hai file khác nhau.
                record.OutputLogFile = CoworkPaths.RunLogFileName(record.Id, record.StartedAt);

                process.OutputDataReceived += (_, e) => HandleOutput(entry, e.Data, isError: false);
                process.ErrorDataReceived += (_, e) => HandleOutput(entry, e.Data, isError: true);
            }

            process.Exited += (_, _) => HandleExited(entry);

            // Qua ConsoleSignal để app console sau này còn nhận được Ctrl+C khi bấm Dừng.
            if (!ConsoleSignal.StartChild(process))
                return StartResult.Fail("Windows từ chối khởi chạy tiến trình.");

            record.ProcessId = process.Id;

            if (startInfo.RedirectStandardOutput)
            {
                // Ghi khối đầu trước khi mở luồng đọc: mở trước thì dòng output đầu tiên có
                // thể xuống đĩa trước header, và file mất trật tự.
                entry.WriteHeader(_paths, new RunLogContext(
                    app.Name, record.Id, trigger, startInfo.FileName, startInfo.Arguments,
                    startInfo.WorkingDirectory, app.EnvironmentVariables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                    ExitCodes.Normalize(app.SuccessExitCodes).ToList(), record.ProcessId, record.StartedAt));

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

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
        Win32Exception { NativeErrorCode: 2 } => Loc.T("Proc.FileNotFound", exePath),
        Win32Exception { NativeErrorCode: 5 } => Loc.T("Proc.AccessDenied"),
        Win32Exception { NativeErrorCode: 1223 } => Loc.T("Proc.UacCancelled"),
        Win32Exception w32 => Loc.T("Proc.WindowsError", w32.NativeErrorCode, w32.Message),
        _ => ex.Message,
    };

    /// <summary>
    /// Ghi một dòng do chính Cowork sinh ra vào nhật ký của lần chạy. Nằm chung file với
    /// output của app, theo đúng thứ tự thời gian — tách ra file khác thì lúc dò lỗi phải
    /// ngồi ghép hai dòng thời gian lại với nhau.
    /// </summary>
    private void Note(RunningApp entry, string text)
    {
        var line = AppOutputLine.FromCowork(entry.AppId, DateTimeOffset.Now, text);
        entry.Append(line);
        entry.WriteToLog(_paths);
        OutputReceived?.Invoke(this, line);
    }

    private void HandleOutput(RunningApp entry, string? text, bool isError)
    {
        if (text is null)
            return;

        var line = AppOutputLine.FromApp(entry.AppId, DateTimeOffset.Now, text, isError);
        entry.Append(line);
        entry.WriteToLog(_paths);
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
            record.Outcome = record.ExitCode is { } code && entry.SuccessExitCodes.Contains(code)
                ? RunOutcome.Succeeded
                : RunOutcome.Failed;
        }

        _logger.Info($"'{entry.AppName}' kết thúc, mã thoát {record.ExitCode?.ToString() ?? "?"} ({record.Outcome}).");

        entry.WriteToLog(_paths);
        entry.WriteFooter(_paths);

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
        Note(entry, Loc.T("RunLog.Timeout"));
        TryKill(entry.Process);
    }

    public Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default)
        => TerminateAsync(appId, RunOutcome.Cancelled, reason: null, graceMs, cancellationToken);

    public async Task<bool> TerminateAsync(
        Guid appId, RunOutcome outcome, string? reason, int graceMs = 5000, CancellationToken cancellationToken = default)
    {
        if (!_running.TryGetValue(appId, out var entry) || entry.Process.HasExited)
            return false;

        RaiseStatus(appId, AppRuntimeState.Stopping, entry.Record.ProcessId, null);
        entry.Record.Outcome = outcome;
        if (reason is not null)
            entry.Record.Error = reason;

        Note(entry, reason is null
            ? Loc.T("RunLog.StopRequested", graceMs / 1000.0)
            : Loc.T("RunLog.StopBecause", reason, graceMs / 1000.0));

        try
        {
            // Dừng lịch sự theo thứ tự: đóng cửa sổ chính (app GUI), không có cửa sổ thì gửi Ctrl+C
            // (app console). Cả hai đều không được thì kill ngay, không chờ vô ích.
            if (entry.Process.CloseMainWindow())
            {
                Note(entry, Loc.T("RunLog.ClosedWindow"));
            }
            else if (TrySendCtrlC(entry))
            {
                Note(entry, Loc.T("RunLog.SentCtrlC"));
            }
            else
            {
                Note(entry, Loc.T("RunLog.KilledNoSignal"));
                TryKill(entry.Process);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(graceMs);

            try
            {
                await entry.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Note(entry, Loc.T("RunLog.GraceExpired"));
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

    /// <summary>
    /// Gửi Ctrl+C cho app console. Tín hiệu tới cả cây tiến trình dùng chung console (cmd → node),
    /// nên server thật bên trong một file .bat cũng được dọn dẹp tử tế.
    /// </summary>
    private bool TrySendCtrlC(RunningApp entry)
    {
        if (ConsoleSignal.TrySendCtrlC(entry.Process.Id, out var failure))
        {
            _logger.Info($"Đã gửi Ctrl+C tới '{entry.AppName}' (PID {entry.Process.Id}).");
            return true;
        }

        // App GUI chưa có cửa sổ, tiến trình chạy quyền admin… — không có console để gửi.
        _logger.Info($"Không gửi được Ctrl+C tới '{entry.AppName}' ({failure}), chuyển sang kill.");
        return false;
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

        public RunningApp(
            Guid appId, string appName, Process process, AppRunRecord record, int capacity,
            IReadOnlySet<int> successExitCodes)
        {
            AppId = appId;
            AppName = appName;
            Process = process;
            Record = record;
            _capacity = capacity;
            SuccessExitCodes = successExitCodes;
        }

        public Guid AppId { get; }
        public string AppName { get; }
        public Process Process { get; }
        public AppRunRecord Record { get; }

        /// <summary>Chụp lại lúc khởi chạy: người dùng sửa danh sách giữa chừng thì áp dụng cho lần sau.</summary>
        public IReadOnlySet<int> SuccessExitCodes { get; }

        public void Append(AppOutputLine line)
        {
            lock (_gate)
            {
                _buffer.Enqueue(line);
                while (_buffer.Count > _capacity)
                    _buffer.Dequeue();

                _pendingLogLines.Add(RunLogFormat.Line(line));
            }
        }

        /// <summary>Khối đầu file. Ghi thẳng, không qua bộ đệm, để nó chắc chắn nằm trước mọi dòng output.</summary>
        public void WriteHeader(CoworkPaths paths, RunLogContext context)
            => Write(paths, RunLogFormat.Header(context));

        public void WriteFooter(CoworkPaths paths)
            => Write(paths, RunLogFormat.Footer(Record));

        private void Write(CoworkPaths paths, IEnumerable<string> lines)
        {
            if (Record.OutputLogFile is not { } fileName)
                return;

            try
            {
                File.AppendAllLines(paths.RunLogFile(fileName), lines, System.Text.Encoding.UTF8);
            }
            catch (IOException)
            {
            }
        }

        /// <summary>Xả bộ đệm log xuống đĩa; lỗi ghi file không được ảnh hưởng tới app.</summary>
        public void WriteToLog(CoworkPaths paths)
        {
            if (Record.OutputLogFile is not { } fileName)
                return;

            List<string> batch;
            lock (_gate)
            {
                if (_pendingLogLines.Count == 0)
                    return;
                batch = new List<string>(_pendingLogLines);
                _pendingLogLines.Clear();
            }

            Write(paths, batch);
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
