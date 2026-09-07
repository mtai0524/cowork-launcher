using Cowork.Core.Services;
using Cowork.Remote.Contracts;

namespace Cowork.Remote;

/// <summary>
/// Quyết định xem có chụp được cửa sổ của một app hay không, và chụp.
///
/// Nằm ở tầng này chứ không ở Core vì kết quả là một hợp đồng dữ liệu giữa agent và hub.
/// Tách khỏi chỗ gọi để bốn nhánh hỏng — app lạ, không chạy, không có cửa sổ, chụp thất bại —
/// test được mà không cần một cửa sổ thật; việc vẽ ảnh nằm sau <see cref="IWindowCapture"/>.
/// </summary>
public sealed class AppScreenshotService
{
    /// <summary>
    /// Thu nhỏ về mức này trước khi gửi. Màn 4K chụp nguyên cỡ ra ảnh vài MB, vượt trần
    /// thông điệp của SignalR và ngốn băng thông của hub, trong khi xem trên web thì
    /// chừng này đã đủ đọc chữ.
    /// </summary>
    public const int MaxWidth = 1400;

    private readonly IProcessManager _processes;
    private readonly IWindowCapture _capture;
    private readonly IClock _clock;

    public AppScreenshotService(IProcessManager processes, IWindowCapture capture, IClock clock)
    {
        _processes = processes;
        _capture = capture;
        _clock = clock;
    }

    /// <param name="appExists">App có nằm trong danh sách của máy này không.</param>
    public ScreenshotResult Take(Guid requestId, Guid appId, bool appExists)
    {
        var now = _clock.Now;

        if (!appExists)
            return ScreenshotResult.Failed(requestId, ScreenshotFailure.UnknownApp, now);

        if (!_processes.IsRunning(appId))
            return ScreenshotResult.Failed(requestId, ScreenshotFailure.NotRunning, now);

        var window = _processes.MainWindowHandle(appId);
        if (window == 0)
            return ScreenshotResult.Failed(requestId, ScreenshotFailure.NoWindow, now);

        var image = _capture.Capture(window, MaxWidth);
        if (image is null || image.Png.Length == 0)
            return ScreenshotResult.Failed(requestId, ScreenshotFailure.CaptureFailed, now);

        return new ScreenshotResult(
            requestId, ScreenshotFailure.None, image.Png, image.Width, image.Height, now);
    }
}
