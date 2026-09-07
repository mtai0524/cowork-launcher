using System.Collections.Concurrent;
using Cowork.Core.Services;

namespace Cowork.Hub;

/// <summary>
/// Giữ tạm các ảnh vừa chụp để trình duyệt tải về qua thẻ &lt;img&gt;.
///
/// Không đẩy ảnh thẳng vào HTML dạng data URL: một tấm PNG vài trăm KB sẽ thành nửa MB
/// base64 đi qua vòng SignalR của Blazor mỗi lần vẽ lại trang. Để trình duyệt tải bằng
/// một request riêng thì nhẹ hơn và ảnh cũng cache được.
///
/// Trong bộ nhớ và có hạn dùng: đây là ảnh màn hình máy người ta, không có lý do gì giữ lâu.
/// </summary>
public sealed class ScreenshotCache
{
    /// <summary>Đủ để xem và bấm tải lại vài lần, không đủ để quên mất là nó còn nằm đó.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Trần số ảnh giữ cùng lúc, phòng khi có người bấm chụp liên tục.</summary>
    public const int Capacity = 40;

    private readonly IClock _clock;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public ScreenshotCache(IClock clock) => _clock = clock;

    public int Count => _entries.Count;

    public Guid Put(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        var now = _clock.Now;
        Prune(now);

        var id = Guid.NewGuid();
        _entries[id] = new Entry(png, now + Lifetime);
        return id;
    }

    public byte[]? Get(Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry))
            return null;

        if (entry.ExpiresAt > _clock.Now)
            return entry.Png;

        _entries.TryRemove(id, out _);
        return null;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (id, entry) in _entries)
        {
            if (entry.ExpiresAt <= now)
                _entries.TryRemove(id, out _);
        }

        // Vẫn quá đông dù đã dọn hết ảnh hết hạn: bỏ những tấm sắp hết hạn nhất trước.
        var excess = _entries.Count - Capacity + 1;
        if (excess <= 0)
            return;

        foreach (var id in _entries.OrderBy(e => e.Value.ExpiresAt).Take(excess).Select(e => e.Key).ToList())
            _entries.TryRemove(id, out _);
    }

    private sealed record Entry(byte[] Png, DateTimeOffset ExpiresAt);
}
