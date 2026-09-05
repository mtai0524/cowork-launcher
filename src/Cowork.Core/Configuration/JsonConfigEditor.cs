using System.Text.Json;
using System.Text.Json.Nodes;
using Cowork.Core.Localization;

namespace Cowork.Core.Configuration;

/// <summary>
/// Làm phẳng JSON thành các khoá dạng <c>logging.level.default</c> và <c>servers[0].host</c>.
/// Khi ghi lại, giá trị được đặt đúng kiểu gốc (số vẫn là số, bool vẫn là bool).
/// </summary>
public sealed class JsonConfigEditor : IConfigEditor
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ConfigFormat Format => ConfigFormat.Json;

    public ConfigDocument Parse(string filePath, string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, NodeOptions, DocumentOptions);
        }
        catch (JsonException ex)
        {
            return new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Json,
                RawText = text,
                ParseError = Loc.T("Cfg.InvalidJson", ex.LineNumber + 1, ex.Message),
            };
        }

        if (root is null)
        {
            return new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Json,
                RawText = text,
                ParseError = Loc.T("Cfg.EmptyJson"),
            };
        }

        var entries = new List<ConfigEntry>();
        Flatten(root, string.Empty, string.Empty, entries);

        return new ConfigDocument
        {
            FilePath = filePath,
            Format = ConfigFormat.Json,
            RawText = text,
            Entries = entries,
        };
    }

    private static void Flatten(JsonNode node, string path, string section, List<ConfigEntry> sink)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    var childPath = string.IsNullOrEmpty(path) ? pair.Key : path + "." + pair.Key;
                    if (pair.Value is null)
                        sink.Add(Leaf(childPath, path, "null", ConfigValueKind.Null));
                    else
                        Flatten(pair.Value, childPath, path, sink);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var childPath = path + "[" + i + "]";
                    var child = array[i];
                    if (child is null)
                        sink.Add(Leaf(childPath, path, "null", ConfigValueKind.Null));
                    else
                        Flatten(child, childPath, path, sink);
                }
                break;

            case JsonValue value:
                var element = value.GetValue<JsonElement>();
                var kind = element.ValueKind switch
                {
                    JsonValueKind.Number => ConfigValueKind.Number,
                    JsonValueKind.True or JsonValueKind.False => ConfigValueKind.Boolean,
                    JsonValueKind.Null => ConfigValueKind.Null,
                    _ => ConfigValueKind.String,
                };
                var text = element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.GetRawText();
                sink.Add(Leaf(path, section, text, kind));
                break;
        }
    }

    private static ConfigEntry Leaf(string path, string section, string value, ConfigValueKind kind) => new()
    {
        Path = path,
        Section = section,
        Key = LastSegment(path),
        Value = value,
        Kind = kind,
    };

    private static string LastSegment(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 ? path.Substring(dot + 1) : path;
    }

    public string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues)
    {
        var root = JsonNode.Parse(document.RawText, NodeOptions, DocumentOptions)
                   ?? throw new InvalidOperationException("Không đọc lại được JSON gốc.");

        var kinds = new Dictionary<string, ConfigValueKind>();
        foreach (var entry in document.Entries)
            kinds[entry.Path] = entry.Kind;

        foreach (var change in changedValues)
        {
            var kind = kinds.TryGetValue(change.Key, out var k) ? k : ConfigValueKind.String;
            SetValue(root, change.Key, BuildNode(change.Value, kind));
        }

        return root.ToJsonString(WriteOptions);
    }

    private static JsonNode? BuildNode(string rawValue, ConfigValueKind kind)
    {
        switch (kind)
        {
            case ConfigValueKind.Number:
                if (decimal.TryParse(rawValue, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                    return JsonValue.Create(num);
                break;

            case ConfigValueKind.Boolean:
                if (bool.TryParse(rawValue, out var flag))
                    return JsonValue.Create(flag);
                break;

            case ConfigValueKind.Null:
                if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "null")
                    return null;
                break;
        }

        return JsonValue.Create(rawValue);
    }

    /// <summary>Đi theo path (hỗ trợ <c>a.b[2].c</c>) rồi gán node lá.</summary>
    private static void SetValue(JsonNode root, string path, JsonNode? value)
    {
        var segments = SplitPath(path);
        if (segments.Count == 0)
            return;

        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            current = Descend(current, segments[i])
                      ?? throw new InvalidOperationException("Không tìm thấy nhánh trong đường dẫn: " + path);
        }

        var last = segments[segments.Count - 1];
        if (current is JsonObject obj && !last.IsIndex)
        {
            obj[last.Name] = value;
            return;
        }

        if (current is JsonArray arr && last.IsIndex && last.Index < arr.Count)
        {
            arr[last.Index] = value;
            return;
        }

        throw new InvalidOperationException("Không gán được giá trị cho khoá: " + path);
    }

    private static JsonNode? Descend(JsonNode current, PathSegment segment)
    {
        if (current is JsonObject obj && !segment.IsIndex)
            return obj[segment.Name];
        if (current is JsonArray arr && segment.IsIndex && segment.Index < arr.Count)
            return arr[segment.Index];
        return null;
    }

    private readonly record struct PathSegment(string Name, int Index, bool IsIndex);

    private static List<PathSegment> SplitPath(string path)
    {
        var segments = new List<PathSegment>();
        var buffer = new System.Text.StringBuilder();

        void FlushName()
        {
            if (buffer.Length == 0)
                return;
            segments.Add(new PathSegment(buffer.ToString(), -1, false));
            buffer.Clear();
        }

        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '.')
            {
                FlushName();
            }
            else if (c == '[')
            {
                FlushName();
                var close = path.IndexOf(']', i);
                if (close < 0)
                    break;
                if (int.TryParse(path.Substring(i + 1, close - i - 1), out var index))
                    segments.Add(new PathSegment(string.Empty, index, true));
                i = close;
            }
            else
            {
                buffer.Append(c);
            }
        }

        FlushName();
        return segments;
    }
}
