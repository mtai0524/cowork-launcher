using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

/// <summary>So sánh theo dòng là hàm thuần — kiểm được mọi hình dạng thay đổi mà không cần file thật.</summary>
public class LineDiffTests
{
    private static string Text(params string[] lines) => string.Join("\n", lines);

    private static IReadOnlyList<string> Rendered(DiffResult result)
        => result.Lines.Select(l => l.Kind switch
        {
            DiffKind.Added => "+" + l.Text,
            DiffKind.Removed => "-" + l.Text,
            _ => " " + l.Text,
        }).ToList();

    [Fact]
    public void IdenticalText_HasNoChanges()
    {
        var result = LineDiff.Compare(Text("a", "b"), Text("a", "b"));

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Removed);
        Assert.All(result.Lines, l => Assert.Equal(DiffKind.Unchanged, l.Kind));
    }

    [Fact]
    public void AChangedLine_ShowsAsRemovedThenAdded()
    {
        var result = LineDiff.Compare(Text("a", "b", "c"), Text("a", "B", "c"));

        Assert.Equal(new[] { " a", "-b", "+B", " c" }, Rendered(result));
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public void AnInsertedLine_OnlyCountsAsAdded()
    {
        var result = LineDiff.Compare(Text("a", "c"), Text("a", "b", "c"));

        Assert.Equal(new[] { " a", "+b", " c" }, Rendered(result));
        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void ADeletedLine_OnlyCountsAsRemoved()
    {
        var result = LineDiff.Compare(Text("a", "b", "c"), Text("a", "c"));

        Assert.Equal(new[] { " a", "-b", " c" }, Rendered(result));
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public void LineNumbers_FollowEachSideSeparately()
    {
        var result = LineDiff.Compare(Text("a", "b", "c"), Text("a", "c"));

        var removed = Assert.Single(result.Lines, l => l.Kind == DiffKind.Removed);
        Assert.Equal(2, removed.LeftNumber);
        Assert.Null(removed.RightNumber);

        var last = result.Lines[^1];
        Assert.Equal(3, last.LeftNumber);
        Assert.Equal(2, last.RightNumber);
    }

    [Fact]
    public void EmptyText_IsNotTheSameAsOneBlankLine()
    {
        Assert.False(LineDiff.Compare(string.Empty, string.Empty).HasChanges);
        Assert.True(LineDiff.Compare(string.Empty, "\n").HasChanges);
        Assert.True(LineDiff.Compare(null, "a").HasChanges);
    }

    [Fact]
    public void LineEndings_DoNotCountAsChanges()
    {
        // Chuyển CRLF sang LF không phải thay đổi nội dung; báo cả file đổi thì bảng vô dụng.
        Assert.False(LineDiff.Compare("a\r\nb\r\nc", "a\nb\nc").HasChanges);
    }

    [Fact]
    public void OnlyChanges_KeepsSurroundingContext()
    {
        var left = Text("1", "2", "3", "4", "5", "6", "7", "8", "9");
        var right = Text("1", "2", "3", "4", "X", "6", "7", "8", "9");

        var trimmed = LineDiff.OnlyChanges(LineDiff.Compare(left, right), context: 1);

        Assert.Equal(new[] { " 4", "-5", "+X", " 6" }, Rendered(trimmed));

        // Số dòng thêm/bỏ vẫn là của cả file, không phải của phần đang hiện.
        Assert.Equal(1, trimmed.Added);
        Assert.Equal(1, trimmed.Removed);
    }

    [Fact]
    public void OnlyChanges_OnIdenticalText_ShowsNothing()
        => Assert.Empty(LineDiff.OnlyChanges(LineDiff.Compare("a\nb", "a\nb")).Lines);

    [Fact]
    public void ASmallEditInABigFile_StaysASmallDiff()
    {
        // Phần đầu và đuôi giống nhau bị cắt trước, nên bảng LCS chỉ chạy trên khúc giữa.
        var lines = Enumerable.Range(1, 5000).Select(i => $"key{i}=value{i}").ToArray();
        var left = string.Join("\n", lines);
        lines[2500] = "key2501=doi-roi";
        var right = string.Join("\n", lines);

        var result = LineDiff.Compare(left, right);

        Assert.False(result.Truncated);
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public void TwoCompletelyDifferentBigFiles_FallBackToWholeBlockReplacement()
    {
        var left = string.Join("\n", Enumerable.Range(1, LineDiff.MaxBlockLines + 10).Select(i => $"left{i}"));
        var right = string.Join("\n", Enumerable.Range(1, LineDiff.MaxBlockLines + 10).Select(i => $"right{i}"));

        var result = LineDiff.Compare(left, right);

        Assert.True(result.Truncated);
        Assert.Equal(LineDiff.MaxBlockLines + 10, result.Added);
        Assert.Equal(LineDiff.MaxBlockLines + 10, result.Removed);
    }

    [Fact]
    public void EverythingRemoved_OrEverythingAdded()
    {
        Assert.Equal(new[] { "-a", "-b" }, Rendered(LineDiff.Compare(Text("a", "b"), string.Empty)));
        Assert.Equal(new[] { "+a", "+b" }, Rendered(LineDiff.Compare(string.Empty, Text("a", "b"))));
    }
}

public class ConfigBackupTests
{
    private static string WriteFile(TempDirectory temp, string name, string content)
    {
        var path = Path.Combine(temp.Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadBackup_ReturnsNull_UntilTheFirstSave()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();
        var file = WriteFile(temp, "app.ini", "port=8080");

        Assert.Null(service.ReadBackup(file));

        var document = service.Load(file, ConfigFormat.Auto);
        service.SaveChanges(document, new Dictionary<string, string> { ["port"] = "9090" }, backup: true);

        Assert.Equal("port=8080", service.ReadBackup(file));
    }

    [Fact]
    public void BackupPath_SitsNextToTheFile()
    {
        var service = new ConfigFileService();

        Assert.Equal(@"C:\app\config.ini.cowork.bak", service.BackupPath(@"C:\app\config.ini"));
    }

    [Fact]
    public void RestoreBackup_PutsTheOldContentsBack()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();
        var file = WriteFile(temp, "app.ini", "port=8080");

        var document = service.Load(file, ConfigFormat.Auto);
        service.SaveChanges(document, new Dictionary<string, string> { ["port"] = "9090" }, backup: true);
        Assert.Equal("port=9090", File.ReadAllText(file));

        Assert.True(service.RestoreBackup(file));
        Assert.Equal("port=8080", File.ReadAllText(file));
    }

    [Fact]
    public void RestoreBackup_SwapsTheTwo_SoItUndoesItself()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();
        var file = WriteFile(temp, "app.ini", "port=8080");

        var document = service.Load(file, ConfigFormat.Auto);
        service.SaveChanges(document, new Dictionary<string, string> { ["port"] = "9090" }, backup: true);

        service.RestoreBackup(file);
        Assert.Equal("port=9090", service.ReadBackup(file));

        // Bấm khôi phục lần nữa quay lại chỗ cũ — bấm nhầm không mất gì.
        service.RestoreBackup(file);
        Assert.Equal("port=9090", File.ReadAllText(file));
    }

    [Fact]
    public void RestoreBackup_ReportsFalse_WhenThereIsNothingToRestore()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();

        Assert.False(service.RestoreBackup(WriteFile(temp, "app.ini", "port=8080")));
    }

    [Fact]
    public void PreviewChanges_ProducesTheSameTextAsSaving_WithoutTouchingTheFile()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();
        var file = WriteFile(temp, "app.ini", "# giu nguyen\nport=8080");

        var document = service.Load(file, ConfigFormat.Auto);
        var changes = new Dictionary<string, string> { ["port"] = "9090" };

        var preview = service.PreviewChanges(document, changes);

        Assert.Contains("port=9090", preview);
        Assert.Contains("# giu nguyen", preview);
        Assert.Equal("# giu nguyen\nport=8080", File.ReadAllText(file));
        Assert.Equal(preview, service.SaveChanges(document, changes, backup: false));
    }
}
