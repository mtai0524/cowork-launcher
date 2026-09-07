using Cowork.Remote;
using Cowork.Remote.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Cowork.Hub;

/// <summary>
/// Đầu nhận kết nối từ agent. Xác thực bằng token ngay lúc nối: sai thì cắt, không cho
/// gọi bất kỳ phương thức nào. Mọi trạng thái đẩy sang <see cref="MachineRegistry"/>.
/// </summary>
public sealed class AgentHub : Microsoft.AspNetCore.SignalR.Hub
{
    private const string MachineKey = "machine";

    private readonly AgentDirectory _directory;
    private readonly MachineRegistry _registry;
    private readonly ILogger<AgentHub> _logger;

    public AgentHub(AgentDirectory directory, MachineRegistry registry, ILogger<AgentHub> logger)
    {
        _directory = directory;
        _registry = registry;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var token = ExtractToken(Context.GetHttpContext());
        if (!_directory.TryResolve(token, out var machine))
        {
            _logger.LogWarning("Từ chối agent không có token hợp lệ từ {Address}.",
                Context.GetHttpContext()?.Connection.RemoteIpAddress);
            Context.Abort();
            return;
        }

        Context.Items[MachineKey] = machine;
        _registry.Connected(machine, Context.ConnectionId);
        _logger.LogInformation("Máy '{Machine}' đã nối.", machine);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.Disconnected(Context.ConnectionId);

        if (Context.Items.TryGetValue(MachineKey, out var machine))
            _logger.LogInformation("Máy '{Machine}' đã ngắt.", machine);

        await base.OnDisconnectedAsync(exception);
    }

    public Task Register(MachineSnapshot snapshot)
    {
        _registry.Register(Context.ConnectionId, snapshot);
        return Task.CompletedTask;
    }

    public Task UpdateApp(AppSnapshot app)
    {
        _registry.UpdateApp(Context.ConnectionId, app);
        return Task.CompletedTask;
    }

    [HubMethodName(HubMethods.CommandResult)]
    public Task ReportResult(CommandResult result)
    {
        _registry.Complete(result);
        return Task.CompletedTask;
    }

    [HubMethodName(HubMethods.ScreenshotResult)]
    public Task ReportScreenshot(ScreenshotResult result)
    {
        _registry.Complete(result);
        return Task.CompletedTask;
    }

    [HubMethodName(HubMethods.RunHistoryResult)]
    public Task ReportRunHistory(RunHistoryResult result)
    {
        _registry.Complete(result);
        return Task.CompletedTask;
    }

    [HubMethodName(HubMethods.RunLogResult)]
    public Task ReportRunLog(RunLogResult result)
    {
        _registry.Complete(result);
        return Task.CompletedTask;
    }

    /// <summary>
    /// SignalR client gửi token qua query khi dùng WebSocket/SSE, qua header khi long-polling —
    /// phải nhận cả hai.
    /// </summary>
    private static string? ExtractToken(HttpContext? http)
    {
        if (http is null)
            return null;

        var fromQuery = http.Request.Query["access_token"].ToString();
        if (!string.IsNullOrEmpty(fromQuery))
            return fromQuery;

        var header = http.Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        return header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? header[bearer.Length..].Trim()
            : null;
    }
}

/// <summary>Gửi lệnh xuống đúng kết nối agent qua SignalR.</summary>
public sealed class HubCommandSender : IAgentCommandSender
{
    private readonly IHubContext<AgentHub> _hub;

    public HubCommandSender(IHubContext<AgentHub> hub) => _hub = hub;

    public Task SendAsync(string connectionId, string method, object payload, CancellationToken cancellationToken)
        => _hub.Clients.Client(connectionId).SendAsync(method, payload, cancellationToken);
}
