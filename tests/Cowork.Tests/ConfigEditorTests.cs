using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

public class JsonConfigEditorTests
{
    private const string Sample = """
        {
          "app": {
            "name": "Bao cao sang",
            "retryCount": 3,
            "enabled": true,
            "timeout": null
          },
          "servers": [
            { "host": "10.0.0.1", "port": 5432 },
            { "host": "10.0.0.2", "port": 5433 }
          ]
        }
        """;

    private static ConfigDocument Parse(string text = Sample)
        => new JsonConfigEditor().Parse("test.json", text);

    [Fact]
    public void Flattens_NestedObjectsAndArrays()
    {
        var document = Parse();
        var paths = document.Entries.Select(e => e.Path).ToList();

        Assert.Contains("app.name", paths);
        Assert.Contains("app.retryCount", paths);
        Assert.Contains("servers[0].host", paths);
        Assert.Contains("servers[1].port", paths);
    }

    [Fact]
    public void DetectsValueKinds()
    {
        var document = Parse();

        Assert.Equal(ConfigValueKind.String, document.Entries.First(e => e.Path == "app.name").Kind);
        Assert.Equal(ConfigValueKind.Number, document.Entries.First(e => e.Path == "app.retryCount").Kind);
        Assert.Equal(ConfigValueKind.Boolean, document.Entries.First(e => e.Path == "app.enabled").Kind);
        Assert.Equal(ConfigValueKind.Null, document.Entries.First(e => e.Path == "app.timeout").Kind);
    }

    [Fact]
    public void ApplyChanges_PreservesTypes()
    {
        var editor = new JsonConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document, new Dictionary<string, string>
        {
            ["app.name"] = "Bao cao chieu",
            ["app.retryCount"] = "5",
            ["app.enabled"] = "false",
        });

        // Số và bool phải ghi ra không có dấu nháy.
        Assert.Contains("\"retryCount\": 5", updated);
        Assert.Contains("\"enabled\": false", updated);
        Assert.Contains("\"name\": \"Bao cao chieu\"", updated);
    }

    [Fact]
    public void ApplyChanges_UpdatesValueInsideArray()
    {
        var editor = new JsonConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document,
            new Dictionary<string, string> { ["servers[1].host"] = "10.0.0.99" });

        var reparsed = editor.Parse("test.json", updated);
        Assert.Equal("10.0.0.99", reparsed.Entries.First(e => e.Path == "servers[1].host").Value);
        Assert.Equal("10.0.0.1", reparsed.Entries.First(e => e.Path == "servers[0].host").Value);
    }

    [Fact]
    public void InvalidJson_ReportsParseErrorInsteadOfThrowing()
    {
        var document = Parse("{ \"a\": ");

        Assert.NotNull(document.ParseError);
        Assert.False(document.SupportsStructuredEditing);
    }
}

public class IniConfigEditorTests
{
    private const string Sample = "; Cau hinh ket noi\r\n"
                                  + "[database]\r\n"
                                  + "host = 127.0.0.1\r\n"
                                  + "port=5432\r\n"
                                  + "password = \"bi mat\"\r\n"
                                  + "\r\n"
                                  + "[logging]\r\n"
                                  + "# muc do ghi log\r\n"
                                  + "level = info\r\n";

    private static ConfigDocument Parse(string text = Sample)
        => new IniConfigEditor().Parse("test.ini", text);

    [Fact]
    public void ParsesSectionsAndKeys()
    {
        var document = Parse();

        Assert.Equal(4, document.Entries.Count);
        Assert.Equal("127.0.0.1", document.Entries.First(e => e.Path == "database:host").Value);
        Assert.Equal("info", document.Entries.First(e => e.Path == "logging:level").Value);
    }

    [Fact]
    public void StripsQuotesFromValue()
        => Assert.Equal("bi mat", Parse().Entries.First(e => e.Path == "database:password").Value);

    [Fact]
    public void CapturesCommentAboveKey()
        => Assert.Equal("muc do ghi log", Parse().Entries.First(e => e.Path == "logging:level").Comment);

    [Fact]
    public void ApplyChanges_KeepsCommentsSpacingAndOtherLines()
    {
        var editor = new IniConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document,
            new Dictionary<string, string> { ["database:host"] = "192.168.1.50" });

        Assert.Contains("; Cau hinh ket noi", updated);
        Assert.Contains("# muc do ghi log", updated);
        Assert.Contains("host = 192.168.1.50", updated);   // giữ nguyên khoảng trắng quanh dấu =
        Assert.Contains("port=5432", updated);             // dòng khác không bị đụng vào
    }

    [Fact]
    public void ApplyChanges_KeepsQuotingStyle()
    {
        var editor = new IniConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document,
            new Dictionary<string, string> { ["database:password"] = "mat khau moi" });

        Assert.Contains("password = \"mat khau moi\"", updated);
    }
}

public class XmlConfigEditorTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <!-- ket noi CSDL -->
          <appSettings>
            <add key="Server" value="localhost" />
            <add key="Timeout" value="30" />
          </appSettings>
          <logLevel>Information</logLevel>
        </configuration>
        """;

    private static ConfigDocument Parse(string text = Sample)
        => new XmlConfigEditor().Parse("App.config", text);

    [Fact]
    public void FlattensAttributesWithIndexedPaths()
    {
        var paths = Parse().Entries.Select(e => e.Path).ToList();

        Assert.Contains("configuration/appSettings/add[1]/@key", paths);
        Assert.Contains("configuration/appSettings/add[2]/@value", paths);
        Assert.Contains("configuration/logLevel", paths);
    }

    [Fact]
    public void ApplyChanges_UpdatesAttributeAndElement()
    {
        var editor = new XmlConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document, new Dictionary<string, string>
        {
            ["configuration/appSettings/add[2]/@value"] = "60",
            ["configuration/logLevel"] = "Debug",
        });

        var reparsed = editor.Parse("App.config", updated);
        Assert.Equal("60", reparsed.Entries.First(e => e.Path == "configuration/appSettings/add[2]/@value").Value);
        Assert.Equal("Debug", reparsed.Entries.First(e => e.Path == "configuration/logLevel").Value);
        Assert.Equal("localhost", reparsed.Entries.First(e => e.Path == "configuration/appSettings/add[1]/@value").Value);
    }

    [Fact]
    public void ApplyChanges_KeepsComments()
    {
        var editor = new XmlConfigEditor();
        var document = Parse();

        var updated = editor.ApplyChanges(document,
            new Dictionary<string, string> { ["configuration/logLevel"] = "Warning" });

        Assert.Contains("ket noi CSDL", updated);
    }

    [Fact]
    public void InvalidXml_ReportsParseError()
    {
        var document = Parse("<a><b></a>");

        Assert.NotNull(document.ParseError);
    }
}
