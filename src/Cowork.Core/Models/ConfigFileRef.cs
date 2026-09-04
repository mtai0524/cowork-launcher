using Cowork.Core.Configuration;

namespace Cowork.Core.Models;

/// <summary>
/// Trỏ tới một file cấu hình thuộc về app. Cowork chỉ lưu đường dẫn + định dạng;
/// nội dung luôn được đọc trực tiếp từ đĩa để không bao giờ lệch với file thật.
/// </summary>
public sealed class ConfigFileRef
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tên hiển thị trên tab editor. Nếu rỗng, dùng tên file.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Đường dẫn tuyệt đối, hoặc tương đối so với WorkingDirectory của app.</summary>
    public string Path { get; set; } = string.Empty;

    public ConfigFormat Format { get; set; } = ConfigFormat.Auto;

    /// <summary>Tạo file .bak trước mỗi lần ghi đè.</summary>
    public bool BackupOnSave { get; set; } = true;

    public string ResolveDisplayName()
        => !string.IsNullOrWhiteSpace(DisplayName)
            ? DisplayName
            : (System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : "(chưa đặt tên)");

    /// <summary>Ghép đường dẫn tương đối với thư mục làm việc của app.</summary>
    public string ResolveFullPath(string? workingDirectory)
    {
        var raw = Environment.ExpandEnvironmentVariables(Path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (System.IO.Path.IsPathRooted(raw))
            return System.IO.Path.GetFullPath(raw);

        var baseDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Environment.ExpandEnvironmentVariables(workingDirectory);

        return System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, raw));
    }

    public ConfigFileRef Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Path = Path,
        Format = Format,
        BackupOnSave = BackupOnSave,
    };
}
