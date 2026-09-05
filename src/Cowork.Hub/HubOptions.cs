using System.Security.Cryptography;
using System.Text;

namespace Cowork.Hub;

/// <summary>Mục <c>Web</c> trong appsettings.json.</summary>
public sealed class WebOptions
{
    public string Password { get; set; } = string.Empty;
}

/// <summary>Một phần tử trong mảng <c>Agents</c> của appsettings.json.</summary>
public sealed class AgentOptions
{
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public static class HubOptions
{
    /// <summary>Giá trị giữ chỗ trong appsettings.json mẫu — hub từ chối chạy khi còn nguyên chúng.</summary>
    public const string PlaceholderPassword = "doi-mat-khau-nay";
    public const string PlaceholderToken = "doi-token-nay";

    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// Kiểm tra cấu hình trước khi mở cổng. Một hub ra lệnh chạy chương trình trên nhiều máy
    /// mà mật khẩu còn là mẫu thì tệ hơn là không chạy — nên ném lỗi thẳng.
    /// </summary>
    public static void Validate(WebOptions web, IReadOnlyList<AgentOptions> agents)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(agents);

        if (string.IsNullOrWhiteSpace(web.Password) || web.Password == PlaceholderPassword)
            throw new InvalidOperationException("Chưa đặt mật khẩu web: sửa Web:Password trong appsettings.json.");

        if (web.Password.Length < MinimumPasswordLength)
            throw new InvalidOperationException($"Web:Password phải dài ít nhất {MinimumPasswordLength} ký tự.");

        foreach (var agent in agents)
        {
            if (agent.Token == PlaceholderToken)
                throw new InvalidOperationException($"Agent '{agent.Name}' còn dùng token mẫu: đặt token riêng trong appsettings.json.");
        }
    }

    public static bool PasswordMatches(WebOptions web, string? presented)
    {
        if (string.IsNullOrEmpty(presented))
            return false;

        var expected = Encoding.UTF8.GetBytes(web.Password);
        var actual = Encoding.UTF8.GetBytes(presented);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
