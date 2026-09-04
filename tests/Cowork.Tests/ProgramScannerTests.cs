using Cowork.Core.Configuration;
using Xunit;

namespace Cowork.Tests;

public class ProgramScannerTests
{
    private static void Write(string root, string relativePath, string content = "echo hi")
    {
        var full = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static ProgramCandidate? Find(ProgramScanResult result, string fileName)
        => result.Candidates.FirstOrDefault(c =>
            Path.GetFileName(c.FullPath).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void FindsRunnableFilesAndIgnoresEverythingElse()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "tool.exe");
        Write(temp.Path, "run.bat");
        Write(temp.Path, "task.cmd");
        Write(temp.Path, "report.ps1");
        Write(temp.Path, "readme.md");
        Write(temp.Path, "data.json");
        Write(temp.Path, "library.dll");

        var result = new ProgramScanner().Scan(temp.Path);

        Assert.Equal(4, result.Candidates.Count);
        Assert.Null(Find(result, "library.dll"));
        Assert.Null(Find(result, "data.json"));
    }

    [Fact]
    public void ClassifiesProgramKind()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "tool.exe");
        Write(temp.Path, "task.cmd");
        Write(temp.Path, "report.ps1");

        var result = new ProgramScanner().Scan(temp.Path);

        Assert.Equal(ProgramKind.Executable, Find(result, "tool.exe")!.Kind);
        Assert.Equal(ProgramKind.BatchScript, Find(result, "task.cmd")!.Kind);
        Assert.Equal(ProgramKind.PowerShellScript, Find(result, "report.ps1")!.Kind);
    }

    [Fact]
    public void FileMatchingFolderNameRanksHighest()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "mytool");
        Directory.CreateDirectory(project);
        Write(project, "mytool.exe");
        Write(project, "helper.exe");

        var result = new ProgramScanner().Scan(project);

        Assert.Equal(ScanConfidence.High, Find(result, "mytool.exe")!.Confidence);
        Assert.Equal(ScanConfidence.Medium, Find(result, "helper.exe")!.Confidence);
    }

    [Theory]
    [InlineData("run.bat")]
    [InlineData("start.cmd")]
    [InlineData("main.exe")]
    [InlineData("chay.bat")]
    [InlineData("run-backup.bat")]
    [InlineData("start_service.cmd")]
    public void LauncherNamesRankHigh(string fileName)
    {
        using var temp = new TempDirectory();
        Write(temp.Path, fileName);

        var candidate = Assert.Single(new ProgramScanner().Scan(temp.Path).Candidates);
        Assert.Equal(ScanConfidence.High, candidate.Confidence);
    }

    [Fact]
    public void ScansBuildOutputFolders_UnlikeTheConfigScanner()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("bin", "Release", "myapp.exe"));
        Write(temp.Path, Path.Combine("dist", "packer.exe"));

        var result = new ProgramScanner().Scan(temp.Path);

        // Với file cấu hình thì bin/dist là rác; với chương trình thì đó là nơi cần tìm.
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.Equal(ScanConfidence.Medium, c.Confidence));
    }

    [Fact]
    public void SkipsToolingDirectories()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("node_modules", ".bin", "tool.cmd"));
        Write(temp.Path, Path.Combine("obj", "leftover.exe"));
        Write(temp.Path, Path.Combine(".git", "hook.bat"));
        Write(temp.Path, "run.bat");

        var candidate = Assert.Single(new ProgramScanner().Scan(temp.Path).Candidates);
        Assert.Equal("run.bat", Path.GetFileName(candidate.FullPath));
    }

    [Fact]
    public void DemotesInstallers()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "unins000.exe");
        Write(temp.Path, "setup.exe");
        Write(temp.Path, "vcredist_x64.exe");
        Write(temp.Path, "mytool.exe");

        var result = new ProgramScanner().Scan(temp.Path);

        Assert.Equal(ScanConfidence.Low, Find(result, "unins000.exe")!.Confidence);
        Assert.Equal(ScanConfidence.Low, Find(result, "setup.exe")!.Confidence);
        Assert.Equal(ScanConfidence.Low, Find(result, "vcredist_x64.exe")!.Confidence);
        Assert.Equal(ScanConfidence.Medium, Find(result, "mytool.exe")!.Confidence);
    }

    [Fact]
    public void SkipsBuildArtefacts()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, "MyApp.vshost.exe");
        Write(temp.Path, "crashpad_handler.exe");
        Write(temp.Path, "MyApp.exe");

        var candidate = Assert.Single(new ProgramScanner().Scan(temp.Path).Candidates);
        Assert.Equal("MyApp.exe", Path.GetFileName(candidate.FullPath));
    }

    [Fact]
    public void ResultsAreOrderedByConfidence()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("tools", "deep", "misc.exe"));
        Write(temp.Path, "helper.exe");
        Write(temp.Path, "run.bat");

        var result = new ProgramScanner().Scan(temp.Path);

        Assert.Equal(ScanConfidence.High, result.Candidates[0].Confidence);
        Assert.Equal(ScanConfidence.Low, result.Candidates[^1].Confidence);
    }

    [Fact]
    public void RespectsMaxDepthAndMaxResults()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("a", "b", "c", "deep.exe"));

        var shallow = new ProgramScanner().Scan(temp.Path, new ProgramScanOptions { MaxDepth = 1 });
        var deep = new ProgramScanner().Scan(temp.Path, new ProgramScanOptions { MaxDepth = 5 });

        Assert.Empty(shallow.Candidates);
        Assert.Single(deep.Candidates);
    }

    [Fact]
    public void CanHideLowConfidenceResults()
    {
        using var temp = new TempDirectory();
        Write(temp.Path, Path.Combine("tools", "deep", "misc.exe"));
        Write(temp.Path, "run.bat");

        var withoutLow = new ProgramScanner().Scan(temp.Path,
            new ProgramScanOptions { IncludeLowConfidence = false });

        Assert.Single(withoutLow.Candidates);
    }

    [Fact]
    public void MissingDirectory_Throws()
    {
        using var temp = new TempDirectory();
        var scanner = new ProgramScanner();

        Assert.Throws<DirectoryNotFoundException>(
            () => scanner.Scan(Path.Combine(temp.Path, "khong-ton-tai")));
    }

    // ---------- Dựng lệnh chạy ----------

    [Fact]
    public void BuildCommand_UsesExecutableDirectly()
    {
        var candidate = new ProgramCandidate(
            @"C:\tools\run.bat", "run.bat", ProgramKind.BatchScript,
            ScanConfidence.High, "", 10);

        var (exe, args) = ProgramScanner.BuildCommand(candidate);

        Assert.Equal(@"C:\tools\run.bat", exe);
        Assert.Empty(args);
    }

    [Fact]
    public void BuildCommand_WrapsPowerShellScript()
    {
        var candidate = new ProgramCandidate(
            @"C:\tools\bao cao.ps1", "bao cao.ps1", ProgramKind.PowerShellScript,
            ScanConfidence.High, "", 10);

        var (exe, args) = ProgramScanner.BuildCommand(candidate);

        // .ps1 không tự chạy được — phải gọi qua powershell.exe, và đường dẫn
        // có dấu cách nên bắt buộc bọc nháy kép.
        Assert.EndsWith("powershell.exe", exe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-ExecutionPolicy Bypass", args);
        Assert.Contains("\"C:\\tools\\bao cao.ps1\"", args);
    }
}
