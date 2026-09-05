using System.Text.Json;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Remote;
using Cowork.Remote.Contracts;
using Xunit;

namespace Cowork.Tests;

public class AgentDirectoryTests
{
    private const string TokenA = "token-may-a-0123456789";
    private const string TokenB = "token-may-b-0123456789";

    private static AgentDirectory Directory() => new(new[]
    {
        new AgentCredential("may-a", TokenA),
        new AgentCredential("may-b", TokenB),
    });

    [Fact]
    public void ResolvesMachineNameFromToken()
    {
        Assert.True(Directory().TryResolve(TokenB, out var name));
        Assert.Equal("may-b", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token-khong-ton-tai-0123")]
    [InlineData("token-may-a-012345678")]
    public void RejectsUnknownTokens(string? token)
        => Assert.False(Directory().TryResolve(token, out _));

    [Fact]
    public void ShortToken_IsRefusedAtConstruction()
        => Assert.Throws<ArgumentException>(() => new AgentDirectory(new[] { new AgentCredential("x", "ngan") }));

    [Fact]
    public void DuplicateName_IsRefused()
        => Assert.Throws<ArgumentException>(() => new AgentDirectory(new[]
        {
            new AgentCredential("may-a", TokenA),
            new AgentCredential("MAY-A", TokenB),
        }));

    [Fact]
    public void Names_ListEveryConfiguredMachine()
        => Assert.Equal(new[] { "may-a", "may-b" }, Directory().Names);
}

public class SnapshotBuilderTests
{
    [Fact]
    public void CopiesModelAndRuntimeFields_AndComputesNextRun()
    {
        var now = new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(7));
        var app = new ManagedApp
        {
            Name = "bao-cao",
            Group = "sang",
            ExecutablePath = @"C:\tools\report.exe",
            KeepAlive = true,
            LastExitCode = 0,
            LastRunAt = now.AddDays(-1),
            Schedule = new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleKind.DailyAtTimes,
                Times = { new TimeSpan(18, 0, 0) },
            },
        };

        var snapshot = SnapshotBuilder.Build(app, AppRuntimeState.Running, 4242, null, now);

        Assert.Equal(app.Id, snapshot.Id);
        Assert.Equal("bao-cao", snapshot.Name);
        Assert.Equal("sang", snapshot.Group);
        Assert.True(snapshot.KeepAlive);
        Assert.Equal(AppRuntimeState.Running, snapshot.State);
        Assert.Equal(4242, snapshot.ProcessId);
        Assert.Equal(0, snapshot.LastExitCode);
        Assert.Equal(now.AddHours(9), snapshot.NextRunAt);
        Assert.Contains("18:00", snapshot.ScheduleSummary);
    }
}

/// <summary>Ghi lại lệnh đã gửi; có thể bảo nó ném lỗi để test đường thất bại.</summary>
internal sealed class RecordingSender : IAgentCommandSender
{
    public List<(string ConnectionId, RemoteCommand Command)> Sent { get; } = new();

    public bool Throw { get; set; }

    public Task SendAsync(string connectionId, RemoteCommand command, CancellationToken cancellationToken)
    {
        if (Throw)
            throw new InvalidOperationException("mat mang");

        Sent.Add((connectionId, command));
        return Task.CompletedTask;
    }
}

public class MachineRegistryTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(7));

    private static (MachineRegistry Registry, RecordingSender Sender, FixedClock Clock) Build()
    {
        var sender = new RecordingSender();
        var clock = new FixedClock(Start);
        return (new MachineRegistry(sender, clock, new[] { "may-a", "may-b" }), sender, clock);
    }

    private static AppSnapshot App(string name, AppRuntimeState state = AppRuntimeState.Idle, Guid? id = null)
        => new(id ?? Guid.NewGuid(), name, string.Empty, true, false, state, 0, null, null, null, "Thủ công", null);

    private static MachineSnapshot Snapshot(params AppSnapshot[] apps)
        => new("PC-A", "1.0", Start, apps);

    [Fact]
    public void KnownMachines_StartOffline_AndVisible()
    {
        var (registry, _, _) = Build();

        var machines = registry.Machines;

        Assert.Equal(2, machines.Count);
        Assert.All(machines, m => Assert.False(m.Online));
        Assert.All(machines, m => Assert.Null(m.LastSeen));
    }

    [Fact]
    public void Connected_MarksOnline_AndRegister_StoresApps()
    {
        var (registry, _, _) = Build();

        registry.Connected("may-a", "c1");
        Assert.True(registry.Register("c1", Snapshot(App("x"), App("y"))));

        var machine = registry.Machines.Single(m => m.Name == "may-a");
        Assert.True(machine.Online);
        Assert.Equal("PC-A", machine.HostName);
        Assert.Equal(new[] { "x", "y" }, machine.Apps.Select(a => a.Name));
    }

    [Fact]
    public void Register_FromUnknownConnection_IsIgnored()
    {
        var (registry, _, _) = Build();

        Assert.False(registry.Register("la", Snapshot(App("x"))));
        Assert.All(registry.Machines, m => Assert.Empty(m.Apps));
    }

    [Fact]
    public void UpdateApp_ReplacesTheRowInPlace_OrAppends()
    {
        var (registry, _, _) = Build();
        var id = Guid.NewGuid();
        registry.Connected("may-a", "c1");
        registry.Register("c1", Snapshot(App("x", id: id), App("y")));

        registry.UpdateApp("c1", App("x", AppRuntimeState.Running, id));
        registry.UpdateApp("c1", App("z"));

        var apps = registry.Machines.Single(m => m.Name == "may-a").Apps;
        Assert.Equal(new[] { "x", "y", "z" }, apps.Select(a => a.Name));
        Assert.Equal(AppRuntimeState.Running, apps[0].State);
    }

    [Fact]
    public void Disconnected_KeepsLastSnapshot_ButMarksOffline()
    {
        var (registry, _, clock) = Build();
        registry.Connected("may-a", "c1");
        registry.Register("c1", Snapshot(App("x")));

        clock.Advance(TimeSpan.FromMinutes(5));
        registry.Disconnected("c1");

        var machine = registry.Machines.Single(m => m.Name == "may-a");
        Assert.False(machine.Online);
        Assert.Equal(Start.AddMinutes(5), machine.LastSeen);
        Assert.Single(machine.Apps);
    }

    [Fact]
    public void Reconnect_NewConnectionWins_AndStaleDisconnectIsIgnored()
    {
        var (registry, sender, _) = Build();
        registry.Connected("may-a", "old");
        registry.Connected("may-a", "new");

        // Kết nối cũ đóng muộn: không được làm máy thành ngoại tuyến.
        registry.Disconnected("old");

        Assert.True(registry.Machines.Single(m => m.Name == "may-a").Online);

        _ = registry.SendAsync("may-a", Guid.NewGuid(), RemoteCommandKind.Run, TimeSpan.FromMilliseconds(50));
        Assert.Equal("new", Assert.Single(sender.Sent).ConnectionId);
    }

    [Fact]
    public async Task SendAsync_UnknownOrOfflineMachine_FailsWithoutSending()
    {
        var (registry, sender, _) = Build();

        var unknown = await registry.SendAsync("khong-co", Guid.NewGuid(), RemoteCommandKind.Run, TimeSpan.FromSeconds(1));
        var offline = await registry.SendAsync("may-a", Guid.NewGuid(), RemoteCommandKind.Run, TimeSpan.FromSeconds(1));

        Assert.Equal(CommandFailure.MachineUnknown, unknown.Failure);
        Assert.Equal(CommandFailure.MachineOffline, offline.Failure);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task SendAsync_DeliversToTheConnection_AndResolvesWhenTheAgentAnswers()
    {
        var (registry, sender, _) = Build();
        registry.Connected("may-a", "c1");
        var appId = Guid.NewGuid();

        var pending = registry.SendAsync("may-a", appId, RemoteCommandKind.Stop, TimeSpan.FromSeconds(5));

        var (connectionId, command) = Assert.Single(sender.Sent);
        Assert.Equal("c1", connectionId);
        Assert.Equal(appId, command.AppId);
        Assert.Equal(RemoteCommandKind.Stop, command.Kind);

        Assert.True(registry.Complete(new CommandResult(command.RequestId, true, "da dung")));

        var outcome = await pending;
        Assert.True(outcome.Ok);
        Assert.Equal(CommandFailure.None, outcome.Failure);
        Assert.Equal("da dung", outcome.AgentMessage);
    }

    [Fact]
    public async Task SendAsync_TimesOut_WhenTheAgentStaysSilent()
    {
        var (registry, _, _) = Build();
        registry.Connected("may-a", "c1");

        var outcome = await registry.SendAsync("may-a", Guid.NewGuid(), RemoteCommandKind.Run, TimeSpan.FromMilliseconds(100));

        Assert.False(outcome.Ok);
        Assert.Equal(CommandFailure.Timeout, outcome.Failure);
    }

    [Fact]
    public async Task SendAsync_ReportsSendFailures()
    {
        var (registry, sender, _) = Build();
        registry.Connected("may-a", "c1");
        sender.Throw = true;

        var outcome = await registry.SendAsync("may-a", Guid.NewGuid(), RemoteCommandKind.Run, TimeSpan.FromSeconds(1));

        Assert.Equal(CommandFailure.SendFailed, outcome.Failure);
        Assert.Equal("mat mang", outcome.AgentMessage);
    }

    [Fact]
    public void Complete_ForARequestNobodyIsWaitingOn_IsIgnored()
        => Assert.False(Build().Registry.Complete(new CommandResult(Guid.NewGuid(), true, "muon")));

    [Fact]
    public void Changed_FiresOnEveryUpdate()
    {
        var (registry, _, _) = Build();
        var fired = 0;
        registry.Changed += () => fired++;

        registry.Connected("may-a", "c1");
        registry.Register("c1", Snapshot(App("x")));
        registry.UpdateApp("c1", App("y"));
        registry.Disconnected("c1");

        Assert.Equal(4, fired);
    }
}

public class ContractSerializationTests
{
    [Fact]
    public void MachineSnapshot_RoundTripsThroughJson()
    {
        var original = new MachineSnapshot("PC-A", "1.0", new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(7)),
            new[]
            {
                new AppSnapshot(Guid.NewGuid(), "x", "g", true, true, AppRuntimeState.WaitingRestart, 7,
                    DateTimeOffset.Now, 3, DateTimeOffset.Now.AddHours(1), "Mỗi 30 phút · mỗi ngày", "loi"),
            });

        var json = JsonSerializer.Serialize(original);
        var copy = JsonSerializer.Deserialize<MachineSnapshot>(json);

        Assert.NotNull(copy);
        Assert.Equal(original.HostName, copy!.HostName);
        var app = Assert.Single(copy.Apps);
        Assert.Equal(AppRuntimeState.WaitingRestart, app.State);
        Assert.Equal(3, app.LastExitCode);
        Assert.Equal("Mỗi 30 phút · mỗi ngày", app.ScheduleSummary);
    }

    [Fact]
    public void RemoteCommand_RoundTripsThroughJson()
    {
        var original = new RemoteCommand(Guid.NewGuid(), Guid.NewGuid(), RemoteCommandKind.Restart);

        var copy = JsonSerializer.Deserialize<RemoteCommand>(JsonSerializer.Serialize(original));

        Assert.Equal(original, copy);
    }
}
