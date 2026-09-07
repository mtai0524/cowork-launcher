using Cowork.Core.Services;

namespace Cowork.Hub;

/// <summary>
/// Bắc <see cref="ICoworkLogger"/> của tầng Core sang hệ log của ASP.NET, để chuyện xảy ra
/// trong <see cref="Cowork.Remote.JsonAgentStore"/> hiện cùng chỗ với log của hub.
/// </summary>
public sealed class HubLogger : ICoworkLogger
{
    private readonly ILogger _inner;

    public HubLogger(ILogger inner) => _inner = inner;

    public void Log(Cowork.Core.Services.LogLevel level, string message, Exception? exception = null)
        => _inner.Log(Map(level), exception, "{Message}", message);

    private static Microsoft.Extensions.Logging.LogLevel Map(Cowork.Core.Services.LogLevel level) => level switch
    {
        Cowork.Core.Services.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        Cowork.Core.Services.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        Cowork.Core.Services.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        _ => Microsoft.Extensions.Logging.LogLevel.Information,
    };
}
