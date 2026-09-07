using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Remote;
using Cowork.Remote.Contracts;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cowork.Tests;

/// <summary>
/// Dựng hub thật trong tiến trình và nối một agent giả vào bằng chính <see cref="HubClient"/>
/// mà Cowork dùng — kiểm cả đường mạng, xác thực token, và vòng lệnh đi–về.
/// TestServer không nói WebSocket nên client dùng long-polling; token khi đó đi qua header,
/// đúng nhánh thứ hai của <c>AgentHub.ExtractToken</c>.
/// </summary>
public sealed class HubIntegrationTests : IDisposable
{
    private const string Token = "token-test-0123456789abc";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly WebApplicationFactory<Program> _factory;

    // Sổ máy phải nằm trong thư mục tạm riêng của từng lần chạy. Mặc định nó là
    // src/Cowork.Hub/App_Data — chạy hub ở local một lần là test dính luôn máy để lại ở đó.
    private readonly string _storeDirectory =
        Path.Combine(Path.GetTempPath(), "cowork-hub-test-" + Guid.NewGuid().ToString("N"));

    // Với minimal hosting, cấu hình thêm qua WithWebHostBuilder bị nạp TRƯỚC appsettings.json
    // nên bị file đó đè lại; biến môi trường thì được nạp sau, nên đè được giá trị mẫu.
    private static readonly Dictionary<string, string> Environment = new()
    {
        ["Web__Password"] = "mat-khau-test-123",
        ["Agents__0__Name"] = "may-test",
        ["Agents__0__Token"] = Token,
    };

    public HubIntegrationTests()
    {
        foreach (var (key, value) in Environment)
            System.Environment.SetEnvironmentVariable(key, value);

        System.Environment.SetEnvironmentVariable(
            "AgentStorePath", Path.Combine(_storeDirectory, "agents.json"));

        _factory = new WebApplicationFactory<Program>();
    }

    private sealed class FakeAgent : IAgentHost
    {
        public Guid AppId { get; } = Guid.NewGuid();

        public List<RemoteCommand> Received { get; } = new();

        public MachineSnapshot BuildSnapshot() => new("PC-TEST", "test", DateTimeOffset.Now, new[]
        {
            new AppSnapshot(AppId, "echo", string.Empty, true, false, AppRuntimeState.Idle, 0,
                null, null, null, "Thủ công", null),
        });

        public Task<CommandResult> ExecuteAsync(RemoteCommand command)
        {
            lock (Received)
                Received.Add(command);

            return Task.FromResult(new CommandResult(command.RequestId, true, "da chay"));
        }

        /// <summary>Ảnh giả: chỉ cần vài byte để kiểm rằng dữ liệu nhị phân đi trọn vẹn qua đường mạng.</summary>
        public byte[] Png { get; set; } = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

        public ScreenshotFailure Failure { get; set; } = ScreenshotFailure.None;

        public Task<ScreenshotResult> CaptureAsync(ScreenshotRequest request)
            => Task.FromResult(Failure == ScreenshotFailure.None
                ? new ScreenshotResult(request.RequestId, ScreenshotFailure.None, Png, 800, 600, DateTimeOffset.Now)
                : ScreenshotResult.Failed(request.RequestId, Failure, DateTimeOffset.Now));
    }

    private HubClient CreateClient(IAgentHost agent)
        => new(agent, NullLogger.Instance, builder =>
            builder.WithUrl(HubClient.BuildUrl(BaseAddress), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            }));

    private string BaseAddress => _factory.Server.BaseAddress.ToString();

    private MachineRegistry Registry => _factory.Services.GetRequiredService<MachineRegistry>();

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }

        Assert.Fail("Chờ quá lâu: " + what);
    }

    [Fact]
    public async Task Agent_RegistersOverTheWire_AndAnswersCommands()
    {
        var agent = new FakeAgent();
        await using var client = CreateClient(agent);

        await client.StartAsync(BaseAddress, Token);
        await WaitUntilAsync(
            () => Registry.Machines.Any(m => m.Name == "may-test" && m.Online && m.Apps.Count == 1),
            "máy lên trực tuyến kèm một app");

        var machine = Registry.Machines.Single(m => m.Name == "may-test");
        Assert.Equal("PC-TEST", machine.HostName);
        Assert.Equal("echo", machine.Apps[0].Name);

        var outcome = await Registry.SendAsync("may-test", agent.AppId, RemoteCommandKind.Run, Patience);

        Assert.True(outcome.Ok, outcome.AgentMessage);
        Assert.Equal("da chay", outcome.AgentMessage);
        var received = Assert.Single(agent.Received);
        Assert.Equal(agent.AppId, received.AppId);
        Assert.Equal(RemoteCommandKind.Run, received.Kind);

        await client.StopAsync();
        await WaitUntilAsync(() => !Registry.Machines.Single(m => m.Name == "may-test").Online, "máy ngoại tuyến");
    }

    /// <summary>
    /// Lời hứa của trang cấp token: máy thêm trên web nối được ngay. Nếu sổ agent còn được
    /// dựng một lần lúc khởi động như trước, token mới sẽ bị từ chối cho tới lần deploy sau.
    /// </summary>
    [Fact]
    public async Task TokenIssuedAtRuntime_LetsANewMachineConnect_WithoutRestart()
    {
        var admin = _factory.Services.GetRequiredService<AgentAdmin>();
        var token = AgentAdmin.NewToken();

        Assert.True(admin.Add("may-moi", token).Ok);
        Assert.Contains(Registry.Machines, m => m.Name == "may-moi" && !m.Online);

        await using var client = CreateClient(new FakeAgent());
        await client.StartAsync(BaseAddress, token);

        await WaitUntilAsync(
            () => Registry.Machines.Any(m => m.Name == "may-moi" && m.Online),
            "máy vừa cấp token lên trực tuyến");
    }

    /// <summary>Thu hồi token thì máy đó không nối lại được nữa, cũng không cần khởi động hub.</summary>
    [Fact]
    public async Task RevokedToken_IsRefusedOnTheNextConnection()
    {
        var admin = _factory.Services.GetRequiredService<AgentAdmin>();
        var token = AgentAdmin.NewToken();
        Assert.True(admin.Add("may-tam", token).Ok);
        Assert.True(admin.Remove("may-tam").Ok);

        var states = new List<HubLinkState>();
        await using var client = CreateClient(new FakeAgent());
        client.StateChanged += (_, state) => { lock (states) states.Add(state); };

        await client.StartAsync(BaseAddress, token);
        await WaitUntilAsync(() => { lock (states) return states.Contains(HubLinkState.Failed); }, "client báo thất bại");

        Assert.DoesNotContain(Registry.Machines, m => m.Name == "may-tam");
    }

    /// <summary>
    /// Ảnh chụp là thông điệp lớn nhất đi qua đường này. Mức mặc định 32 KB của SignalR sẽ
    /// cắt kết nối, nên test đi qua chính đường mạng thật thay vì gọi thẳng registry.
    /// </summary>
    [Fact]
    public async Task Screenshot_TravelsOverTheWire_Intact()
    {
        var agent = new FakeAgent { Png = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray() };
        await using var client = CreateClient(agent);

        await client.StartAsync(BaseAddress, Token);
        await WaitUntilAsync(() => Registry.Machines.Any(m => m.Name == "may-test" && m.Online), "máy lên trực tuyến");

        var result = await Registry.RequestScreenshotAsync("may-test", agent.AppId, Patience);

        Assert.True(result.Ok);
        Assert.Equal(agent.Png, result.Png);
        Assert.Equal(800, result.Width);
    }

    /// <summary>Lý do không chụp được phải về tới web nguyên vẹn, để trang nói đúng chuyện gì đã xảy ra.</summary>
    [Fact]
    public async Task Screenshot_CarriesTheFailureReasonBack()
    {
        var agent = new FakeAgent { Failure = ScreenshotFailure.NoWindow };
        await using var client = CreateClient(agent);

        await client.StartAsync(BaseAddress, Token);
        await WaitUntilAsync(() => Registry.Machines.Any(m => m.Name == "may-test" && m.Online), "máy lên trực tuyến");

        var result = await Registry.RequestScreenshotAsync("may-test", agent.AppId, Patience);

        Assert.False(result.Ok);
        Assert.Equal(ScreenshotFailure.NoWindow, result.Failure);
        Assert.Empty(result.Png);
    }

    [Fact]
    public async Task WrongToken_IsRejected_AndTheMachineNeverShowsOnline()
    {
        var states = new List<HubLinkState>();
        await using var client = CreateClient(new FakeAgent());
        client.StateChanged += (_, state) => { lock (states) states.Add(state); };

        await client.StartAsync(BaseAddress, "token-sai-0123456789abcdef");
        await WaitUntilAsync(() => { lock (states) return states.Contains(HubLinkState.Failed); }, "client báo thất bại");

        Assert.False(Registry.Machines.Single(m => m.Name == "may-test").Online);
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach (var key in Environment.Keys)
            System.Environment.SetEnvironmentVariable(key, null);

        System.Environment.SetEnvironmentVariable("AgentStorePath", null);
        if (Directory.Exists(_storeDirectory))
            Directory.Delete(_storeDirectory, recursive: true);
    }
}
