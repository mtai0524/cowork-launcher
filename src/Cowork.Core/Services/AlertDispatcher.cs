using Cowork.Core.Models;

namespace Cowork.Core.Services;

public sealed record AlertSentEventArgs(Alert Alert, IReadOnlyList<string> Delivered, IReadOnlyList<string> Failed);

/// <summary>
/// Đẩy một cảnh báo ra mọi kênh đã khai. Gửi song song, không bao giờ ném ra ngoài, và không bao
/// giờ chặn người gọi: chỗ gọi nó là luồng giao diện ngay sau khi một app vừa lỗi.
///
/// Kênh nào hỏng cũng không kéo theo kênh khác — mất mạng thì webhook lỗi nhưng email nội bộ vẫn đi.
/// </summary>
public sealed class AlertDispatcher : IDisposable
{
    private readonly Func<NotificationSettings> _settings;
    private readonly IReadOnlyList<IAlertChannel> _channels;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    /// <param name="settings">Đọc lại mỗi lần gửi: người dùng có thể vừa sửa thiết lập.</param>
    public AlertDispatcher(
        Func<NotificationSettings> settings,
        IEnumerable<IAlertChannel> channels,
        IClock clock,
        ICoworkLogger logger)
    {
        _settings = settings;
        _channels = channels.ToList();
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Đã gửi xong một cảnh báo, kèm danh sách kênh thành công và thất bại.</summary>
    public event EventHandler<AlertSentEventArgs>? Sent;

    public IReadOnlyList<IAlertChannel> Channels => _channels;

    /// <summary>Có kênh nào dùng được không — giao diện dùng để nói "chưa khai kênh nào".</summary>
    public bool AnyConfigured() => AlertPolicy.AnyChannelConfigured(_settings(), _channels);

    /// <summary>
    /// Gửi cảnh báo, không chờ. Trả về true nếu cảnh báo được nhận để gửi (không bị khoảng lặng
    /// hay công tắc chung chặn).
    /// </summary>
    public bool Send(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (_disposed)
            return false;

        var settings = _settings();
        var now = _clock.Now;

        lock (_gate)
        {
            var last = _lastSent.TryGetValue(alert.DedupeKey, out var at) ? at : (DateTimeOffset?)null;
            if (!AlertPolicy.ShouldSend(settings, last, now))
                return false;

            _lastSent[alert.DedupeKey] = now;
        }

        var targets = _channels.Where(c => c.IsConfigured(settings)).ToList();
        if (targets.Count == 0)
            return false;

        _ = DeliverAsync(settings.Clone(), alert, targets);
        return true;
    }

    /// <summary>Gửi và chờ xong — dành cho nút "Gửi thử" ở màn hình thiết lập.</summary>
    public async Task<AlertSentEventArgs> SendTestAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var settings = _settings();
        var targets = _channels.Where(c => c.IsConfigured(settings)).ToList();

        return await DeliverAsync(settings.Clone(), alert, targets, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AlertSentEventArgs> DeliverAsync(
        NotificationSettings settings,
        Alert alert,
        IReadOnlyList<IAlertChannel> targets,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);

        var results = await Task.WhenAll(targets.Select(async channel =>
        {
            try
            {
                await channel.SendAsync(settings, alert, linked.Token).ConfigureAwait(false);
                return (channel.Name, Error: (string?)null);
            }
            catch (Exception ex)
            {
                // Mọi loại lỗi đều bắt: kênh hỏng không được làm sập Cowork, cũng không được
                // kéo theo các kênh khác.
                _logger.Warning($"Không gửi được cảnh báo qua {channel.Name}: {ex.Message}");
                return (channel.Name, Error: ex.Message);
            }
        })).ConfigureAwait(false);

        var delivered = results.Where(r => r.Error is null).Select(r => r.Name).ToList();
        var failed = results.Where(r => r.Error is not null).Select(r => r.Name).ToList();

        if (delivered.Count > 0)
            _logger.Info($"Đã gửi cảnh báo '{alert.Title}' qua {string.Join(", ", delivered)}.");

        var args = new AlertSentEventArgs(alert, delivered, failed);
        Sent?.Invoke(this, args);
        return args;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
