namespace Cowork.Remote;

/// <summary>Kết quả một lần thêm/bớt máy, ở dạng mã để tầng giao diện tự dịch.</summary>
public enum AgentChangeStatus
{
    Ok = 0,
    NameEmpty,
    NameDuplicate,
    TokenTooShort,
    TokenDuplicate,
    NotFound,
    SaveFailed,
}

/// <summary><see cref="Detail"/> là lời của hệ điều hành khi ghi file hỏng — kèm thêm, không thay cho câu đã dịch.</summary>
public sealed record AgentChange(AgentChangeStatus Status, string? Detail = null)
{
    public bool Ok => Status == AgentChangeStatus.Ok;

    public static readonly AgentChange Success = new(AgentChangeStatus.Ok);
}

/// <summary>
/// Thêm/bớt máy như một thao tác trọn vẹn: sổ trong bộ nhớ, file dưới đĩa, và bảng máy
/// trên web phải cùng đổi hoặc cùng không đổi.
///
/// Ghi đĩa hỏng thì hoàn tác thay đổi trong bộ nhớ. Nếu không, web sẽ hiện một máy mới
/// mà lần khởi động sau nó biến mất — người dùng dán token xong vẫn không nối được và
/// không có gì chỉ ra vì sao.
/// </summary>
public sealed class AgentAdmin
{
    private readonly AgentDirectory _directory;
    private readonly IAgentStore _store;
    private readonly MachineRegistry _registry;

    public AgentAdmin(AgentDirectory directory, IAgentStore store, MachineRegistry registry)
    {
        _directory = directory;
        _store = store;
        _registry = registry;
    }

    public IReadOnlyList<AgentCredential> Agents => _directory.Agents;

    public static string NewToken() => AgentDirectory.NewToken();

    public AgentChange Add(string? name, string? token)
    {
        if (!_directory.TryAdd(name, token, out var rejection))
            return new AgentChange(Translate(rejection));

        var added = name!.Trim();

        if (!TrySave(out var detail))
        {
            _directory.Remove(added);
            return new AgentChange(AgentChangeStatus.SaveFailed, detail);
        }

        _registry.Track(added);
        return AgentChange.Success;
    }

    public AgentChange Remove(string? name)
    {
        var existing = _directory.Agents
            .FirstOrDefault(a => string.Equals(a.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (existing is null || !_directory.Remove(name))
            return new AgentChange(AgentChangeStatus.NotFound);

        if (!TrySave(out var detail))
        {
            _directory.TryAdd(existing.Name, existing.Token, out _);
            return new AgentChange(AgentChangeStatus.SaveFailed, detail);
        }

        _registry.Forget(existing.Name);
        return AgentChange.Success;
    }

    private bool TrySave(out string? detail)
    {
        detail = null;

        try
        {
            _store.Save(_directory.Agents);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static AgentChangeStatus Translate(AgentRejection rejection) => rejection switch
    {
        AgentRejection.NameEmpty => AgentChangeStatus.NameEmpty,
        AgentRejection.NameDuplicate => AgentChangeStatus.NameDuplicate,
        AgentRejection.TokenDuplicate => AgentChangeStatus.TokenDuplicate,
        _ => AgentChangeStatus.TokenTooShort,
    };
}
