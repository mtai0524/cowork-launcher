using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Vì sao app bị coi là treo. <see cref="None"/> nghĩa là còn khoẻ.</summary>
public enum HealthFailure
{
    None = 0,

    /// <summary>Không có output trong quá thời gian cho phép.</summary>
    Silence,

    /// <summary>Thăm dò cổng/URL lỗi liên tiếp quá ngưỡng.</summary>
    ProbeFailed,

    /// <summary>Output xuất hiện mẫu báo lỗi.</summary>
    FailurePattern,
}

/// <summary>Việc cần làm ở một nhịp kiểm tra.</summary>
public enum HealthTickAction
{
    /// <summary>Chưa tới lúc (đang trong thời gian ân hạn) hoặc không có gì để thăm dò.</summary>
    Wait = 0,

    /// <summary>Thăm dò cổng/URL.</summary>
    Probe,

    /// <summary>App đã treo, dừng nó.</summary>
    Terminate,
}

public sealed record HealthTickDecision(HealthTickAction Action, HealthFailure Failure)
{
    public static readonly HealthTickDecision Wait = new(HealthTickAction.Wait, HealthFailure.None);
    public static readonly HealthTickDecision Probe = new(HealthTickAction.Probe, HealthFailure.None);

    public static HealthTickDecision Terminate(HealthFailure failure) => new(HealthTickAction.Terminate, failure);
}

/// <summary>
/// Các quyết định của kiểm tra sức khoẻ dưới dạng hàm thuần, nhận <c>now</c> làm tham số để kiểm
/// thử được thời gian ân hạn và ngưỡng im lặng mà không phải chờ thật.
/// </summary>
public static class HealthPolicy
{
    /// <summary>Chu kỳ kiểm tra ngắn nhất; ngắn hơn chỉ tốn CPU và làm phiền app.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>Watchdog theo output chỉ có nghĩa khi Cowork thật sự nhận được output.</summary>
    public static bool OutputIsObservable(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.CaptureOutput && !app.RunAsAdministrator;
    }

    /// <summary>App có gì để theo dõi không, tính cả chuyện output có thu được hay không.</summary>
    public static bool AppliesTo(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var check = app.HealthCheck;
        return check.HasProbe || (check.HasOutputWatch && OutputIsObservable(app));
    }

    public static bool InStartupGrace(HealthCheck check, DateTimeOffset startedAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(check);
        return now - startedAt < TimeSpan.FromSeconds(Math.Max(0, check.StartupGraceSeconds));
    }

    public static bool IsSilentTooLong(HealthCheck check, DateTimeOffset lastOutputAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.SilenceMinutes > 0 && now - lastOutputAt >= TimeSpan.FromMinutes(check.SilenceMinutes);
    }

    public static bool ReachedFailureThreshold(HealthCheck check, int consecutiveFailures)
    {
        ArgumentNullException.ThrowIfNull(check);
        return consecutiveFailures >= Math.Max(1, check.FailureThreshold);
    }

    /// <summary>Chu kỳ kiểm tra thật sự dùng: giá trị đã đặt, nhưng không ngắn hơn <paramref name="minimum"/>.</summary>
    public static TimeSpan CheckInterval(HealthCheck check, TimeSpan? minimum = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        var floor = minimum ?? MinimumInterval;
        var configured = TimeSpan.FromSeconds(Math.Max(0, check.IntervalSeconds));
        return configured < floor ? floor : configured;
    }

    public static TimeSpan ProbeTimeout(HealthCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return TimeSpan.FromSeconds(Math.Max(1, check.TimeoutSeconds));
    }

    /// <summary>Quyết định ở một nhịp kiểm tra: chờ, thăm dò, hay dừng vì im lặng quá lâu.</summary>
    public static HealthTickDecision OnTick(
        HealthCheck check,
        bool outputObservable,
        DateTimeOffset startedAt,
        DateTimeOffset lastOutputAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (InStartupGrace(check, startedAt, now))
            return HealthTickDecision.Wait;

        if (outputObservable && IsSilentTooLong(check, lastOutputAt, now))
            return HealthTickDecision.Terminate(HealthFailure.Silence);

        return check.HasProbe ? HealthTickDecision.Probe : HealthTickDecision.Wait;
    }
}

/// <summary>Bộ mẫu báo lỗi đã biên dịch; mẫu không phải regex hợp lệ được so như chuỗi thường.</summary>
public sealed class FailurePatternSet
{
    public static readonly FailurePatternSet Empty = new(Array.Empty<string>());

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Output có thể rất dày; một regex tệ (backtracking) không được kéo cả luồng đọc output đứng lại.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly List<(string Source, Regex Regex)> _patterns = new();

    public FailurePatternSet(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        foreach (var raw in patterns)
        {
            var source = raw?.Trim();
            if (string.IsNullOrEmpty(source))
                continue;

            _patterns.Add((source, Compile(source)));
        }
    }

    public int Count => _patterns.Count;

    public static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Mẫu đầu tiên khớp với dòng, null nếu dòng sạch.</summary>
    public string? Match(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        foreach (var (source, regex) in _patterns)
        {
            try
            {
                if (regex.IsMatch(line))
                    return source;
            }
            catch (RegexMatchTimeoutException)
            {
                // Coi như không khớp; thà bỏ sót một dòng còn hơn nghẽn output.
            }
        }

        return null;
    }

    private static Regex Compile(string source)
    {
        try
        {
            return new Regex(source, Options, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return new Regex(Regex.Escape(source), Options, MatchTimeout);
        }
    }
}

/// <summary>Đọc chuỗi mục tiêu người dùng gõ cho từng kiểu thăm dò.</summary>
public static class HealthTarget
{
    public const string DefaultHost = "localhost";

    /// <summary>Chấp nhận "8080", ":8080", "host:8080" và "[::1]:8080". Thiếu host thì là máy này.</summary>
    public static bool TryParseTcp(string? target, out string host, out int port)
    {
        host = DefaultHost;
        port = 0;

        var text = (target ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var alone))
        {
            port = alone;
            return IsPort(port);
        }

        var colon = text.LastIndexOf(':');
        if (colon < 0)
            return false;

        if (!int.TryParse(text[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || !IsPort(port))
            return false;

        var hostPart = text[..colon].Trim().Trim('[', ']');
        if (hostPart.Contains('/') || hostPart.Any(char.IsWhiteSpace))
        {
            // "http://host:8080" là URL chứ không phải host:cổng — người dùng chọn nhầm kiểu thăm dò.
            return false;
        }

        if (hostPart.Length > 0)
            host = hostPart;

        return true;
    }

    /// <summary>Chấp nhận URL http/https; thiếu scheme thì hiểu là http://.</summary>
    public static bool TryParseHttp(string? target, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;

        var text = (target ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // "8080" là cổng người dùng gõ nhầm vào ô HTTP. Phải bắt ở đây: Uri nhận
            // "http://8080" và gói con số thành địa chỉ IPv4 (0.0.31.144), nên xét host sau đó là muộn.
            if (HostPartOf(text).All(char.IsAsciiDigit))
                return false;

            text = "http://" + text;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Phần tên máy của một địa chỉ chưa có scheme: cắt trước dấu <c>:</c> đầu tiên và trước đường dẫn.</summary>
    private static string HostPartOf(string authority)
    {
        var end = authority.IndexOfAny(new[] { ':', '/', '?', '#' });
        return end < 0 ? authority : authority[..end];
    }

    public static bool IsValid(HealthProbeKind kind, string? target) => kind switch
    {
        HealthProbeKind.TcpPort => TryParseTcp(target, out _, out _),
        HealthProbeKind.HttpGet => TryParseHttp(target, out _),
        _ => true,
    };

    private static bool IsPort(int port) => port is >= 1 and <= 65535;
}
