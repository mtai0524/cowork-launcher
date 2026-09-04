namespace Cowork.Core.Configuration;

/// <summary>Loại chương trình tìm được — quyết định cách Cowork dựng lệnh chạy.</summary>
public enum ProgramKind
{
    /// <summary>File .exe / .com — chạy trực tiếp được.</summary>
    Executable = 0,

    /// <summary>File .bat / .cmd — chạy trực tiếp được.</summary>
    BatchScript = 1,

    /// <summary>File .ps1 — phải gọi qua powershell.exe, không chạy thẳng được.</summary>
    PowerShellScript = 2,
}

/// <summary>Một chương trình ứng viên tìm được khi quét thư mục.</summary>
public sealed record ProgramCandidate(
    string FullPath,
    string RelativePath,
    ProgramKind Kind,
    ScanConfidence Confidence,
    string Reason,
    long SizeBytes);

public sealed class ProgramScanOptions
{
    public int MaxDepth { get; set; } = 4;
    public int MaxResults { get; set; } = 300;

    /// <summary>Có trả về cả các ứng viên mức Thấp hay không.</summary>
    public bool IncludeLowConfidence { get; set; } = true;

    public static ProgramScanOptions Default => new();
}

public sealed record ProgramScanResult(
    IReadOnlyList<ProgramCandidate> Candidates, bool Truncated, int FilesInspected);

public interface IProgramScanner
{
    ProgramScanResult Scan(string rootDirectory, ProgramScanOptions? options = null);
}

/// <summary>
/// Quét một thư mục để tìm chương trình có thể chạy được.
///
/// Khác biệt quan trọng so với <see cref="ConfigFileScanner"/>: danh sách thư mục nhiễu
/// gần như ngược lại. Với file cấu hình thì <c>bin</c>, <c>dist</c>, <c>publish</c> là rác;
/// với chương trình thì đó chính là nơi file .exe nằm.
/// </summary>
public sealed class ProgramScanner : IProgramScanner
{
    /// <summary>Chỉ những đuôi mà Cowork thật sự khởi chạy được.</summary>
    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1",
    };

    /// <summary>
    /// Thư mục nhiễu khi tìm chương trình. Cố ý <em>không</em> loại
    /// <c>bin</c>/<c>dist</c>/<c>build</c>/<c>publish</c> vì đó là nơi chứa file chạy.
    /// </summary>
    private static readonly HashSet<string> NoiseDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "obj", "packages", "__pycache__", "vendor",
        "coverage", "TestResults", "logs", "temp", "tmp",
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea", ".venv", "venv",
        ".gradle", ".nuget", ".cache", ".pytest_cache", ".mypy_cache",
        ".next", ".nuxt", ".turbo", ".yarn",
    };

    /// <summary>Thư mục là đích build — file chạy nằm đây thì đáng tin hơn nằm lung tung.</summary>
    private static readonly HashSet<string> OutputDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "dist", "build", "out", "publish", "release", "debug", "target", "app",
    };

    /// <summary>Tên gợi ý đây là điểm khởi chạy chính của thư mục.</summary>
    private static readonly HashSet<string> LauncherNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "run", "start", "startup", "launch", "main", "app", "cli",
        "chay", "khoichay", "khoi-chay", "batdau", "bat-dau",
    };

    /// <summary>File chạy được nhưng gần như chắc chắn không phải thứ cần chạy hằng ngày.</summary>
    private static readonly string[] InstallerPrefixes =
    {
        "unins", "uninstall", "setup", "install", "vcredist", "vc_redist", "dotnet-install",
    };

    /// <summary>Tàn dư của trình build/gỡ lỗi, không bao giờ là thứ người dùng muốn chạy.</summary>
    private static readonly string[] BuildArtefactMarkers =
    {
        ".vshost.", "crashpad", "createdump", "apphost", "singlefilehost",
    };

    public ProgramScanResult Scan(string rootDirectory, ProgramScanOptions? options = null)
    {
        options ??= ProgramScanOptions.Default;

        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Chưa chọn thư mục cần quét.", nameof(rootDirectory));

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Không tìm thấy thư mục: " + root);

        var candidates = new List<ProgramCandidate>();
        var inspected = 0;
        var truncated = false;

        Walk(root, root, 0, options, candidates, ref inspected, ref truncated);

        var ordered = candidates
            .Where(c => options.IncludeLowConfidence || c.Confidence > ScanConfidence.Low)
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ProgramScanResult(ordered, truncated, inspected);
    }

    private void Walk(
        string root,
        string directory,
        int depth,
        ProgramScanOptions options,
        List<ProgramCandidate> sink,
        ref int inspected,
        ref bool truncated)
    {
        if (sink.Count >= options.MaxResults)
        {
            truncated = true;
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (sink.Count >= options.MaxResults)
            {
                truncated = true;
                return;
            }

            inspected++;
            var candidate = Evaluate(root, file);
            if (candidate is not null)
                sink.Add(candidate);
        }

        if (depth >= options.MaxDepth)
            return;

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var child in directories)
        {
            if (NoiseDirectories.Contains(Path.GetFileName(child)))
                continue;

            Walk(root, child, depth + 1, options, sink, ref inspected, ref truncated);
        }
    }

    private static ProgramCandidate? Evaluate(string root, string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);

        if (!RunnableExtensions.Contains(extension))
            return null;

        if (BuildArtefactMarkers.Any(m => fileName.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return null;

        long size;
        try
        {
            size = new FileInfo(fullPath).Length;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        var kind = extension.ToLowerInvariant() switch
        {
            ".bat" or ".cmd" => ProgramKind.BatchScript,
            ".ps1" => ProgramKind.PowerShellScript,
            _ => ProgramKind.Executable,
        };

        var (confidence, reason) = Score(root, fullPath, fileName);
        var relative = Path.GetRelativePath(root, fullPath);

        return new ProgramCandidate(fullPath, relative, kind, confidence, reason, size);
    }

    private static (ScanConfidence, string) Score(string root, string fullPath, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);

        // Trình cài đặt/gỡ cài đặt chạy được nhưng không phải việc hằng ngày.
        if (InstallerPrefixes.Any(p => stem.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return (ScanConfidence.Low, "Có vẻ là trình cài đặt/gỡ cài đặt");

        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var isAtRoot = string.Equals(directory, root, StringComparison.OrdinalIgnoreCase);
        var rootName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));

        // Tên file trùng tên thư mục gốc: gcm\gcm.exe, MyTool\MyTool.exe.
        if (string.Equals(stem, rootName, StringComparison.OrdinalIgnoreCase))
            return (ScanConfidence.High, $"Trùng tên thư mục \"{rootName}\"");

        if (LauncherNames.Contains(stem))
            return (ScanConfidence.High, $"Tên gợi ý điểm khởi chạy (\"{stem}\")");

        // Tên chứa "run"/"start"/"chay" kèm hậu tố, ví dụ run-backup.bat.
        if (LauncherNames.Any(n => stem.StartsWith(n + "-", StringComparison.OrdinalIgnoreCase)
                                   || stem.StartsWith(n + "_", StringComparison.OrdinalIgnoreCase)))
        {
            return (ScanConfidence.High, "Tên bắt đầu bằng từ khoá khởi chạy");
        }

        if (isAtRoot)
            return (ScanConfidence.Medium, "Nằm ngay thư mục gốc");

        var parent = Path.GetFileName(directory);
        if (OutputDirectoryNames.Contains(parent))
            return (ScanConfidence.Medium, $"Nằm trong thư mục kết quả build \"{parent}\"");

        return (ScanConfidence.Low, "Nằm sâu trong cây thư mục");
    }

    /// <summary>
    /// Dựng lệnh chạy từ một ứng viên. File .ps1 không khởi chạy trực tiếp được nên
    /// được bọc qua powershell.exe — nếu không, app sẽ lỗi ngay lần chạy đầu.
    /// </summary>
    public static (string ExecutablePath, string Arguments) BuildCommand(ProgramCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Kind != ProgramKind.PowerShellScript)
            return (candidate.FullPath, string.Empty);

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

        return (powershell, $"-NoProfile -ExecutionPolicy Bypass -File \"{candidate.FullPath}\"");
    }
}
