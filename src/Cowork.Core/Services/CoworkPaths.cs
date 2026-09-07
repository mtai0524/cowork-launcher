namespace Cowork.Core.Services;

/// <summary>
/// Tập trung mọi đường dẫn dữ liệu của Cowork. Mặc định nằm trong
/// <c>%APPDATA%\Cowork</c> để không lẫn với thư mục cài đặt.
/// </summary>
public sealed class CoworkPaths
{
    public CoworkPaths(string? rootDirectory = null)
    {
        Root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cowork");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }

    public string Root { get; }

    /// <summary>Danh sách app + thiết lập chung.</summary>
    public string WorkspaceFile => Path.Combine(Root, "workspace.json");

    /// <summary>Lịch sử chạy.</summary>
    public string HistoryFile => Path.Combine(Root, "history.json");

    public string LogDirectory => Path.Combine(Root, "logs");

    /// <summary>Ảnh chụp workspace theo ngày, để cứu lại khi file chính bị ghi hỏng.</summary>
    public string BackupDirectory => Path.Combine(Root, "backups");

    public string WorkspaceBackupFile(DateTime day)
        => Path.Combine(BackupDirectory, $"workspace-{day:yyyyMMdd}.json");

    public string AppLogFile => Path.Combine(LogDirectory, $"cowork-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>
    /// Log output của <em>một lần chạy</em>. Tên bắt đầu bằng ngày để <see cref="LogPruner"/> dọn được,
    /// và mang mã lần chạy để bảng Lịch sử mở lại đúng lần đó.
    ///
    /// Mỗi lần chạy một file, thay vì gộp cả ngày của một app vào một file: gộp thì hai lần chạy
    /// chồng nhau sẽ trộn dòng vào nhau, và một lần chạy vắt qua nửa đêm bị cắt làm đôi.
    /// </summary>
    public static string RunLogFileName(Guid runId, DateTimeOffset startedAt)
        => $"run-{startedAt:yyyyMMdd}-{runId:N}.log";

    public string RunLogFile(string fileName) => Path.Combine(LogDirectory, fileName);
}
