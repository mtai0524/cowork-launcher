using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Hub;
using Cowork.Remote;
using Cowork.Remote.Contracts;
using Xunit;

namespace Cowork.Tests;

/// <summary>Bốn nhánh hỏng của việc chụp cửa sổ, không cần một cửa sổ thật nào.</summary>
public class AppScreenshotServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 14, 30, 0, TimeSpan.FromHours(7));
    private static readonly Guid AppId = Guid.NewGuid();

    private sealed class FakeCapture : IWindowCapture
    {
        public CapturedImage? Result { get; set; } = new(new byte[] { 1, 2, 3 }, 640, 480);

        public nint SeenHandle { get; private set; }

        public int SeenMaxWidth { get; private set; }

        public CapturedImage? Capture(nint windowHandle, int maxWidth)
        {
            SeenHandle = windowHandle;
            SeenMaxWidth = maxWidth;
            return Result;
        }
    }

    private sealed class FakeProcesses : IProcessManager
    {
        public bool Running { get; set; } = true;
        public nint Window { get; set; } = 4242;

        public bool IsRunning(Guid appId) => Running;
        public nint MainWindowHandle(Guid appId) => Window;

        public IReadOnlyCollection<Guid> RunningAppIds => Array.Empty<Guid>();
        public StartResult Start(ManagedApp app, RunTrigger trigger) => throw new NotSupportedException();
        public Task<bool> StopAsync(Guid appId, int graceMs = 5000, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> TerminateAsync(Guid appId, RunOutcome outcome, string? reason, int graceMs = 5000, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task StopAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IReadOnlyList<AppOutputLine> GetOutput(Guid appId) => Array.Empty<AppOutputLine>();

        public event EventHandler<AppStatusChanged>? StatusChanged;
        public event EventHandler<AppOutputLine>? OutputReceived;
        public event EventHandler<AppRunRecord>? RunCompleted;

        // Sự kiện không dùng ở đây nhưng interface đòi; giữ để trình biên dịch khỏi cảnh báo thừa.
        private void Unused()
        {
            StatusChanged?.Invoke(this, null!);
            OutputReceived?.Invoke(this, null!);
            RunCompleted?.Invoke(this, null!);
        }
    }

    private static (AppScreenshotService Service, FakeProcesses Processes, FakeCapture Capture) Build()
    {
        var processes = new FakeProcesses();
        var capture = new FakeCapture();
        return (new AppScreenshotService(processes, capture, new FixedClock(Now)), processes, capture);
    }

    [Fact]
    public void Capture_ReturnsTheImage_AndStampsTheTime()
    {
        var (service, _, capture) = Build();
        var requestId = Guid.NewGuid();

        var result = service.Take(requestId, AppId, appExists: true);

        Assert.True(result.Ok);
        Assert.Equal(requestId, result.RequestId);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Png);
        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
        Assert.Equal(Now, result.TakenAt);

        Assert.Equal(4242, capture.SeenHandle);
        Assert.Equal(AppScreenshotService.MaxWidth, capture.SeenMaxWidth);
    }

    [Fact]
    public void UnknownApp_IsReportedAsSuch()
    {
        var (service, _, _) = Build();

        var result = service.Take(Guid.NewGuid(), AppId, appExists: false);

        Assert.Equal(ScreenshotFailure.UnknownApp, result.Failure);
        Assert.Empty(result.Png);
    }

    [Fact]
    public void NotRunningApp_IsReportedAsSuch()
    {
        var (service, processes, _) = Build();
        processes.Running = false;

        Assert.Equal(ScreenshotFailure.NotRunning, service.Take(Guid.NewGuid(), AppId, true).Failure);
    }

    /// <summary>Dịch vụ nền và console ẩn: đang chạy nhưng không có gì để chụp.</summary>
    [Fact]
    public void RunningWithoutAWindow_IsReportedAsSuch()
    {
        var (service, processes, _) = Build();
        processes.Window = 0;

        Assert.Equal(ScreenshotFailure.NoWindow, service.Take(Guid.NewGuid(), AppId, true).Failure);
    }

    [Fact]
    public void CaptureReturningNothing_IsReportedAsFailure()
    {
        var (service, _, capture) = Build();
        capture.Result = null;

        Assert.Equal(ScreenshotFailure.CaptureFailed, service.Take(Guid.NewGuid(), AppId, true).Failure);
    }

    /// <summary>Ảnh rỗng cũng là hỏng: gửi một tấm 0 byte lên web chỉ ra ô ảnh vỡ.</summary>
    [Fact]
    public void EmptyImage_IsReportedAsFailure()
    {
        var (service, _, capture) = Build();
        capture.Result = new CapturedImage(Array.Empty<byte>(), 100, 100);

        Assert.Equal(ScreenshotFailure.CaptureFailed, service.Take(Guid.NewGuid(), AppId, true).Failure);
    }
}

/// <summary>Đường đi của yêu cầu chụp ở phía hub.</summary>
public class ScreenshotRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 14, 0, 0, TimeSpan.FromHours(7));

    private static (MachineRegistry Registry, RecordingSender Sender) Build()
    {
        var sender = new RecordingSender();
        var registry = new MachineRegistry(sender, new FixedClock(Now), new[] { "may-a" });
        sender.Registry = registry;
        return (registry, sender);
    }

    [Fact]
    public async Task OfflineMachine_IsReportedAsOffline_AndNothingIsSent()
    {
        var (registry, sender) = Build();

        var result = await registry.RequestScreenshotAsync("may-a", Guid.NewGuid(), TimeSpan.FromMilliseconds(50));

        Assert.Equal(ScreenshotFailure.MachineOffline, result.Failure);
        Assert.Empty(sender.Shots);
    }

    [Fact]
    public async Task UnknownMachine_IsReportedAsOffline()
    {
        var (registry, _) = Build();

        var result = await registry.RequestScreenshotAsync("khong-co", Guid.NewGuid(), TimeSpan.FromMilliseconds(50));

        Assert.Equal(ScreenshotFailure.MachineOffline, result.Failure);
    }

    /// <summary>
    /// Agent bản cũ không có bộ xử lý lệnh chụp nên lặng lẽ bỏ qua; phía hub chỉ thấy im lặng.
    /// Phải gọi đúng tên là hết giờ chờ, đừng nói "không chụp được" — hai chuyện cần hai cách xử lý khác nhau.
    /// </summary>
    [Fact]
    public async Task SilentAgent_IsReportedAsTimeout_NotCaptureFailure()
    {
        var (registry, _) = Build();
        registry.Connected("may-a", "c1");

        var result = await registry.RequestScreenshotAsync("may-a", Guid.NewGuid(), TimeSpan.FromMilliseconds(100));

        Assert.Equal(ScreenshotFailure.Timeout, result.Failure);
    }

    [Fact]
    public async Task AnsweredRequest_ComesBackWithTheImage()
    {
        var (registry, sender) = Build();
        registry.Connected("may-a", "c1");
        sender.OnScreenshot = request =>
            new ScreenshotResult(request.RequestId, ScreenshotFailure.None, new byte[] { 7, 7 }, 320, 240, Now);

        var result = await registry.RequestScreenshotAsync("may-a", Guid.NewGuid(), TimeSpan.FromSeconds(5));

        Assert.True(result.Ok);
        Assert.Equal(new byte[] { 7, 7 }, result.Png);
        Assert.Equal("c1", Assert.Single(sender.Shots).ConnectionId);
    }

    /// <summary>Ảnh về muộn sau khi đã quá hạn thì bỏ, không được gán vào yêu cầu khác.</summary>
    [Fact]
    public async Task LateAnswer_IsIgnored()
    {
        var (registry, _) = Build();
        registry.Connected("may-a", "c1");

        await registry.RequestScreenshotAsync("may-a", Guid.NewGuid(), TimeSpan.FromMilliseconds(50));

        Assert.False(registry.Complete(
            new ScreenshotResult(Guid.NewGuid(), ScreenshotFailure.None, new byte[] { 1 }, 10, 10, Now)));
    }
}

/// <summary>Kho ảnh tạm của hub: có hạn dùng và có trần, vì đây là ảnh màn hình máy người ta.</summary>
public class ScreenshotCacheTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 14, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void StoredImage_ComesBack()
    {
        var cache = new ScreenshotCache(new FixedClock(Start));
        var png = new byte[] { 9, 8, 7 };

        Assert.Equal(png, cache.Get(cache.Put(png)));
    }

    [Fact]
    public void UnknownId_ReturnsNothing()
        => Assert.Null(new ScreenshotCache(new FixedClock(Start)).Get(Guid.NewGuid()));

    [Fact]
    public void ExpiredImage_IsNoLongerServed()
    {
        var clock = new FixedClock(Start);
        var cache = new ScreenshotCache(clock);
        var id = cache.Put(new byte[] { 1 });

        clock.Now = Start + ScreenshotCache.Lifetime + TimeSpan.FromSeconds(1);

        Assert.Null(cache.Get(id));
    }

    [Fact]
    public void ImageJustBeforeExpiry_IsStillServed()
    {
        var clock = new FixedClock(Start);
        var cache = new ScreenshotCache(clock);
        var id = cache.Put(new byte[] { 1 });

        clock.Now = Start + ScreenshotCache.Lifetime - TimeSpan.FromSeconds(1);

        Assert.NotNull(cache.Get(id));
    }

    /// <summary>Bấm chụp liên tục không được làm kho phình mãi.</summary>
    [Fact]
    public void Cache_StaysUnderItsCapacity()
    {
        var cache = new ScreenshotCache(new FixedClock(Start));

        for (var i = 0; i < ScreenshotCache.Capacity * 3; i++)
            cache.Put(new byte[] { (byte)i });

        Assert.True(cache.Count <= ScreenshotCache.Capacity, $"giữ {cache.Count} ảnh, trần là {ScreenshotCache.Capacity}");
    }

    [Fact]
    public void Putting_DropsImagesThatAlreadyExpired()
    {
        var clock = new FixedClock(Start);
        var cache = new ScreenshotCache(clock);
        cache.Put(new byte[] { 1 });

        clock.Now = Start + ScreenshotCache.Lifetime + TimeSpan.FromMinutes(1);
        cache.Put(new byte[] { 2 });

        Assert.Equal(1, cache.Count);
    }
}
