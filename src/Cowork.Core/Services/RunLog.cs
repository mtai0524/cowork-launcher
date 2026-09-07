using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Đọc lại output của một lần chạy đã kết thúc. Mỗi lần chạy một file nên chỉ việc đọc đúng file
/// đó — phần đáng nghĩ duy nhất là trần số dòng, vì một dịch vụ chạy cả ngày có thể để lại file
/// hàng trăm nghìn dòng mà bảng lịch sử thì không cần hết.
/// </summary>
public static class RunLog
{
    /// <summary>Số dòng tối đa nạp vào giao diện; phần đầu bị cắt, vì đuôi mới là chỗ có lỗi.</summary>
    public const int MaxLines = 5000;

    public sealed record Content(IReadOnlyList<string> Lines, bool Truncated, bool Exists);

    public static readonly Content Missing = new(Array.Empty<string>(), false, false);

    /// <summary>Đọc <paramref name="maxLines"/> dòng cuối của file log một lần chạy.</summary>
    public static Content Read(CoworkPaths paths, AppRunRecord record, int maxLines = MaxLines)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.OutputLogFile))
            return Missing;

        return ReadFile(paths.RunLogFile(record.OutputLogFile), maxLines);
    }

    /// <summary>Đường dẫn đầy đủ tới file log của một lần chạy, hoặc null nếu lần đó không thu output.</summary>
    public static string? PathOf(CoworkPaths paths, AppRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(record);

        return string.IsNullOrWhiteSpace(record.OutputLogFile) ? null : paths.RunLogFile(record.OutputLogFile);
    }

    private static Content ReadFile(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path))
                return Missing;

            // Đọc theo luồng với hàng đợi vòng: file lớn cũng không nuốt cả vào RAM.
            var tail = new Queue<string>();
            var total = 0;

            // FileShare.ReadWrite: file có thể đang được chính Cowork ghi tiếp.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            while (reader.ReadLine() is { } line)
            {
                total++;
                tail.Enqueue(line);
                if (tail.Count > maxLines)
                    tail.Dequeue();
            }

            return new Content(tail.ToList(), total > maxLines, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Missing;
        }
    }
}
