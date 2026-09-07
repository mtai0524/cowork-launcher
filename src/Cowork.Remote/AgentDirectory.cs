using System.Security.Cryptography;
using System.Text;

namespace Cowork.Remote;

/// <summary>Một máy được phép kết nối: tên hiển thị và mã bí mật nó phải trình ra.</summary>
public sealed record AgentCredential(string Name, string Token);

/// <summary>
/// Vì sao một cặp tên + token bị từ chối. Trả về mã thay vì câu chữ để tầng giao diện
/// tự dịch — sổ agent nằm dưới tầng ngôn ngữ nên không được tự dựng chuỗi hiển thị.
/// </summary>
public enum AgentRejection
{
    None = 0,
    NameEmpty,
    NameDuplicate,
    TokenTooShort,
    TokenDuplicate,
}

/// <summary>
/// Sổ các agent được phép. Tên máy lấy từ mã token chứ không tin tên agent tự khai —
/// nhờ vậy một agent có token của máy A không thể tự nhận mình là máy B.
///
/// Sửa được lúc chạy: trang Máy trên web thêm/bớt máy mà không phải deploy lại hub.
/// Mọi thao tác đọc lẫn ghi đều dưới một khoá, vì kết nối agent đến từ luồng khác
/// với luồng đang dựng giao diện.
/// </summary>
public sealed class AgentDirectory
{
    /// <summary>Token ngắn hơn ngần này coi như chưa được đặt, từ chối luôn cho an toàn.</summary>
    public const int MinimumTokenLength = 16;

    /// <summary>Số byte ngẫu nhiên của một token tự sinh. 24 byte ⇒ 48 ký tự hex.</summary>
    private const int GeneratedTokenBytes = 24;

    private readonly object _gate = new();
    private readonly List<AgentCredential> _agents = new();

    public AgentDirectory(IEnumerable<AgentCredential> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            var rejection = Check(agent.Name, agent.Token);
            if (rejection != AgentRejection.None)
                throw new ArgumentException(Explain(agent.Name, rejection), nameof(agents));

            _agents.Add(new AgentCredential(agent.Name.Trim(), agent.Token));
        }
    }

    /// <summary>Bắn sau khi danh sách máy đổi, để nơi khác lưu xuống đĩa và vẽ lại giao diện.</summary>
    public event Action? Changed;

    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
                return _agents.Select(a => a.Name).ToList();
        }
    }

    public IReadOnlyList<AgentCredential> Agents
    {
        get
        {
            lock (_gate)
                return _agents.ToList();
        }
    }

    /// <summary>Sinh token mới. Hex chứ không Base64 để dán qua dòng lệnh hay URL đều không phải thoát ký tự.</summary>
    public static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(GeneratedTokenBytes)).ToLowerInvariant();

    /// <summary>Thêm một máy. Trả về false kèm lý do khi tên hoặc token không hợp lệ.</summary>
    public bool TryAdd(string? name, string? token, out AgentRejection rejection)
    {
        lock (_gate)
        {
            rejection = Check(name, token);
            if (rejection != AgentRejection.None)
                return false;

            _agents.Add(new AgentCredential(name!.Trim(), token!));
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Bỏ một máy. Token của nó hết hiệu lực ngay ở lần kết nối kế tiếp.</summary>
    public bool Remove(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (_gate)
        {
            var index = _agents.FindIndex(a => string.Equals(a.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            _agents.RemoveAt(index);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>So token theo thời gian cố định để không lộ độ dài/độ khớp qua thời gian phản hồi.</summary>
    public bool TryResolve(string? token, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrEmpty(token))
            return false;

        var presented = Encoding.UTF8.GetBytes(token);

        lock (_gate)
        {
            foreach (var agent in _agents)
            {
                var expected = Encoding.UTF8.GetBytes(agent.Token);
                if (expected.Length == presented.Length && CryptographicOperations.FixedTimeEquals(expected, presented))
                {
                    name = agent.Name;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gọi khi đang giữ <see cref="_gate"/>, hoặc từ hàm dựng khi chưa ai thấy đối tượng.</summary>
    private AgentRejection Check(string? name, string? token)
    {
        if (string.IsNullOrWhiteSpace(name))
            return AgentRejection.NameEmpty;

        if (string.IsNullOrWhiteSpace(token) || token.Length < MinimumTokenLength)
            return AgentRejection.TokenTooShort;

        if (_agents.Any(a => string.Equals(a.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return AgentRejection.NameDuplicate;

        if (_agents.Any(a => a.Token == token))
            return AgentRejection.TokenDuplicate;

        return AgentRejection.None;
    }

    /// <summary>Câu chữ cho ngoại lệ lúc khởi động — đi vào log của server, không phải giao diện.</summary>
    private static string Explain(string? name, AgentRejection rejection) => rejection switch
    {
        AgentRejection.NameEmpty => "Agent phải có tên.",
        AgentRejection.NameDuplicate => $"Tên agent '{name}' bị trùng.",
        AgentRejection.TokenDuplicate => $"Token của agent '{name}' trùng với agent khác.",
        _ => $"Token của agent '{name}' phải dài ít nhất {MinimumTokenLength} ký tự.",
    };
}
