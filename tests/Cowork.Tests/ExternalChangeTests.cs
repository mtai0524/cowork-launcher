using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

/// <summary>
/// Bịt lỗ "sửa file bằng công cụ khác rồi Cowork ghi đè im lặng". Dấu file phải bắt được mọi
/// thay đổi thực tế, và bắt hụt thì thà báo thừa còn hơn ghi đè mất nội dung của người khác.
/// </summary>
public class FileStampTests
{
    [Fact]
    public void Read_ReturnsNull_ForAMissingFile()
    {
        using var temp = new TempDirectory();

        Assert.Null(FileStamp.Read(Path.Combine(temp.Path, "khong-co.json")));
        Assert.Null(FileStamp.Read(null));
        Assert.Null(FileStamp.Read("   "));
    }

    [Fact]
    public void AnUntouchedFile_ReadsTheSameStampTwice()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "app.json");
        File.WriteAllText(file, """{ "a": 1 }""");

        Assert.False(FileStamp.HasChanged(FileStamp.Read(file), FileStamp.Read(file)));
    }

    [Fact]
    public void ADifferentLength_CountsAsChanged()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "app.json");
        File.WriteAllText(file, """{ "a": 1 }""");
        var before = FileStamp.Read(file);

        File.WriteAllText(file, """{ "a": 1, "b": 2 }""");

        Assert.True(FileStamp.HasChanged(before, FileStamp.Read(file)));
    }

    [Fact]
    public void ADifferentWriteTime_CountsAsChanged()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "app.json");
        File.WriteAllText(file, """{ "a": 1 }""");
        var before = FileStamp.Read(file);

        // Cùng kích thước, chỉ khác nội dung và giờ ghi — trường hợp một công cụ khác sửa tại chỗ.
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));

        Assert.True(FileStamp.HasChanged(before, FileStamp.Read(file)));
    }

    [Fact]
    public void ADeletedFile_CountsAsChanged()
    {
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "app.json");
        File.WriteAllText(file, "x");
        var before = FileStamp.Read(file);

        File.Delete(file);

        Assert.True(FileStamp.HasChanged(before, FileStamp.Read(file)));
    }

    [Fact]
    public void AMissingStamp_CountsAsChanged()
    {
        // Chưa chụp được dấu thì không có cơ sở để nói "không đổi" — hỏi thừa còn hơn ghi đè nhầm.
        using var temp = new TempDirectory();
        var file = Path.Combine(temp.Path, "app.json");
        File.WriteAllText(file, "x");

        Assert.True(FileStamp.HasChanged(null, FileStamp.Read(file)));
        Assert.True(FileStamp.HasChanged(null, null));
    }
}

public class ConfigExternalChangeTests
{
    private static (ConfigFileService Service, string File) Setup(TempDirectory temp, string content = """{ "port": 8080 }""")
    {
        var file = Path.Combine(temp.Path, "appsettings.json");
        File.WriteAllText(file, content);
        return (new ConfigFileService(), file);
    }

    [Fact]
    public void LoadedDocument_CarriesTheStampOfTheFile()
    {
        using var temp = new TempDirectory();
        var (service, file) = Setup(temp);

        var document = service.Load(file, ConfigFormat.Auto);

        Assert.NotNull(document.Stamp);
        Assert.False(service.HasChangedOnDisk(document));
    }

    [Fact]
    public void HasChangedOnDisk_SpotsAnEditFromAnotherTool()
    {
        using var temp = new TempDirectory();
        var (service, file) = Setup(temp);
        var document = service.Load(file, ConfigFormat.Auto);

        File.WriteAllText(file, """{ "port": 9090, "extra": true }""");

        Assert.True(service.HasChangedOnDisk(document));
    }

    [Fact]
    public void SavingThroughCowork_ThenReloading_ClearsTheFlag()
    {
        using var temp = new TempDirectory();
        var (service, file) = Setup(temp);
        var document = service.Load(file, ConfigFormat.Auto);

        service.SaveChanges(document, new Dictionary<string, string> { ["port"] = "9090" }, backup: false);

        // Chính Cowork vừa ghi, nên tài liệu cũ đã lỗi thời — đúng ra phải nạp lại.
        Assert.True(service.HasChangedOnDisk(document));
        Assert.False(service.HasChangedOnDisk(service.Load(file, ConfigFormat.Auto)));
    }

    [Fact]
    public void AMissingFile_HasNoStamp_AndCountsAsChanged()
    {
        using var temp = new TempDirectory();
        var service = new ConfigFileService();

        var document = service.Load(Path.Combine(temp.Path, "khong-co.ini"), ConfigFormat.Auto);

        Assert.Null(document.Stamp);
        Assert.NotNull(document.ParseError);
        Assert.True(service.HasChangedOnDisk(document));
    }
}
