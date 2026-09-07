using System.Net.Http;
using System.Net.Sockets;
using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Một lần thăm dò cổng/URL. Tách interface để test bộ theo dõi mà không cần server thật.</summary>
public interface IHealthProbe
{
    /// <summary>Trả về null nếu app phản hồi, ngược lại là lý do ngắn gọn, đọc được.</summary>
    Task<string?> ProbeAsync(HealthProbeKind kind, string target, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Thăm dò thật qua mạng: mở kết nối TCP, hoặc GET một URL và xem mã trả về.</summary>
public sealed class NetworkHealthProbe : IHealthProbe
{
    // Một HttpClient dùng chung cho mọi lần thăm dò; tạo mới mỗi lần sẽ cạn socket.
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        // Thời hạn do từng lần thăm dò đặt qua CancellationToken.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<string?> ProbeAsync(HealthProbeKind kind, string target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            switch (kind)
            {
                case HealthProbeKind.TcpPort:
                {
                    if (!HealthTarget.TryParseTcp(target, out var host, out var port))
                        return Loc.T("Health.BadTarget", target);

                    using var client = new TcpClient();
                    await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
                    return null;
                }

                case HealthProbeKind.HttpGet:
                {
                    if (!HealthTarget.TryParseHttp(target, out var uri))
                        return Loc.T("Health.BadTarget", target);

                    using var response = await Http
                        .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                        .ConfigureAwait(false);

                    return response.IsSuccessStatusCode ? null : Loc.T("Health.HttpStatus", (int)response.StatusCode);
                }

                default:
                    return null;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Loc.T("Health.ProbeTimeout", (int)Math.Ceiling(timeout.TotalSeconds));
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or IOException)
        {
            return ex.Message;
        }
    }
}
