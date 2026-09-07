using System.Collections.Concurrent;
using Cowork.Core.Services;
using Cowork.Remote.Contracts;

namespace Cowork.Remote;

/// <summary>
/// Kênh gửi xuống một kết nối agent cụ thể. Hub cài bằng SignalR; test cài bằng bộ giả.
///
/// Một phương thức nhận tên phương thức SignalR thay vì mỗi loại yêu cầu một nạp chồng:
/// hub hỏi agent ngày càng nhiều thứ, và mỗi thứ thêm một nạp chồng thì mọi bộ giả trong
/// test phải sửa theo dù chẳng liên quan.
/// </summary>
public interface IAgentCommandSender
{
    Task SendAsync(string connectionId, string method, object payload, CancellationToken cancellationToken);
}

/// <summary>Trạng thái một máy như web nhìn thấy.</summary>
public sealed record MachineView(
    string Name,
    bool Online,
    string? HostName,
    string? AgentVersion,
    DateTimeOffset? LastSeen,
    IReadOnlyList<AppSnapshot> Apps);

public enum CommandFailure
{
    None = 0,
    MachineUnknown,
    MachineOffline,
    SendFailed,
    Timeout,
}

/// <summary>Kết quả gửi một lệnh, nhìn từ phía web.</summary>
public sealed record CommandOutcome(bool Ok, CommandFailure Failure, string? AgentMessage)
{
    public static CommandOutcome Fail(CommandFailure failure, string? message = null) => new(false, failure, message);
}

/// <summary>
/// Sổ các máy: máy nào đang nối, trạng thái app cuối cùng nhận được, và lệnh nào đang chờ trả lời.
///
/// Thuần trạng thái trong bộ nhớ, không lưu đĩa: hub chỉ phản ánh những gì agent đang báo,
/// agent mới là nguồn sự thật. Máy rớt mạng vẫn giữ ảnh chụp cuối để web còn thấy nó *đã*
/// từng ở trạng thái nào, đánh dấu ngoại tuyến kèm mốc "lần cuối thấy".
/// </summary>
public sealed class MachineRegistry
{
    private readonly IAgentCommandSender _sender;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry> _byConnection = new(StringComparer.Ordinal);
    private readonly PendingRequests<CommandResult> _commands = new();
    private readonly PendingRequests<ScreenshotResult> _shots = new();
    private readonly PendingRequests<RunHistoryResult> _histories = new();
    private readonly PendingRequests<RunLogResult> _logs = new();

    public MachineRegistry(IAgentCommandSender sender, IClock clock, IEnumerable<string> knownMachines)
    {
        _sender = sender;
        _clock = clock;

        // Máy đã cấp token nhưng chưa từng nối vẫn hiện ra, để người quản trị biết nó đang thiếu.
        foreach (var name in knownMachines)
            _byName[name] = new Entry(name);
    }

    /// <summary>Bắn mỗi khi có gì đổi; web dùng để vẽ lại.</summary>
    public event Action? Changed;

    public IReadOnlyList<MachineView> Machines
    {
        get
        {
            lock (_gate)
            {
                return _byName.Values
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(e => e.ToView())
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Một máy vừa được cấp token: hiện nó ra ngay ở trạng thái ngoại tuyến, để người quản trị
    /// thấy máy đã tạo xong và đang chờ dán token, chứ không phải chờ nó nối rồi mới xuất hiện.
    /// </summary>
    public void Track(string machineName)
    {
        lock (_gate)
        {
            if (_byName.ContainsKey(machineName))
                return;

            _byName[machineName] = new Entry(machineName);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Một máy vừa bị thu hồi token. Gỡ luôn khỏi bảng kết nối, nhờ vậy kết nối cũ còn treo
    /// (đã xác thực từ trước) không báo trạng thái lên được nữa và cũng không nhận được lệnh.
    /// </summary>
    public bool Forget(string machineName)
    {
        lock (_gate)
        {
            if (!_byName.Remove(machineName, out var entry))
                return false;

            if (entry.ConnectionId is not null)
                _byConnection.Remove(entry.ConnectionId);
        }

        Changed?.Invoke();
        return true;
    }

    public void Connected(string machineName, string connectionId)
    {
        lock (_gate)
        {
            if (!_byName.TryGetValue(machineName, out var entry))
                _byName[machineName] = entry = new Entry(machineName);

            // Một máy nối lại trước khi kết nối cũ kịp đóng: kết nối mới thắng.
            if (entry.ConnectionId is not null)
                _byConnection.Remove(entry.ConnectionId);

            entry.ConnectionId = connectionId;
            entry.LastSeen = _clock.Now;
            _byConnection[connectionId] = entry;
        }

        Changed?.Invoke();
    }

    public bool Register(string connectionId, MachineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var entry))
                return false;

            entry.HostName = snapshot.HostName;
            entry.AgentVersion = snapshot.AgentVersion;
            entry.LastSeen = _clock.Now;
            entry.Apps = snapshot.Apps.ToList();
        }

        Changed?.Invoke();
        return true;
    }

    public bool UpdateApp(string connectionId, AppSnapshot app)
    {
        ArgumentNullException.ThrowIfNull(app);

        lock (_gate)
        {
            if (!_byConnection.TryGetValue(connectionId, out var entry))
                return false;

            entry.LastSeen = _clock.Now;
            var index = entry.Apps.FindIndex(a => a.Id == app.Id);
            if (index >= 0)
                entry.Apps[index] = app;
            else
                entry.Apps.Add(app);
        }

        Changed?.Invoke();
        return true;
    }

    public void Disconnected(string connectionId)
    {
        lock (_gate)
        {
            if (!_byConnection.Remove(connectionId, out var entry))
                return;

            // Chỉ đánh dấu ngoại tuyến nếu đây vẫn là kết nối hiện hành của máy đó.
            if (entry.ConnectionId == connectionId)
            {
                entry.ConnectionId = null;
                entry.LastSeen = _clock.Now;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Gửi lệnh và chờ agent trả lời, tối đa <paramref name="timeout"/>.</summary>
    public async Task<CommandOutcome> SendAsync(
        string machineName, Guid appId, RemoteCommandKind kind, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        string connectionId;
        lock (_gate)
        {
            if (!_byName.TryGetValue(machineName, out var entry))
                return CommandOutcome.Fail(CommandFailure.MachineUnknown);

            if (entry.ConnectionId is null)
                return CommandOutcome.Fail(CommandFailure.MachineOffline);

            connectionId = entry.ConnectionId;
        }

        var command = new RemoteCommand(Guid.NewGuid(), appId, kind);

        var outcome = await _commands.SendAndWaitAsync(
            command.RequestId,
            () => _sender.SendAsync(connectionId, HubMethods.Execute, command, cancellationToken),
            timeout,
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Result: { } result } => new CommandOutcome(result.Ok, CommandFailure.None, result.Message),
            { SendError: { } error } => CommandOutcome.Fail(CommandFailure.SendFailed, error.Message),
            _ => CommandOutcome.Fail(CommandFailure.Timeout),
        };
    }

    /// <summary>
    /// Xin agent một tấm ảnh cửa sổ của app. Trả kết quả mang mã lý do thay vì ném, vì mọi
    /// nhánh hỏng ở đây đều là chuyện bình thường: máy ngoại tuyến, app không chạy, app không
    /// có cửa sổ.
    /// </summary>
    public async Task<ScreenshotResult> RequestScreenshotAsync(
        string machineName, Guid appId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();

        if (ConnectionOf(machineName) is not { } connectionId)
            return ScreenshotResult.Failed(requestId, ScreenshotFailure.MachineOffline, _clock.Now);

        var request = new ScreenshotRequest(requestId, appId);

        var outcome = await _shots.SendAndWaitAsync(
            requestId,
            () => _sender.SendAsync(connectionId, HubMethods.Capture, request, cancellationToken),
            timeout,
            cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            { Result: { } result } => result,

            // Gửi hỏng nghĩa là kết nối vừa đứt, chứ không phải agent chụp không nổi.
            { SendError: not null } => ScreenshotResult.Failed(requestId, ScreenshotFailure.MachineOffline, _clock.Now),
            _ => ScreenshotResult.Failed(requestId, ScreenshotFailure.Timeout, _clock.Now),
        };
    }

    /// <summary>
    /// Xin agent danh sách các lần chạy gần đây của một app. Trả danh sách rỗng khi máy
    /// ngoại tuyến hoặc không trả lời — web hiển thị "không có" chứ không phải lỗi đỏ.
    /// </summary>
    public async Task<IReadOnlyList<RunSummary>> RequestRunHistoryAsync(
        string machineName, Guid appId, int limit, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();

        if (ConnectionOf(machineName) is not { } connectionId)
            return Array.Empty<RunSummary>();

        var request = new RunHistoryRequest(requestId, appId, limit);

        var outcome = await _histories.SendAndWaitAsync(
            requestId,
            () => _sender.SendAsync(connectionId, HubMethods.RunHistory, request, cancellationToken),
            timeout,
            cancellationToken).ConfigureAwait(false);

        return outcome.Result?.Runs ?? Array.Empty<RunSummary>();
    }

    /// <summary>Xin agent nội dung file log của một lần chạy. <c>null</c> nghĩa là không lấy được.</summary>
    public async Task<RunLogResult?> RequestRunLogAsync(
        string machineName, Guid runId, int maxLines, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();

        if (ConnectionOf(machineName) is not { } connectionId)
            return null;

        var request = new RunLogRequest(requestId, runId, maxLines);

        var outcome = await _logs.SendAndWaitAsync(
            requestId,
            () => _sender.SendAsync(connectionId, HubMethods.RunLog, request, cancellationToken),
            timeout,
            cancellationToken).ConfigureAwait(false);

        return outcome.Result;
    }

    /// <summary>Kết nối hiện hành của một máy, hoặc null nếu máy lạ hoặc đang ngoại tuyến.</summary>
    private string? ConnectionOf(string machineName)
    {
        lock (_gate)
            return _byName.TryGetValue(machineName, out var entry) ? entry.ConnectionId : null;
    }

    /// <summary>Agent gửi ảnh về. Ảnh của yêu cầu đã quá hạn bị bỏ qua.</summary>
    public bool Complete(ScreenshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _shots.Complete(result.RequestId, result);
    }

    public bool Complete(RunHistoryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _histories.Complete(result.RequestId, result);
    }

    public bool Complete(RunLogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _logs.Complete(result.RequestId, result);
    }

    /// <summary>Agent trả lời một lệnh. Trả lời cho lệnh không còn chờ (đã quá hạn) bị bỏ qua.</summary>
    public bool Complete(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return _commands.Complete(result.RequestId, result);
    }

    private sealed class Entry
    {
        public Entry(string name) => Name = name;

        public string Name { get; }
        public string? ConnectionId { get; set; }
        public string? HostName { get; set; }
        public string? AgentVersion { get; set; }
        public DateTimeOffset? LastSeen { get; set; }
        public List<AppSnapshot> Apps { get; set; } = new();

        public MachineView ToView()
            => new(Name, ConnectionId is not null, HostName, AgentVersion, LastSeen, Apps.ToList());
    }
}
