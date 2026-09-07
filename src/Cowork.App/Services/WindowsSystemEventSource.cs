using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Cowork.Core.Models;
using Cowork.Core.Services;

namespace Cowork.App.Services;

/// <summary>
/// Lắng nghe sự kiện thật của Windows. Nằm ở tầng ứng dụng chứ không phải Core, vì
/// <see cref="SystemEvents"/> chỉ có trên Windows còn Core phải chạy được ở nơi khác.
///
/// <see cref="SystemEvents"/> gọi handler trên một luồng riêng do nó dựng lên; supervisor phía sau
/// đã tự lo phần đồng bộ, và mọi thứ chạm tới giao diện đều đi qua Dispatcher ở view-model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemEventSource : ISystemEventSource
{
    private readonly ICoworkLogger _logger;
    private bool _started;
    private bool _disposed;

    public WindowsSystemEventSource(ICoworkLogger logger) => _logger = logger;

    public event EventHandler<SystemEventKind>? Occurred;

    public void Start()
    {
        if (_started || _disposed)
            return;
        _started = true;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Raise(SystemEventKind.Resume);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // SessionUnlock là mở khoá màn hình; SessionLogon là đăng nhập lại sau khi chuyển tài khoản.
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon)
            Raise(SystemEventKind.SessionUnlock);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
            Raise(SystemEventKind.NetworkAvailable);
    }

    private void Raise(SystemEventKind kind)
    {
        if (_disposed)
            return;

        try
        {
            Occurred?.Invoke(this, kind);
        }
        catch (Exception ex)
        {
            // Luồng của Windows, không có ai bắt phía trên.
            _logger.Error($"Lỗi khi xử lý sự kiện hệ thống {kind}.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_started)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }

        Occurred = null;
    }
}
