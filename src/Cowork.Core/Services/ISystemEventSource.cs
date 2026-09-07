using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Nguồn phát sự kiện của máy. Tách interface vì phần lắng nghe thật phụ thuộc Windows
/// (<c>SystemEvents</c>, <c>NetworkChange</c>) — nó nằm ở tầng ứng dụng, còn tầng Core chỉ cần
/// biết "vừa có sự kiện gì".
/// </summary>
public interface ISystemEventSource : IDisposable
{
    event EventHandler<SystemEventKind>? Occurred;

    void Start();
}

/// <summary>Nguồn giả cho test và cho lúc chạy trên nền không phải Windows.</summary>
public sealed class NullSystemEventSource : ISystemEventSource
{
    public event EventHandler<SystemEventKind>? Occurred;

    public void Start()
    {
    }

    /// <summary>Bắn một sự kiện như thể máy vừa sinh ra nó.</summary>
    public void Raise(SystemEventKind kind) => Occurred?.Invoke(this, kind);

    public void Dispose() => Occurred = null;
}
