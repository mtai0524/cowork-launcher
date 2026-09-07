namespace Cowork.Core.Configuration;

public enum ConfigValueKind
{
    String = 0,
    Number = 1,
    Boolean = 2,
    Null = 3,
}

/// <summary>
/// Một khoá cấu hình đã được "làm phẳng" để hiển thị dạng bảng.
/// <see cref="Handle"/> là con trỏ nội bộ để editor ghi ngược lại đúng vị trí trong file gốc.
/// </summary>
public sealed class ConfigEntry
{
    public required string Path { get; init; }

    /// <summary>Nhóm hiển thị: section của INI, hoặc nhánh cha của JSON/XML.</summary>
    public string Section { get; init; } = string.Empty;

    /// <summary>Tên khoá ngắn gọn (đoạn cuối của Path).</summary>
    public required string Key { get; init; }

    public string Value { get; set; } = string.Empty;

    public ConfigValueKind Kind { get; init; } = ConfigValueKind.String;

    /// <summary>Chú thích đứng ngay trên khoá (INI/XML), hiển thị làm gợi ý.</summary>
    public string? Comment { get; init; }

    /// <summary>Dữ liệu nội bộ của editor. Không dùng ở tầng UI.</summary>
    internal object? Handle { get; init; }
}

/// <summary>
/// Kết quả đọc một file cấu hình: vừa có bảng khoá-giá trị, vừa giữ nguyên text gốc
/// để người dùng có thể chuyển sang chế độ sửa thô.
/// </summary>
public sealed class ConfigDocument
{
    public required string FilePath { get; init; }
    public required ConfigFormat Format { get; init; }
    public required string RawText { get; init; }
    public IReadOnlyList<ConfigEntry> Entries { get; init; } = Array.Empty<ConfigEntry>();

    /// <summary>Lý do không phân tích được thành bảng (file lỗi cú pháp...). Null nếu ổn.</summary>
    public string? ParseError { get; init; }

    /// <summary>
    /// Dấu của file tại lúc đọc, để sau này biết có ai sửa file bằng công cụ khác không.
    /// Null khi file không tồn tại, hoặc khi tài liệu được dựng từ text chứ không phải từ đĩa
    /// (bộ đọc-ghi chỉ nhận text, nên dấu do <see cref="ConfigFileService"/> gắn vào sau).
    /// </summary>
    public FileStamp? Stamp { get; internal set; }

    public bool SupportsStructuredEditing => ParseError is null && Format != ConfigFormat.PlainText;
}
