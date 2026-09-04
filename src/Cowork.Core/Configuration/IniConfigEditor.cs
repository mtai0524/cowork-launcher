using System.Text;

namespace Cowork.Core.Configuration;

/// <summary>
/// Đọc/ghi file dạng INI, .properties và .env. Sửa tại chỗ theo từng dòng nên
/// comment, dòng trắng và thứ tự trong file gốc được giữ nguyên tuyệt đối.
/// </summary>
public sealed class IniConfigEditor : IConfigEditor
{
    public ConfigFormat Format => ConfigFormat.Ini;

    /// <summary>Vị trí của một khoá trong file gốc, đủ để ghi đè đúng đoạn giá trị.</summary>
    private sealed record LineHandle(int LineIndex, int ValueStart, int ValueLength);

    public ConfigDocument Parse(string filePath, string text)
    {
        var lines = SplitLines(text);
        var entries = new List<ConfigEntry>();
        var section = string.Empty;
        string? pendingComment = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                pendingComment = null;
                continue;
            }

            if (trimmed[0] == ';' || trimmed[0] == '#')
            {
                pendingComment = trimmed.TrimStart(';', '#', ' ');
                continue;
            }

            if (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                pendingComment = null;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                pendingComment = null;
                continue;
            }

            var key = line.Substring(0, separator).Trim();
            if (key.Length == 0)
            {
                pendingComment = null;
                continue;
            }

            // Giá trị bắt đầu sau dấu '=' và sau các khoảng trắng thụt đầu.
            var valueStart = separator + 1;
            while (valueStart < line.Length && (line[valueStart] == ' ' || line[valueStart] == '\t'))
                valueStart++;

            var valueEnd = line.Length;
            while (valueEnd > valueStart && (line[valueEnd - 1] == ' ' || line[valueEnd - 1] == '\t'))
                valueEnd--;

            var rawValue = line.Substring(valueStart, valueEnd - valueStart);

            entries.Add(new ConfigEntry
            {
                Path = section.Length == 0 ? key : section + ":" + key,
                Section = section,
                Key = key,
                Value = Unquote(rawValue),
                Kind = InferKind(rawValue),
                Comment = pendingComment,
                Handle = new LineHandle(i, valueStart, valueEnd - valueStart),
            });

            pendingComment = null;
        }

        return new ConfigDocument
        {
            FilePath = filePath,
            Format = ConfigFormat.Ini,
            RawText = text,
            Entries = entries,
        };
    }

    public string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues)
    {
        var lines = SplitLines(document.RawText);

        // Sửa từ cuối file lên đầu để chỉ số cột của các dòng phía trên không bị lệch.
        var targets = new List<(LineHandle Handle, ConfigEntry Entry, string NewValue)>();
        foreach (var entry in document.Entries)
        {
            if (!changedValues.TryGetValue(entry.Path, out var newValue))
                continue;
            if (entry.Handle is LineHandle handle)
                targets.Add((handle, entry, newValue));
        }

        foreach (var target in targets.OrderByDescending(t => t.Handle.LineIndex))
        {
            var line = lines[target.Handle.LineIndex];
            var original = line.Substring(target.Handle.ValueStart, target.Handle.ValueLength);
            var replacement = RequoteLike(original, target.NewValue);

            lines[target.Handle.LineIndex] =
                line.Substring(0, target.Handle.ValueStart)
                + replacement
                + line.Substring(target.Handle.ValueStart + target.Handle.ValueLength);
        }

        return string.Join(DetectNewLine(document.RawText), lines);
    }

    /// <summary>Giữ nguyên kiểu xuống dòng của file gốc (CRLF hay LF).</summary>
    private static string DetectNewLine(string text)
        => text.Contains("\r\n") ? "\r\n" : (text.Contains('\n') ? "\n" : Environment.NewLine);

    /// <summary>Nếu giá trị gốc có nháy bao quanh thì giá trị mới cũng được bao nháy tương tự.</summary>
    private static string RequoteLike(string original, string newValue)
    {
        if (original.Length >= 2 && original[0] == '"' && original[original.Length - 1] == '"')
            return "\"" + newValue.Replace("\"", "\\\"") + "\"";

        if (original.Length >= 2 && original[0] == '\'' && original[original.Length - 1] == '\'')
            return "'" + newValue + "'";

        return newValue;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            return value.Substring(1, value.Length - 2).Replace("\\\"", "\"");

        if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            return value.Substring(1, value.Length - 2);

        return value;
    }

    private static ConfigValueKind InferKind(string value)
    {
        var trimmed = Unquote(value.Trim());
        if (trimmed.Length == 0)
            return ConfigValueKind.String;
        if (bool.TryParse(trimmed, out _))
            return ConfigValueKind.Boolean;
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return ConfigValueKind.Number;
        return ConfigValueKind.String;
    }

    /// <summary>Tách dòng giữ được cả CRLF lẫn LF.</summary>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var builder = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                lines.Add(builder.ToString());
                builder.Clear();
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                lines.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        lines.Add(builder.ToString());
        return lines;
    }
}
