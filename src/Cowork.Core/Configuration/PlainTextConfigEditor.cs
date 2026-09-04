namespace Cowork.Core.Configuration;

/// <summary>
/// Dự phòng cho định dạng không nhận diện được: chỉ mở ở chế độ sửa text thô.
/// </summary>
public sealed class PlainTextConfigEditor : IConfigEditor
{
    public ConfigFormat Format => ConfigFormat.PlainText;

    public ConfigDocument Parse(string filePath, string text) => new()
    {
        FilePath = filePath,
        Format = ConfigFormat.PlainText,
        RawText = text,
    };

    public string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues)
        => document.RawText;
}
