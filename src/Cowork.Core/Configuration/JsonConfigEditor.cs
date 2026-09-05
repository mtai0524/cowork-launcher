using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cowork.Core.Localization;

namespace Cowork.Core.Configuration;

/// <summary>
/// Làm phẳng JSON thành các khoá dạng <c>logging.level.default</c> và <c>servers[0].host</c>.
/// Khi ghi lại, giá trị được đặt đúng kiểu gốc (số vẫn là số, bool vẫn là bool).
///
/// Chấp nhận cả JSONC: comment <c>//</c> và <c>/* */</c> được bỏ qua lúc đọc và
/// <em>giữ nguyên</em> lúc ghi, vì <see cref="ApplyChanges"/> vá thẳng lên text gốc.
/// </summary>
public sealed class JsonConfigEditor : IConfigEditor
{
    /// <summary>
    /// JSON không có cú pháp comment, nên nhiều công cụ dùng khoá <c>"//ten"</c> làm chú
    /// thích cho khoá <c>"ten"</c> nằm ngay cạnh. Đây là quy ước, không phải chuẩn.
    /// </summary>
    private const string DocumentationPrefix = "//";

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions ValueOptions = new()
    {
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
        Flatten(root, string.Empty, string.Empty, entries, comment: null);

        return new ConfigDocument
        {
            FilePath = filePath,
            Format = ConfigFormat.Json,
            RawText = text,
            Entries = entries,
        };
    }

    private static void Flatten(JsonNode node, string path, string section, List<ConfigEntry> sink, string? comment)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (IsFoldedDocumentation(obj, pair.Key))
                        continue;

                    var childPath = string.IsNullOrEmpty(path) ? pair.Key : path + "." + pair.Key;

                    if (pair.Value is null)
                        sink.Add(Leaf(childPath, path, "null", ConfigValueKind.Null, DocumentationFor(obj, pair.Key)));
                    else if (pair.Value is JsonValue)
                        Flatten(pair.Value, childPath, path, sink, DocumentationFor(obj, pair.Key));
                    else
                        Flatten(pair.Value, childPath, path, sink, comment: null);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var childPath = path + "[" + i + "]";
                    var child = array[i];
                    if (child is null)
                        sink.Add(Leaf(childPath, path, "null", ConfigValueKind.Null, comment: null));
                    else
                        Flatten(child, childPath, path, sink, comment: null);
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
                sink.Add(Leaf(path, section, text, kind, comment));
                break;
        }
    }

    /// <summary>
    /// Khoá <c>"//ten"</c> có được gộp vào cột ghi chú của <c>"ten"</c> hay không.
    ///
    /// Chỉ gộp khi khoá được chú thích có thật và là giá trị đơn. Chú thích cho một
    /// nhánh con thì không biết bám vào dòng nào, còn chú thích cho khoá không tồn tại
    /// (kiểu <c>"//lang"</c> mô tả một mặc định chưa bật) mà giấu đi là mất nội dung —
    /// hai trường hợp đó giữ nguyên thành dòng riêng để còn đọc và sửa được.
    /// </summary>
    private static bool IsFoldedDocumentation(JsonObject obj, string key)
    {
        if (!key.StartsWith(DocumentationPrefix, StringComparison.Ordinal))
            return false;

        var documented = key[DocumentationPrefix.Length..];
        if (documented.Length == 0)
            return false;

        return obj.TryGetPropertyValue(documented, out var target)
               && target is null or JsonValue
               && IsStringValue(obj[key]);
    }

    private static string? DocumentationFor(JsonObject obj, string key)
        => obj.TryGetPropertyValue(DocumentationPrefix + key, out var node) && IsStringValue(node)
            ? node!.GetValue<JsonElement>().GetString()
            : null;

    private static bool IsStringValue(JsonNode? node)
        => node is JsonValue value && value.GetValue<JsonElement>().ValueKind == JsonValueKind.String;

    private static ConfigEntry Leaf(string path, string section, string value, ConfigValueKind kind, string? comment)
        => new()
        {
            Path = path,
            Section = section,
            Key = LastSegment(path),
            Value = value,
            Kind = kind,
            Comment = comment,
        };

    private static string LastSegment(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 ? path.Substring(dot + 1) : path;
    }

    /// <summary>
    /// Vá giá trị đã sửa lên đúng vị trí của nó trong text gốc.
    ///
    /// Cố ý không dựng lại file từ cây JSON: dựng lại sẽ xoá sạch comment, thứ tự thụt lề
    /// và dòng trống của người dùng — với file .jsonc thì đó là mất dữ liệu thật sự.
    /// Vị trí tính theo byte UTF-8 vì <see cref="Utf8JsonReader"/> làm việc trên byte.
    /// </summary>
    public string ApplyChanges(ConfigDocument document, IReadOnlyDictionary<string, string> changedValues)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changedValues);

        if (changedValues.Count == 0)
            return document.RawText;

        var kinds = new Dictionary<string, ConfigValueKind>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
            kinds[entry.Path] = entry.Kind;

        var bytes = Encoding.UTF8.GetBytes(document.RawText);
        var spans = LocateValues(bytes);

        // Vá từ cuối file ngược lên đầu để các vị trí phía trước không bị lệch.
        var edits = new List<(int Start, int End, byte[] Value)>();
        foreach (var change in changedValues)
        {
            if (!spans.TryGetValue(change.Key, out var span))
                throw new InvalidOperationException("Không gán được giá trị cho khoá: " + change.Key);

            var kind = kinds.TryGetValue(change.Key, out var k) ? k : ConfigValueKind.String;
            edits.Add((span.Start, span.End, Render(change.Value, kind)));
        }

        edits.Sort((a, b) => b.Start.CompareTo(a.Start));

        var buffer = new List<byte>(bytes);
        foreach (var (start, end, value) in edits)
        {
            buffer.RemoveRange(start, end - start);
            buffer.InsertRange(start, value);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Đường dẫn khoá -> khoảng byte của giá trị, khớp đúng path mà Flatten sinh ra.</summary>
    private static Dictionary<string, (int Start, int End)> LocateValues(ReadOnlySpan<byte> json)
    {
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        var frames = new Stack<Frame>();
        string? pendingName = null;

        var reader = new Utf8JsonReader(json, ReaderOptions);

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pendingName = reader.GetString();
                        break;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        frames.Push(new Frame(
                            reader.TokenType == JsonTokenType.StartArray,
                            NextPath(frames, ref pendingName)));
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (frames.Count > 0)
                            frames.Pop();
                        break;

                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                    case JsonTokenType.Null:
                        spans[NextPath(frames, ref pendingName)] =
                            ((int)reader.TokenStartIndex, (int)reader.BytesConsumed);
                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            // Tầng trên chỉ bắt InvalidOperationException; để JsonException lọt lên là sập app.
            throw new InvalidOperationException("Không đọc lại được JSON gốc: " + ex.Message, ex);
        }

        return spans;
    }

    private static string NextPath(Stack<Frame> frames, ref string? pendingName)
    {
        if (frames.Count == 0)
            return string.Empty;

        var frame = frames.Peek();
        if (frame.IsArray)
            return frame.Path + "[" + frame.NextIndex() + "]";

        var name = pendingName ?? string.Empty;
        pendingName = null;
        return frame.Path.Length == 0 ? name : frame.Path + "." + name;
    }

    /// <summary>Một tầng object/array đang mở, giữ đường dẫn và chỉ số phần tử kế tiếp.</summary>
    private sealed class Frame
    {
        private int _index;

        public Frame(bool isArray, string path)
        {
            IsArray = isArray;
            Path = path;
        }

        public bool IsArray { get; }

        public string Path { get; }

        public int NextIndex() => _index++;
    }

    /// <summary>Dựng đoạn JSON thay thế cho một giá trị, giữ đúng kiểu gốc của khoá.</summary>
    private static byte[] Render(string rawValue, ConfigValueKind kind)
    {
        switch (kind)
        {
            case ConfigValueKind.Number:
                if (decimal.TryParse(rawValue, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(number, ValueOptions));
                }
                break;

            case ConfigValueKind.Boolean:
                if (bool.TryParse(rawValue, out var flag))
                    return Encoding.UTF8.GetBytes(flag ? "true" : "false");
                break;

            case ConfigValueKind.Null:
                if (string.IsNullOrWhiteSpace(rawValue) || rawValue == "null")
                    return "null"u8.ToArray();
                break;
        }

        // Nhập sai kiểu thì hạ xuống chuỗi, để người dùng thấy giá trị mình gõ thay vì mất trắng.
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rawValue, ValueOptions));
    }
}
