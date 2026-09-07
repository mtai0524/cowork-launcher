using System.Text;
using Cowork.Core.Localization;

namespace Cowork.Core.Configuration;

public interface IConfigFileService
{
    /// <summary>Đọc file cấu hình và phân tích thành bảng khoá-giá trị.</summary>
    ConfigDocument Load(string fullPath, ConfigFormat format);

    /// <summary>Ghi các giá trị đã sửa (chế độ bảng). Trả về text mới đã ghi xuống đĩa.</summary>
    string SaveChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues, bool backup);

    /// <summary>Ghi thẳng text thô (chế độ sửa nguồn).</summary>
    void SaveRaw(string fullPath, string text, bool backup);

    /// <summary>Kiểm tra text thô có đúng cú pháp định dạng không. Null = hợp lệ.</summary>
    string? Validate(string text, ConfigFormat format, string fullPath);

    /// <summary>File trên đĩa đã bị sửa bởi thứ khác kể từ lúc <paramref name="document"/> được đọc chưa.</summary>
    bool HasChangedOnDisk(ConfigDocument document);
}

public sealed class ConfigFileService : IConfigFileService
{
    private readonly IReadOnlyDictionary<ConfigFormat, IConfigEditor> _editors;

    public ConfigFileService()
        : this(new IConfigEditor[]
        {
            new JsonConfigEditor(),
            new IniConfigEditor(),
            new XmlConfigEditor(),
            new PlainTextConfigEditor(),
        })
    {
    }

    public ConfigFileService(IEnumerable<IConfigEditor> editors)
        => _editors = editors.ToDictionary(e => e.Format);

    public ConfigDocument Load(string fullPath, ConfigFormat format)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("Đường dẫn file cấu hình đang trống.", nameof(fullPath));

        if (!File.Exists(fullPath))
        {
            return new ConfigDocument
            {
                FilePath = fullPath,
                Format = format.Resolve(fullPath),
                RawText = string.Empty,
                ParseError = Loc.T("Cfg.FileNotFound", fullPath),
            };
        }

        // Chụp dấu *trước* khi đọc: ai đó ghi đè trong lúc ta đang đọc thì dấu cũ khác dấu mới,
        // nên lần kiểm tra sau vẫn báo đã đổi. Chụp sau thì đúng trường hợp đó bị bỏ lọt.
        var stamp = FileStamp.Read(fullPath);
        var text = File.ReadAllText(fullPath, DetectEncoding(fullPath));
        var document = Resolve(format, fullPath).Parse(fullPath, text);
        document.Stamp = stamp;

        return document;
    }

    public bool HasChangedOnDisk(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return FileStamp.HasChanged(document.Stamp, FileStamp.Read(document.FilePath));
    }

    public string SaveChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues, bool backup)
    {
        var editor = Resolve(document.Format, document.FilePath);
        var updated = editor.ApplyChanges(document, changedValues);
        WriteFile(document.FilePath, updated, backup);
        return updated;
    }

    public void SaveRaw(string fullPath, string text, bool backup) => WriteFile(fullPath, text, backup);

    public string? Validate(string text, ConfigFormat format, string fullPath)
    {
        var document = Resolve(format, fullPath).Parse(fullPath, text);
        return document.ParseError;
    }

    private IConfigEditor Resolve(ConfigFormat format, string path)
    {
        var resolved = format.Resolve(path);
        return _editors.TryGetValue(resolved, out var editor)
            ? editor
            : _editors[ConfigFormat.PlainText];
    }

    /// <summary>
    /// Ghi qua file tạm rồi thay thế nguyên tử, để mất điện giữa chừng không làm hỏng config.
    /// </summary>
    private static void WriteFile(string fullPath, string text, bool backup)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (backup && File.Exists(fullPath))
        {
            var backupPath = fullPath + ".cowork.bak";
            File.Copy(fullPath, backupPath, overwrite: true);
        }

        var encoding = File.Exists(fullPath) ? DetectEncoding(fullPath) : new UTF8Encoding(false);
        var temp = fullPath + ".cowork.tmp";
        File.WriteAllText(temp, text, encoding);

        if (File.Exists(fullPath))
            File.Replace(temp, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(temp, fullPath);
    }

    /// <summary>Nhận diện BOM để ghi lại đúng encoding cũ, tránh làm hỏng file tiếng Việt.</summary>
    private static Encoding DetectEncoding(string path)
    {
        try
        {
            var bom = new byte[4];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var read = stream.Read(bom, 0, 4);

            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                return Encoding.Unicode;
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;
        }
        catch (IOException)
        {
            // File đang bị khoá — dùng mặc định.
        }

        return new UTF8Encoding(false);
    }
}
