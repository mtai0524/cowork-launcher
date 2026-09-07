using Cowork.Core.Services;
using Cowork.Remote;
using Xunit;

namespace Cowork.Tests;

/// <summary>Sổ agent trong bộ nhớ, đọc/ghi cùng lúc với việc agent nối vào.</summary>
public class AgentDirectoryMutationTests
{
    private const string TokenA = "token-may-a-0123456789";

    private static AgentDirectory Directory()
        => new(new[] { new AgentCredential("may-a", TokenA) });

    [Fact]
    public void NewToken_IsLongEnough_AndNeverRepeats()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => AgentDirectory.NewToken()).ToList();

        Assert.All(tokens, t => Assert.True(t.Length >= AgentDirectory.MinimumTokenLength));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    /// <summary>Điểm cốt lõi của trang web: cấp token xong là dùng được ngay, không cần khởi động lại hub.</summary>
    [Fact]
    public void AddedToken_ResolvesImmediately()
    {
        var directory = Directory();
        var token = AgentDirectory.NewToken();

        Assert.False(directory.TryResolve(token, out _));
        Assert.True(directory.TryAdd("may-b", token, out _));

        Assert.True(directory.TryResolve(token, out var name));
        Assert.Equal("may-b", name);
    }

    [Fact]
    public void RemovedToken_StopsResolving()
    {
        var directory = Directory();

        Assert.True(directory.Remove("MAY-A"));
        Assert.False(directory.TryResolve(TokenA, out _));
    }

    [Fact]
    public void Remove_UnknownMachine_ReportsFailure()
        => Assert.False(Directory().Remove("khong-co"));

    [Theory]
    [InlineData("", "token-hop-le-0123456789", AgentRejection.NameEmpty)]
    [InlineData("   ", "token-hop-le-0123456789", AgentRejection.NameEmpty)]
    [InlineData("may-b", "ngan", AgentRejection.TokenTooShort)]
    [InlineData("may-b", null, AgentRejection.TokenTooShort)]
    [InlineData("MAY-A", "token-hop-le-0123456789", AgentRejection.NameDuplicate)]
    [InlineData("may-b", TokenA, AgentRejection.TokenDuplicate)]
    public void RejectsBadInput(string? name, string? token, AgentRejection expected)
    {
        var directory = Directory();

        Assert.False(directory.TryAdd(name, token, out var rejection));
        Assert.Equal(expected, rejection);
        Assert.Single(directory.Agents);
    }

    [Fact]
    public void Add_TrimsName()
    {
        var directory = Directory();

        Assert.True(directory.TryAdd("  may-b  ", AgentDirectory.NewToken(), out _));
        Assert.Contains("may-b", directory.Names);
    }
}

/// <summary>Lưu danh sách máy xuống đĩa.</summary>
public class JsonAgentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cowork-agents-" + Guid.NewGuid().ToString("N"));

    private JsonAgentStore Store() => new(Path.Combine(_directory, "agents.json"), new NullLogger());

    [Fact]
    public void Load_BeforeAnythingSaved_ReturnsNull()
        => Assert.Null(Store().Load());

    [Fact]
    public void SavedAgents_ComeBackUnchanged()
    {
        var agents = new[]
        {
            new AgentCredential("may-a", "token-may-a-0123456789"),
            new AgentCredential("may-b", "token-may-b-0123456789"),
        };

        Store().Save(agents);

        Assert.Equal(agents, Store().Load());
    }

    /// <summary>Ghi đè phải thay trọn vẹn, không để lại đuôi của bản cũ dài hơn.</summary>
    [Fact]
    public void SavingTwice_LeavesOnlyTheSecondList()
    {
        var store = Store();
        store.Save(new[]
        {
            new AgentCredential("may-a", "token-may-a-0123456789"),
            new AgentCredential("may-b", "token-may-b-0123456789"),
        });

        store.Save(new[] { new AgentCredential("may-c", "token-may-c-0123456789") });

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("may-c", Assert.Single(loaded!).Name);
    }

    [Fact]
    public void CorruptFile_FallsBackToNull_InsteadOfThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "agents.json"), "{ khong phai json");

        Assert.Null(Store().Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>Thêm/bớt máy như một thao tác trọn vẹn trên cả ba nơi: sổ, đĩa, bảng máy.</summary>
public class AgentAdminTests
{
    private const string TokenA = "token-may-a-0123456789";

    private sealed class MemoryStore : IAgentStore
    {
        public List<AgentCredential>? Saved { get; private set; }

        public bool Throw { get; set; }

        public int Writes { get; private set; }

        public IReadOnlyList<AgentCredential>? Load() => Saved;

        public void Save(IReadOnlyList<AgentCredential> agents)
        {
            if (Throw)
                throw new IOException("dia chi doc");

            Writes++;
            Saved = agents.ToList();
        }
    }

    private static (AgentAdmin Admin, MemoryStore Store, MachineRegistry Registry) Build()
    {
        var directory = new AgentDirectory(new[] { new AgentCredential("may-a", TokenA) });
        var registry = new MachineRegistry(
            new RecordingSender(),
            new FixedClock(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(7))),
            directory.Names);
        var store = new MemoryStore();

        return (new AgentAdmin(directory, store, registry), store, registry);
    }

    [Fact]
    public void Add_PersistsAndShowsTheMachineOffline()
    {
        var (admin, store, registry) = Build();

        var change = admin.Add("may-b", AgentAdmin.NewToken());

        Assert.True(change.Ok);
        Assert.Equal(new[] { "may-a", "may-b" }, admin.Agents.Select(a => a.Name));
        Assert.Equal(2, store.Saved!.Count);

        var machine = registry.Machines.Single(m => m.Name == "may-b");
        Assert.False(machine.Online);
        Assert.Null(machine.LastSeen);
    }

    [Fact]
    public void Remove_PersistsAndDropsTheMachineFromTheBoard()
    {
        var (admin, store, registry) = Build();

        var change = admin.Remove("may-a");

        Assert.True(change.Ok);
        Assert.Empty(admin.Agents);
        Assert.Empty(store.Saved!);
        Assert.Empty(registry.Machines);
    }

    /// <summary>
    /// Máy đang nối mà bị thu hồi token: kết nối cũ còn treo nhưng không báo trạng thái
    /// lên được nữa — nếu không, máy đã bị gỡ vẫn hiện trên web tới khi socket đứt.
    /// </summary>
    [Fact]
    public void Remove_SilencesTheLiveConnection()
    {
        var (admin, _, registry) = Build();
        registry.Connected("may-a", "c1");

        Assert.True(admin.Remove("may-a").Ok);

        Assert.False(registry.Register("c1", new Cowork.Remote.Contracts.MachineSnapshot(
            "PC-A", "1.0", DateTimeOffset.Now, Array.Empty<Cowork.Remote.Contracts.AppSnapshot>())));
        Assert.Empty(registry.Machines);
    }

    [Fact]
    public void Add_WhenDiskFails_RollsBackEverything()
    {
        var (admin, store, registry) = Build();
        store.Throw = true;

        var change = admin.Add("may-b", AgentAdmin.NewToken());

        Assert.Equal(AgentChangeStatus.SaveFailed, change.Status);
        Assert.NotNull(change.Detail);
        Assert.Equal(new[] { "may-a" }, admin.Agents.Select(a => a.Name));
        Assert.DoesNotContain(registry.Machines, m => m.Name == "may-b");
    }

    [Fact]
    public void Remove_WhenDiskFails_KeepsTheMachine()
    {
        var (admin, store, registry) = Build();
        store.Throw = true;

        var change = admin.Remove("may-a");

        Assert.Equal(AgentChangeStatus.SaveFailed, change.Status);
        Assert.Equal(TokenA, Assert.Single(admin.Agents).Token);
        Assert.Contains(registry.Machines, m => m.Name == "may-a");
    }

    [Fact]
    public void Remove_UnknownMachine_ReportsNotFound_AndWritesNothing()
    {
        var (admin, store, _) = Build();

        Assert.Equal(AgentChangeStatus.NotFound, admin.Remove("khong-co").Status);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("", AgentChangeStatus.NameEmpty)]
    [InlineData("may-a", AgentChangeStatus.NameDuplicate)]
    public void Add_RejectedInput_WritesNothing(string name, AgentChangeStatus expected)
    {
        var (admin, store, _) = Build();

        Assert.Equal(expected, admin.Add(name, AgentAdmin.NewToken()).Status);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Add_ShortToken_ReportsTokenTooShort()
    {
        var (admin, _, _) = Build();

        Assert.Equal(AgentChangeStatus.TokenTooShort, admin.Add("may-b", "ngan").Status);
    }
}
