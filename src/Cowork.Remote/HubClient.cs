using Cowork.Core.Services;
using Cowork.Remote.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cowork.Remote;

/// <summary>Phía agent cung cấp: ảnh chụp hiện tại và cách thực thi một lệnh từ xa.</summary>
public interface IAgentHost
{
    MachineSnapshot BuildSnapshot();

    Task<CommandResult> ExecuteAsync(RemoteCommand command);

    /// <summary>Chụp cửa sổ của một app. Không chụp được thì trả kết quả mang mã lý do, không ném.</summary>
    Task<ScreenshotResult> CaptureAsync(ScreenshotRequest request);
}

public enum HubLinkState
{
    Disabled = 0,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
}

/// <summary>
/// Kết nối từ agent <em>ra</em> hub. Máy trong nhà nằm sau NAT, hub không gọi vào được,
/// nên agent phải là bên chủ động — và phải tự nối lại mãi mãi, vì không có ai ngồi
/// cạnh máy để bấm "kết nối lại".
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);

    private readonly IAgentHost _host;
    private readonly ICoworkLogger _logger;
    private readonly Action<IHubConnectionBuilder>? _configure;
    private readonly object _gate = new();

    private HubConnection? _connection;
    private CancellationTokenSource? _lifetime;
    private Timer? _heartbeat;

    public HubClient(IAgentHost host, ICoworkLogger logger, Action<IHubConnectionBuilder>? configure = null)
    {
        _host = host;
        _logger = logger;
        _configure = configure;
    }

    public HubLinkState State { get; private set; } = HubLinkState.Disabled;

    public string? LastError { get; private set; }

    public event EventHandler<HubLinkState>? StateChanged;

    public static string BuildUrl(string hubUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hubUrl);
        return hubUrl.Trim().TrimEnd('/') + HubMethods.AgentPath;
    }

    /// <summary>Bắt đầu nối và giữ kết nối cho tới khi <see cref="StopAsync"/>.</summary>
    public async Task StartAsync(string hubUrl, string token, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

        var builder = new HubConnectionBuilder()
            .WithUrl(BuildUrl(hubUrl), options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .WithAutomaticReconnect(new SteadyRetry());
        _configure?.Invoke(builder);

        var connection = builder.Build();
        connection.On<RemoteCommand>(HubMethods.Execute, command => OnExecuteAsync(connection, command));
        connection.On<ScreenshotRequest>(HubMethods.Capture, request => OnCaptureAsync(connection, request));
        connection.Reconnecting += error =>
        {
            Set(HubLinkState.Reconnecting, error?.Message);
            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            Set(HubLinkState.Connected, null);
            return RegisterAsync(connection);
        };
        connection.Closed += error =>
        {
            // Chính sách tự nối lại của SignalR bỏ cuộc sau vài lần; ta thì không.
            if (_lifetime is { IsCancellationRequested: false } lifetime)
            {
                Set(HubLinkState.Failed, error?.Message);
                _ = ConnectLoopAsync(connection, lifetime.Token);
            }

            return Task.CompletedTask;
        };

        var lifetimeSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _connection = connection;
            _lifetime = lifetimeSource;
        }

        _ = ConnectLoopAsync(connection, lifetimeSource.Token);
    }

    private async Task ConnectLoopAsync(HubConnection connection, CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Set(HubLinkState.Connecting, null);
                await connection.StartAsync(cancellationToken).ConfigureAwait(false);
                Set(HubLinkState.Connected, null);
                _logger.Info("Đã nối tới hub.");

                await RegisterAsync(connection).ConfigureAwait(false);
                StartHeartbeat(connection);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Set(HubLinkState.Failed, ex.Message);
                _logger.Warning("Không nối được tới hub: " + ex.Message);

                var delay = SteadyRetry.DelayFor(attempt++);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void StartHeartbeat(HubConnection connection)
    {
        lock (_gate)
        {
            _heartbeat?.Dispose();
            _heartbeat = new Timer(_ => _ = RegisterAsync(connection), null, Heartbeat, Heartbeat);
        }
    }

    /// <summary>Gửi ảnh chụp toàn máy. Dùng lúc mới nối, lúc nối lại, và làm nhịp tim định kỳ.</summary>
    public Task PushSnapshotAsync()
    {
        var connection = _connection;
        return connection is null ? Task.CompletedTask : RegisterAsync(connection);
    }

    public async Task PushAppAsync(AppSnapshot app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
            return;

        try
        {
            await connection.SendAsync(HubMethods.UpdateApp, app).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Mất mạng giữa chừng: nhịp tim kế tiếp sẽ gửi lại toàn bộ.
            _logger.Warning("Không gửi được trạng thái app lên hub: " + ex.Message);
        }
    }

    private async Task RegisterAsync(HubConnection connection)
    {
        if (connection.State != HubConnectionState.Connected)
            return;

        try
        {
            await connection.SendAsync(HubMethods.Register, _host.BuildSnapshot()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Không gửi được ảnh chụp lên hub: " + ex.Message);
        }
    }

    private async Task OnExecuteAsync(HubConnection connection, RemoteCommand command)
    {
        CommandResult result;
        try
        {
            result = await _host.ExecuteAsync(command).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi thực thi lệnh từ hub.", ex);
            result = new CommandResult(command.RequestId, false, ex.Message);
        }

        try
        {
            await connection.SendAsync(HubMethods.CommandResult, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Không gửi được kết quả lệnh lên hub: " + ex.Message);
        }
    }

    private async Task OnCaptureAsync(HubConnection connection, ScreenshotRequest request)
    {
        ScreenshotResult result;
        try
        {
            result = await _host.CaptureAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("Lỗi khi chụp màn hình theo yêu cầu của hub.", ex);
            result = ScreenshotResult.Failed(request.RequestId, ScreenshotFailure.CaptureFailed, DateTimeOffset.Now);
        }

        try
        {
            await connection.SendAsync(HubMethods.ScreenshotResult, result).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Không gửi được ảnh chụp lên hub: " + ex.Message);
        }
    }

    private void Set(HubLinkState state, string? error)
    {
        State = state;
        LastError = error;
        StateChanged?.Invoke(this, state);
    }

    public async Task StopAsync()
    {
        HubConnection? connection;
        lock (_gate)
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;

            _heartbeat?.Dispose();
            _heartbeat = null;

            connection = _connection;
            _connection = null;
        }

        if (connection is null)
            return;

        try
        {
            await connection.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning("Lỗi khi ngắt kết nối hub: " + ex.Message);
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        Set(HubLinkState.Disabled, null);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    /// <summary>Chờ ngắn vài lần đầu rồi giữ đều 30 giây, không bao giờ bỏ cuộc.</summary>
    private sealed class SteadyRetry : IRetryPolicy
    {
        private static readonly TimeSpan[] Ladder =
        {
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        };

        public static TimeSpan DelayFor(long attempt)
            => attempt < Ladder.Length ? Ladder[attempt] : TimeSpan.FromSeconds(30);

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => DelayFor(retryContext.PreviousRetryCount);
    }
}
