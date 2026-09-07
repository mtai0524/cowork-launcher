namespace Cowork.Core.Configuration;

/// <summary>
/// Dấu nhận dạng một file tại thời điểm đọc: giờ ghi cuối và kích thước. Dùng để phát hiện
/// "file đã bị sửa bằng công cụ khác kể từ lúc Cowork mở nó".
///
/// Cố ý không băm nội dung: file cấu hình có thể lớn, và cặp giờ-ghi + kích thước đã bắt được
/// mọi trường hợp thực tế. Sai sót duy nhất là ai đó sửa file mà không đổi cả hai — lúc đó
/// Cowork ghi đè, đúng như trước khi có lớp kiểm tra này, và bản <c>.cowork.bak</c> vẫn giữ nội dung cũ.
/// </summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length)
{
    /// <summary>Đọc dấu của file. Trả về null nếu file không tồn tại hoặc không đọc được.</summary>
    public static FileStamp? Read(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// File đã đổi so với dấu đã chụp chưa. File bị xoá, hoặc chưa từng chụp được dấu, đều tính là đổi —
    /// thà hỏi thừa còn hơn ghi đè mất nội dung của người khác.
    /// </summary>
    public static bool HasChanged(FileStamp? taken, FileStamp? current)
        => taken is null || current is null || !taken.Value.Equals(current.Value);
}
