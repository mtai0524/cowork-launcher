using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

/// <summary>
/// JSON không có cú pháp comment, nên file cấu hình thật ngoài đời giải quyết theo hai kiểu:
/// dùng comment JSONC <c>//</c>, hoặc dùng khoá giả <c>"//ten"</c>. Cowork phải đọc được
/// cả hai và — quan trọng hơn — không được xoá mất chúng khi lưu.
/// </summary>
public class JsonCommentTests
{
    private static readonly JsonConfigEditor Editor = new();

    private static ConfigDocument Parse(string text) => Editor.Parse("test.json", text);

    // ---------- Comment JSONC thật ----------

    private const string Jsonc = """
        {
          // Khoa chinh, lay tren trang quan tri
          "apiKey": "abc123",

          /* Bat len thi ghi them log chi tiet.
             Tat di cho moi truong that. */
          "verbose": true,
          "retry": 3
        }
        """;

    [Fact]
    public void JsoncComments_DoNotBlockParsing()
    {
        var document = Parse(Jsonc);

        Assert.Null(document.ParseError);
        Assert.True(document.SupportsStructuredEditing);
        Assert.Equal("abc123", document.Entries.First(e => e.Path == "apiKey").Value);
    }

    [Fact]
    public void JsoncComments_SurviveSaving()
    {
        var updated = Editor.ApplyChanges(Parse(Jsonc), new Dictionary<string, string>
        {
            ["apiKey"] = "xyz789",
        });

        Assert.Contains("// Khoa chinh, lay tren trang quan tri", updated);
        Assert.Contains("/* Bat len thi ghi them log chi tiet.", updated);
        Assert.Contains("Tat di cho moi truong that. */", updated);
        Assert.Contains("\"apiKey\": \"xyz789\"", updated);
    }

    [Fact]
    public void Saving_LeavesUntouchedLinesByteForByte()
    {
        var updated = Editor.ApplyChanges(Parse(Jsonc), new Dictionary<string, string>
        {
            ["retry"] = "9",
        });

        // Chỉ đúng một ký tự được đổi; thụt lề, dòng trống và comment phải y nguyên.
        Assert.Equal(Jsonc.Replace("\"retry\": 3", "\"retry\": 9"), updated);
    }

    [Fact]
    public void Saving_KeepsIndentationAndBlankLines()
    {
        const string oddFormatting = "{\n\t\"a\"   :    1,\n\n\n\t\"b\" : \"hai\"\n}";

        var updated = Editor.ApplyChanges(Parse(oddFormatting),
            new Dictionary<string, string> { ["b"] = "ba" });

        Assert.Equal("{\n\t\"a\"   :    1,\n\n\n\t\"b\" : \"ba\"\n}", updated);
    }

    // ---------- Khoá giả "//ten" ----------

    /// <summary>Rút gọn từ config.json thật của gcm, gồm cả nháy kép lồng trong giá trị.</summary>
    private const string DocumentationKeys = """
        {
          "//": "gcm config - CHI ghi nhung key ban muon khac mac dinh.",
          "//api_key": "Groq API key free tai https://console.groq.com/keys  - mac dinh: \"\"",
          "api_key": "gsk_xxx",
          "//lang": "ngon ngu mac dinh cua commit message  (vi | en)  - mac dinh: \"en\"",
          "//tui": "true = mac dinh chon file kieu TUI  - mac dinh: false"
        }
        """;

    [Fact]
    public void DocumentationKey_BecomesTheCommentOfTheKeyItDescribes()
    {
        var document = Parse(DocumentationKeys);
        var apiKey = document.Entries.First(e => e.Path == "api_key");

        Assert.StartsWith("Groq API key free tai", apiKey.Comment);
        Assert.Contains("mac dinh: \"\"", apiKey.Comment);

        // Đã gộp thì không hiện lại thành dòng riêng nữa.
        Assert.DoesNotContain(document.Entries, e => e.Path == "//api_key");
    }

    [Fact]
    public void DocumentationKey_StaysVisibleWhenItDescribesNothing()
    {
        var paths = Parse(DocumentationKeys).Entries.Select(e => e.Path).ToList();

        // "lang" và "tui" chưa được đặt trong file — giấu chú thích của chúng đi là mất nội dung.
        Assert.Contains("//lang", paths);
        Assert.Contains("//tui", paths);

        // Khoá "//" trống là ghi chú cho cả file, không mô tả khoá nào.
        Assert.Contains("//", paths);
    }

    [Fact]
    public void DocumentationKey_IsNotFoldedIntoABranch()
    {
        var document = Parse("""
            {
              "//server": "chum may chu",
              "server": { "host": "10.0.0.1" }
            }
            """);

        Assert.Contains(document.Entries, e => e.Path == "//server");
        Assert.Null(document.Entries.First(e => e.Path == "server.host").Comment);
    }

    [Fact]
    public void EditingADocumentedKey_LeavesItsDocumentationAlone()
    {
        var updated = Editor.ApplyChanges(Parse(DocumentationKeys),
            new Dictionary<string, string> { ["api_key"] = "gsk_moi" });

        Assert.Contains("\"api_key\": \"gsk_moi\"", updated);
        Assert.Contains(@"""//api_key"": ""Groq API key free tai", updated);

        // Nháy kép lồng bên trong giá trị phải giữ nguyên cách escape.
        Assert.Contains(@"mac dinh: \""\""", updated);
    }

    [Fact]
    public void EscapedCharacters_SurviveARoundTrip()
    {
        const string tricky = """
            { "path": "C:\\Users\\nguye\\.config", "quote": "noi \"the nay\"", "tab": "a\tb" }
            """;

        var document = Parse(tricky);
        Assert.Equal(@"C:\Users\nguye\.config", document.Entries.First(e => e.Path == "path").Value);

        var updated = Editor.ApplyChanges(document,
            new Dictionary<string, string> { ["quote"] = @"duong dan D:\tam\moi" });

        var reparsed = Parse(updated);
        Assert.Null(reparsed.ParseError);
        Assert.Equal(@"C:\Users\nguye\.config", reparsed.Entries.First(e => e.Path == "path").Value);
        Assert.Equal(@"duong dan D:\tam\moi", reparsed.Entries.First(e => e.Path == "quote").Value);
        Assert.Equal("a\tb", reparsed.Entries.First(e => e.Path == "tab").Value);
    }

    [Fact]
    public void NonAsciiValues_AreNotEscapedIntoUnreadableCode()
    {
        var updated = Editor.ApplyChanges(Parse("""{ "ten": "cu" }"""),
            new Dictionary<string, string> { ["ten"] = "Báo cáo sáng" });

        Assert.Contains("\"ten\": \"Báo cáo sáng\"", updated);
    }

    // ---------- Vá đúng chỗ ----------

    [Fact]
    public void RepeatedKeyNamesAtDifferentDepths_PatchIndependently()
    {
        const string nested = """
            {
              "name": "goc",
              "child": { "name": "con" },
              "list": [ { "name": "mot" }, { "name": "hai" } ]
            }
            """;

        var updated = Editor.ApplyChanges(Parse(nested), new Dictionary<string, string>
        {
            ["child.name"] = "con-moi",
            ["list[1].name"] = "hai-moi",
        });

        var reparsed = Parse(updated);
        Assert.Equal("goc", reparsed.Entries.First(e => e.Path == "name").Value);
        Assert.Equal("con-moi", reparsed.Entries.First(e => e.Path == "child.name").Value);
        Assert.Equal("mot", reparsed.Entries.First(e => e.Path == "list[0].name").Value);
        Assert.Equal("hai-moi", reparsed.Entries.First(e => e.Path == "list[1].name").Value);
    }

    [Fact]
    public void ChangingSeveralValuesAtOnce_DoesNotShiftLaterOffsets()
    {
        const string many = """{ "a": "1", "b": "2", "c": "3", "d": "4" }""";

        var updated = Editor.ApplyChanges(Parse(many), new Dictionary<string, string>
        {
            ["a"] = "mot gia tri rat dai de doi do lech vi tri",
            ["b"] = "",
            ["c"] = "ba",
            ["d"] = "bon",
        });

        var reparsed = Parse(updated);
        Assert.Equal("mot gia tri rat dai de doi do lech vi tri", reparsed.Entries.First(e => e.Path == "a").Value);
        Assert.Equal("", reparsed.Entries.First(e => e.Path == "b").Value);
        Assert.Equal("ba", reparsed.Entries.First(e => e.Path == "c").Value);
        Assert.Equal("bon", reparsed.Entries.First(e => e.Path == "d").Value);
    }

    [Fact]
    public void NoChanges_ReturnsTheOriginalTextUntouched()
        => Assert.Equal(Jsonc, Editor.ApplyChanges(Parse(Jsonc), new Dictionary<string, string>()));

    [Fact]
    public void UnknownKey_ThrowsAHandledExceptionType()
    {
        var document = Parse(Jsonc);

        // ConfigFileViewModel chỉ bắt InvalidOperationException; kiểu khác sẽ làm sập app.
        Assert.Throws<InvalidOperationException>(() => Editor.ApplyChanges(document,
            new Dictionary<string, string> { ["khong.ton.tai"] = "x" }));
    }

    [Fact]
    public void TrailingCommas_AreAcceptedAndPreserved()
    {
        const string trailing = "{\n  \"a\": 1,\n  \"b\": 2,\n}";

        var document = Parse(trailing);
        Assert.Null(document.ParseError);

        var updated = Editor.ApplyChanges(document, new Dictionary<string, string> { ["a"] = "7" });
        Assert.Equal("{\n  \"a\": 7,\n  \"b\": 2,\n}", updated);
    }
}
