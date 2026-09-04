using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

public class ConfigFileScannerTests
{
    private static void Write(string root, string relativePath, string content = "{}")
    {
        var full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Ghép các dòng lại — tránh phải nhét ký tự xuống dòng vào chuỗi trong test.</summary>
    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static ConfigCandidate? Find(ConfigScanResult result, string fileName)
        => result.Candidates.FirstOrDefault(c =>
            Path.GetFileName(c.FullPath).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Finds_WellKnownConfigFiles_WithHighConfidence()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");
        Write(temp.Path, "App.config", "<configuration><a /></configuration>");
        Write(temp.Path, ".env", "KEY=value");
        Write(temp.Path, "database.ini", "[db]\nhost = localhost");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Equal(ScanConfidence.High, Find(result, "appsettings.json")!.Confidence);
        Assert.Equal(ScanConfidence.High, Find(result, "App.config")!.Confidence);
        Assert.Equal(ScanConfidence.High, Find(result, ".env")!.Confidence);
        // "database.ini" không khớp mẫu tên nào nhưng .ini là phần mở rộng mạnh.
        Assert.True(Find(result, "database.ini")!.Confidence >= ScanConfidence.Medium);
    }

    [Fact]
    public void DetectsCorrectFormat()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");
        Write(temp.Path, "settings.ini", "[s]\nk = v");
        Write(temp.Path, "Web.config", "<configuration />");
        Write(temp.Path, ".env", "K=V");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Equal(ConfigFormat.Json, Find(result, "appsettings.json")!.Format);
        Assert.Equal(ConfigFormat.Ini, Find(result, "settings.ini")!.Format);
        Assert.Equal(ConfigFormat.Xml, Find(result, "Web.config")!.Format);
        Assert.Equal(ConfigFormat.Ini, Find(result, ".env")!.Format);
    }

    [Fact]
    public void SkipsGeneratedAndLockFiles()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "package-lock.json");
        Write(temp.Path, "project.assets.json");
        Write(temp.Path, "bundle.min.json");
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Null(Find(result, "package-lock.json"));
        Assert.Null(Find(result, "project.assets.json"));
        Assert.Null(Find(result, "bundle.min.json"));
        Assert.NotNull(Find(result, "appsettings.json"));
    }

    [Fact]
    public void SkipsNoiseDirectories()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("node_modules", "pkg", "config.json"));
        Write(temp.Path, Path.Combine("bin", "Debug", "App.config"), "<configuration />");
        Write(temp.Path, Path.Combine(".git", "config.json"));
        Write(temp.Path, Path.Combine("src", "config.json"), "{ \"a\": 1 }");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Single(result.Candidates);
        Assert.Contains("src", result.Candidates[0].RelativePath);
    }

    [Fact]
    public void SkipsFilesWithUnrelatedExtensions()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "readme.md", "# hello");
        Write(temp.Path, "run.bat", "echo hi");
        Write(temp.Path, "data.csv", "a,b");
        Write(temp.Path, "config.ini", "[s]\nk = v");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Single(result.Candidates);
        Assert.Equal("config.ini", Path.GetFileName(result.Candidates[0].FullPath));
    }

    [Fact]
    public void FilesInsideConfigFolder_RankAboveFilesBuriedElsewhere()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("config", "servers.json"), "{ \"a\": 1 }");
        Write(temp.Path, Path.Combine("src", "assets", "data", "servers.json"), "{ \"a\": 1 }");

        var result = new ConfigFileScanner().Scan(temp.Path);

        var inConfigDir = result.Candidates.First(c => c.RelativePath.Contains("config"));
        var buried = result.Candidates.First(c => c.RelativePath.Contains("assets"));

        Assert.True(inConfigDir.Confidence > buried.Confidence);
        Assert.Equal(ScanConfidence.Low, buried.Confidence);
    }

    [Fact]
    public void BrokenSyntax_LowersConfidenceAndIsFlagged()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "appsettings.json", "{ \"a\": ");   // JSON hỏng

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.False(candidate.ParsedSuccessfully);
        Assert.Equal(ScanConfidence.Medium, candidate.Confidence);   // bị hạ từ High
    }

    [Fact]
    public void RespectsMaxDepth()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("a", "b", "c", "deep.ini"), "[s]\nk = v");

        var shallow = new ConfigFileScanner().Scan(temp.Path, new ConfigScanOptions { MaxDepth = 1 });
        var deep = new ConfigFileScanner().Scan(temp.Path, new ConfigScanOptions { MaxDepth = 5 });

        Assert.Empty(shallow.Candidates);
        Assert.Single(deep.Candidates);
    }

    [Fact]
    public void RespectsMaxResults_AndReportsTruncation()
    {
        using var temp = new TempDirectory();
        for (var i = 0; i < 20; i++)
            Write(temp.Path, $"config{i}.ini", "[s]\nk = v");

        var result = new ConfigFileScanner().Scan(temp.Path, new ConfigScanOptions { MaxResults = 5 });

        Assert.Equal(5, result.Candidates.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void SkipsOversizedFiles()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "big.ini", new string('x', 5000));
        Write(temp.Path, "small.ini", "[s]\nk = v");

        var result = new ConfigFileScanner().Scan(temp.Path, new ConfigScanOptions { MaxFileSizeBytes = 1000 });

        Assert.Single(result.Candidates);
        Assert.Equal("small.ini", Path.GetFileName(result.Candidates[0].FullPath));
    }

    [Fact]
    public void CanHideLowConfidenceResults()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("src", "deep", "random.json"), "{ \"a\": 1 }");
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");

        var withLow = new ConfigFileScanner().Scan(temp.Path,
            new ConfigScanOptions { IncludeLowConfidence = true });
        var withoutLow = new ConfigFileScanner().Scan(temp.Path,
            new ConfigScanOptions { IncludeLowConfidence = false });

        Assert.Equal(2, withLow.Candidates.Count);
        Assert.Single(withoutLow.Candidates);
    }

    [Fact]
    public void ResultsAreOrderedByConfidence()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("src", "deep", "random.json"), "{ \"a\": 1 }");
        Write(temp.Path, "notes.ini", "[s]\nk = v");
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Equal(ScanConfidence.High, result.Candidates[0].Confidence);
        Assert.Equal(ScanConfidence.Low, result.Candidates[^1].Confidence);
    }

    [Fact]
    public void RelativePathIsReportedFromScanRoot()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("config", "db.ini"), "[s]\nk = v");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.Equal(Path.Combine("config", "db.ini"), candidate.RelativePath);
    }

    [Fact]
    public void ProjectManifests_AreDemotedToLow()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "package.json", "{ \"name\": \"x\" }");
        Write(temp.Path, "tsconfig.json", "{ \"compilerOptions\": {} }");
        Write(temp.Path, "appsettings.json", "{ \"a\": 1 }");

        var result = new ConfigFileScanner().Scan(temp.Path);

        // Nằm ngay thư mục gốc nên nếu không có luật riêng, chúng đã là Medium.
        Assert.Equal(ScanConfidence.Low, Find(result, "package.json")!.Confidence);
        Assert.Equal(ScanConfidence.Low, Find(result, "tsconfig.json")!.Confidence);
        Assert.Equal(ScanConfidence.High, Find(result, "appsettings.json")!.Confidence);
    }

    [Fact]
    public void TemplateFiles_RankBelowTheRealThing()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, ".env", "K=V");
        Write(temp.Path, ".env.example", "K=");
        Write(temp.Path, "config.sample.ini", "[s]\nk = v");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Equal(ScanConfidence.High, Find(result, ".env")!.Confidence);
        Assert.Equal(ScanConfidence.Medium, Find(result, ".env.example")!.Confidence);
        Assert.Equal(ScanConfidence.Medium, Find(result, "config.sample.ini")!.Confidence);
    }

    // ---------- File cấu hình không có phần mở rộng ----------

    [Fact]
    public void FindsExtensionlessFileNamedConfig()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "config", Lines("host = localhost", "port = 8080"));

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.Equal("config", Path.GetFileName(candidate.FullPath));
        Assert.Equal(ScanConfidence.High, candidate.Confidence);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("settings")]
    [InlineData("conf")]
    [InlineData("preferences")]
    [InlineData("myapp-config")]
    [InlineData("appsetting")]
    public void RecognisesConfigLikeNamesWithoutExtension(string fileName)
    {
        using var temp = new TempDirectory();
        Write(temp.Path, fileName, "key = value");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ScanConfidence.High, candidate.Confidence);
    }

    [Fact]
    public void IgnoresExtensionlessFilesWithUnrelatedNames()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "README", "xin chao");
        Write(temp.Path, "LICENSE", "MIT");
        Write(temp.Path, "Makefile", "all: build");

        Assert.Empty(new ConfigFileScanner().Scan(temp.Path).Candidates);
    }

    [Fact]
    public void IgnoresScriptsEvenWhenNamedLikeConfig()
    {
        using var temp = new TempDirectory();
        // "configure" của autotools: tên chứa "config" nhưng là script thực thi.
        Write(temp.Path, "configure", Lines("#!/bin/sh", "PREFIX=/usr/local", "echo building"));

        Assert.Empty(new ConfigFileScanner().Scan(temp.Path).Candidates);
    }

    [Fact]
    public void FindsRcFiles()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, ".npmrc", "registry=https://registry.npmjs.org/");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ScanConfidence.High, candidate.Confidence);
    }

    // ---------- Đoán định dạng theo nội dung ----------

    [Fact]
    public void SniffsIniFromKeyValueContent()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "config", Lines("# ghi chu", "api_key = abc123", "lang = vi"));

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.Equal(ConfigFormat.Ini, candidate.Format);
        Assert.True(candidate.ParsedSuccessfully);
    }

    [Fact]
    public void SniffsJsonFromLeadingBrace()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "config", "{ \"host\": \"localhost\", \"port\": 8080 }");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ConfigFormat.Json, candidate.Format);
    }

    [Fact]
    public void SniffsXmlFromLeadingAngleBracket()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "config", "<configuration><add key=\"a\" value=\"1\" /></configuration>");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ConfigFormat.Xml, candidate.Format);
    }

    [Fact]
    public void DistinguishesIniSectionFromJsonArray()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "config", Lines("[database]", "host = localhost"));
        Write(temp.Path, Path.Combine("sub", "config"), "[ { \"a\": 1 }, { \"a\": 2 } ]");

        var result = new ConfigFileScanner().Scan(temp.Path);

        var ini = result.Candidates.First(c => !c.RelativePath.Contains("sub"));
        var json = result.Candidates.First(c => c.RelativePath.Contains("sub"));

        Assert.Equal(ConfigFormat.Ini, ini.Format);
        Assert.Equal(ConfigFormat.Json, json.Format);
    }

    [Fact]
    public void FallsBackToPlainTextWhenContentHasNoRecognisableShape()
    {
        using var temp = new TempDirectory();
        // Kiểu file ssh_config: "Khoa gia-tri", không có dấu '='.
        Write(temp.Path, "config", Lines("Host github.com", "  User git", "  Port 22"));

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        // Thà nói thẳng "chỉ sửa nguồn được" còn hơn hiện một bảng rỗng.
        Assert.Equal(ConfigFormat.PlainText, candidate.Format);
    }

    [Fact]
    public void DoesNotSniffFilesWhoseExtensionAlreadySaysWhatTheyAre()
    {
        using var temp = new TempDirectory();
        // YAML của GitHub Actions có dòng chứa dấu '=' — không được vì thế mà bị tưởng là INI.
        Write(temp.Path, "workflow.yml", Lines(
            "on:",
            "  push:",
            "jobs:",
            "  build:",
            "    if: github.ref == 'refs/heads/main'"));

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ConfigFormat.PlainText, candidate.Format);
    }

    [Fact]
    public void FindsTemplateOfAnExtensionlessConfig()
    {
        using var temp = new TempDirectory();
        // Kiểu "config.example" — bỏ hậu tố mẫu rồi mới nhận ra đây là file "config".
        Write(temp.Path, "config.example", Lines("api_key = ", "lang = vi"));

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.Equal(ConfigFormat.Ini, candidate.Format);
        Assert.Equal(ScanConfidence.Medium, candidate.Confidence);   // là file mẫu nên không tick sẵn
    }

    [Fact]
    public void StripsTemplateSuffixBeforeReadingExtension()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "appsettings.json.sample", "{ \"a\": 1 }");

        var candidate = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);

        Assert.Equal(ConfigFormat.Json, candidate.Format);
    }

    // ---------- Thư mục bắt đầu bằng dấu chấm ----------

    [Fact]
    public void ScansDotDirectoriesThatHoldRealConfig()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine(".config", "myapp", "config"), "key = value");
        Write(temp.Path, Path.Combine(".ssh", "config"), "Host a");

        var result = new ConfigFileScanner().Scan(temp.Path);

        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void StillSkipsToolingDotDirectories()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine(".git", "config"), Lines("[core]", "bare = false"));
        Write(temp.Path, Path.Combine(".vs", "applicationhost.config"), "<configuration />");
        Write(temp.Path, Path.Combine(".config", "config"), "key = value");

        var found = Assert.Single(new ConfigFileScanner().Scan(temp.Path).Candidates);
        Assert.Contains(".config", found.RelativePath);
    }

    [Fact]
    public void MissingDirectory_Throws()
    {
        using var temp = new TempDirectory();
        var scanner = new ConfigFileScanner();

        Assert.Throws<DirectoryNotFoundException>(
            () => scanner.Scan(Path.Combine(temp.Path, "khong-ton-tai")));
    }
}
