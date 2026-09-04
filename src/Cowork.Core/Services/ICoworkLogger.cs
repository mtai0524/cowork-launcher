using System.Text;

namespace Cowork.Core.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

public interface ICoworkLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);

    void Info(string message) => Log(LogLevel.Info, message);
    void Warning(string message) => Log(LogLevel.Warning, message);
    void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
}

/// <summary>Logger nối đuôi file, an toàn khi nhiều luồng cùng ghi.</summary>
public sealed class FileLogger : ICoworkLogger
{
    private readonly CoworkPaths _paths;
    private readonly object _gate = new();

    public FileLogger(CoworkPaths paths) => _paths = paths;

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(message);

        if (exception is not null)
            builder.AppendLine().Append(exception);

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_paths.AppLogFile, builder.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Không để lỗi ghi log làm sập ứng dụng.
            }
        }
    }
}

/// <summary>Dùng trong unit test hoặc khi tắt log.</summary>
public sealed class NullLogger : ICoworkLogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }
}
