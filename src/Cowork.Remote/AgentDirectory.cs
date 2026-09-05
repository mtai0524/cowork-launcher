using System.Security.Cryptography;
using System.Text;

namespace Cowork.Remote;

/// <summary>Một máy được phép kết nối: tên hiển thị và mã bí mật nó phải trình ra.</summary>
public sealed record AgentCredential(string Name, string Token);

/// <summary>
/// Sổ các agent được phép. Tên máy lấy từ mã token chứ không tin tên agent tự khai —
/// nhờ vậy một agent có token của máy A không thể tự nhận mình là máy B.
/// </summary>
public sealed class AgentDirectory
{
    /// <summary>Token ngắn hơn ngần này coi như chưa được đặt, từ chối luôn cho an toàn.</summary>
    public const int MinimumTokenLength = 16;

    private readonly List<AgentCredential> _agents = new();

    public AgentDirectory(IEnumerable<AgentCredential> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        foreach (var agent in agents)
        {
            if (string.IsNullOrWhiteSpace(agent.Name))
                throw new ArgumentException("Agent phải có tên.", nameof(agents));

            if (string.IsNullOrWhiteSpace(agent.Token) || agent.Token.Length < MinimumTokenLength)
                throw new ArgumentException($"Token của agent '{agent.Name}' phải dài ít nhất {MinimumTokenLength} ký tự.", nameof(agents));

            if (_agents.Any(a => string.Equals(a.Name, agent.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Tên agent '{agent.Name}' bị trùng.", nameof(agents));

            if (_agents.Any(a => a.Token == agent.Token))
                throw new ArgumentException($"Token của agent '{agent.Name}' trùng với agent khác.", nameof(agents));

            _agents.Add(new AgentCredential(agent.Name.Trim(), agent.Token));
        }
    }

    public IReadOnlyList<string> Names => _agents.Select(a => a.Name).ToList();

    /// <summary>So token theo thời gian cố định để không lộ độ dài/độ khớp qua thời gian phản hồi.</summary>
    public bool TryResolve(string? token, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrEmpty(token))
            return false;

        var presented = Encoding.UTF8.GetBytes(token);

        foreach (var agent in _agents)
        {
            var expected = Encoding.UTF8.GetBytes(agent.Token);
            if (expected.Length == presented.Length && CryptographicOperations.FixedTimeEquals(expected, presented))
            {
                name = agent.Name;
                return true;
            }
        }

        return false;
    }
}
