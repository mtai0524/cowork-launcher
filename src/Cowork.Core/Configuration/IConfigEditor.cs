namespace Cowork.Core.Configuration;

/// <summary>
/// Chuyển đổi hai chiều giữa text của file cấu hình và bảng khoá-giá trị.
/// Mọi cài đặt phải giữ nguyên thứ tự, comment và định dạng của phần không bị sửa.
/// </summary>
public interface IConfigEditor
{
    ConfigFormat Format { get; }

    /// <summary>Phân tích text thành bảng. Không được ném lỗi cú pháp — trả về ParseError.</summary>
    ConfigDocument Parse(string filePath, string text);

    /// <summary>
    /// Ghi các giá trị đã sửa ngược vào text gốc và trả về text mới.
    /// </summary>
    string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues);
}
