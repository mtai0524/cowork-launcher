using System.Xml;
using System.Xml.Linq;

namespace Cowork.Core.Configuration;

/// <summary>
/// Làm phẳng XML (bao gồm App.config / Web.config) thành các khoá dạng
/// <c>configuration/appSettings/add[2]/@value</c>. Comment trong file được giữ nguyên
/// vì ta sửa trực tiếp trên cây <see cref="XDocument"/> đã nạp.
/// </summary>
public sealed class XmlConfigEditor : IConfigEditor
{
    public ConfigFormat Format => ConfigFormat.Xml;

    public ConfigDocument Parse(string filePath, string text)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            return new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Xml,
                RawText = text,
                ParseError = $"XML không hợp lệ (dòng {ex.LineNumber}): {ex.Message}",
            };
        }

        if (document.Root is null)
        {
            return new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Xml,
                RawText = text,
                ParseError = "File XML không có phần tử gốc.",
            };
        }

        var entries = new List<ConfigEntry>();
        Flatten(document.Root, document.Root.Name.LocalName, string.Empty, entries);

        return new ConfigDocument
        {
            FilePath = filePath,
            Format = ConfigFormat.Xml,
            RawText = text,
            Entries = entries,
        };
    }

    private static void Flatten(XElement element, string path, string section, List<ConfigEntry> sink)
    {
        var comment = PrecedingComment(element);

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            sink.Add(new ConfigEntry
            {
                Path = path + "/@" + attribute.Name.LocalName,
                Section = path,
                Key = attribute.Name.LocalName,
                Value = attribute.Value,
                Kind = InferKind(attribute.Value),
                Comment = comment,
                Handle = new AttributeHandle(path, attribute.Name.LocalName),
            });
        }

        var children = element.Elements().ToList();
        if (children.Count == 0)
        {
            // Phần tử lá: giá trị chính là nội dung text.
            if (element.Attributes().Any(a => !a.IsNamespaceDeclaration) && element.Value.Trim().Length == 0)
                return;

            sink.Add(new ConfigEntry
            {
                Path = path,
                Section = section,
                Key = element.Name.LocalName,
                Value = element.Value,
                Kind = InferKind(element.Value),
                Comment = comment,
                Handle = new ElementHandle(path),
            });
            return;
        }

        // Anh em trùng tên được đánh số 1-based để đường dẫn luôn duy nhất.
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var totals = children
            .GroupBy(c => c.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        foreach (var child in children)
        {
            var name = child.Name.LocalName;
            counters.TryGetValue(name, out var seen);
            counters[name] = seen + 1;

            var childPath = totals[name] > 1
                ? path + "/" + name + "[" + (seen + 1) + "]"
                : path + "/" + name;

            Flatten(child, childPath, path, sink);
        }
    }

    private static string? PrecedingComment(XElement element)
    {
        for (var node = element.PreviousNode; node is not null; node = node.PreviousNode)
        {
            if (node is XComment comment)
                return comment.Value.Trim();
            if (node is XText text && text.Value.Trim().Length == 0)
                continue;
            break;
        }

        return null;
    }

    public string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues)
    {
        var xml = XDocument.Parse(document.RawText, LoadOptions.PreserveWhitespace);
        if (xml.Root is null)
            throw new InvalidOperationException("File XML không có phần tử gốc.");

        foreach (var entry in document.Entries)
        {
            if (!changedValues.TryGetValue(entry.Path, out var newValue))
                continue;

            switch (entry.Handle)
            {
                case AttributeHandle attributeHandle:
                    var owner = Resolve(xml.Root, attributeHandle.ElementPath)
                                ?? throw new InvalidOperationException("Không tìm thấy phần tử: " + attributeHandle.ElementPath);
                    owner.SetAttributeValue(attributeHandle.AttributeName, newValue);
                    break;

                case ElementHandle elementHandle:
                    var target = Resolve(xml.Root, elementHandle.ElementPath)
                                 ?? throw new InvalidOperationException("Không tìm thấy phần tử: " + elementHandle.ElementPath);
                    target.Value = newValue;
                    break;
            }
        }

        var declaration = xml.Declaration is null ? string.Empty : xml.Declaration + Environment.NewLine;
        return declaration + xml.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Đi theo đường dẫn dạng <c>root/child/grand[2]</c> từ phần tử gốc.</summary>
    private static XElement? Resolve(XElement root, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        // Đoạn đầu tiên chính là phần tử gốc.
        var (rootName, _) = ParseSegment(segments[0]);
        if (!string.Equals(root.Name.LocalName, rootName, StringComparison.Ordinal))
            return null;

        var current = root;
        for (var i = 1; i < segments.Length; i++)
        {
            var (name, index) = ParseSegment(segments[i]);
            var matches = current.Elements().Where(e => e.Name.LocalName == name).ToList();
            if (index < 1 || index > matches.Count)
                return null;
            current = matches[index - 1];
        }

        return current;
    }

    private static (string Name, int Index) ParseSegment(string segment)
    {
        var open = segment.IndexOf('[');
        if (open < 0)
            return (segment, 1);

        var close = segment.IndexOf(']', open);
        if (close < 0)
            return (segment.Substring(0, open), 1);

        var name = segment.Substring(0, open);
        return int.TryParse(segment.Substring(open + 1, close - open - 1), out var index)
            ? (name, index)
            : (name, 1);
    }

    private static ConfigValueKind InferKind(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return ConfigValueKind.String;
        if (bool.TryParse(trimmed, out _))
            return ConfigValueKind.Boolean;
        if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return ConfigValueKind.Number;
        return ConfigValueKind.String;
    }

    private sealed record AttributeHandle(string ElementPath, string AttributeName);

    private sealed record ElementHandle(string ElementPath);
}
