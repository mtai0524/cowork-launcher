using Cowork.Core.Localization;
namespace Cowork.Core.Configuration;

/// <summary>
/// Mức tin cậy của một phỏng đoán khi quét thư mục — dùng chung cho cả bộ quét file
/// cấu hình lẫn bộ quét chương trình.
/// </summary>
public enum ScanConfidence
{
    /// <summary>Đúng phần mở rộng nhưng không có dấu hiệu nào khác. Cần người dùng tự xét.</summary>
    Low = 0,

    /// <summary>Phần mở rộng gần như luôn là cấu hình, hoặc nằm trong thư mục tên "config".</summary>
    Medium = 1,

    /// <summary>Tên file khớp mẫu quen thuộc (appsettings.json, web.config, .env…).</summary>
    High = 2,
}

/// <summary>Một file ứng viên tìm được khi quét thư mục.</summary>
public sealed record ConfigCandidate(
    string FullPath,
    string RelativePath,
    ConfigFormat Format,
    ScanConfidence Confidence,
    string Reason,
    long SizeBytes,
    bool ParsedSuccessfully);

public sealed class ConfigScanOptions
{
    /// <summary>Số cấp thư mục con tối đa. 0 = chỉ quét ngay trong thư mục gốc.</summary>
    public int MaxDepth { get; set; } = 4;

    /// <summary>Bỏ qua file quá lớn — file cấu hình hiếm khi vượt vài trăm KB.</summary>
    public long MaxFileSizeBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Trần số kết quả để quét thư mục khổng lồ không treo giao diện.</summary>
    public int MaxResults { get; set; } = 500;

    /// <summary>Có trả về cả các ứng viên mức Thấp hay không.</summary>
    public bool IncludeLowConfidence { get; set; } = true;

    public static ConfigScanOptions Default => new();
}

public sealed record ConfigScanResult(IReadOnlyList<ConfigCandidate> Candidates, bool Truncated, int FilesInspected);

public interface IConfigFileScanner
{
    ConfigScanResult Scan(string rootDirectory, ConfigScanOptions? options = null);
}

/// <summary>
/// Quét một thư mục để đoán xem file nào là file cấu hình.
///
/// Cách tiếp cận có ba tầng lọc, cố ý theo thứ tự rẻ trước đắt sau:
/// lọc theo phần mở rộng → chấm điểm theo tên file và thư mục cha → thử phân tích cú pháp thật.
/// Kết quả luôn là <em>gợi ý</em>: người dùng vẫn phải tick chọn, vì đoán sai ở đây
/// dẫn tới ghi đè nhầm file, hậu quả nặng hơn nhiều so với việc phải chọn tay.
/// </summary>
public sealed class ConfigFileScanner : IConfigFileScanner
{
    private readonly IConfigFileService _configService;

    public ConfigFileScanner(IConfigFileService configService) => _configService = configService;

    public ConfigFileScanner() : this(new ConfigFileService())
    {
    }

    /// <summary>Chỉ những phần mở rộng này mới được xét tới.</summary>
    private static readonly HashSet<string> CandidateExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".jsonc", ".ini", ".cfg", ".conf", ".properties", ".env",
        ".xml", ".config", ".yaml", ".yml", ".toml",
    };

    /// <summary>
    /// Thư mục sinh ra bởi công cụ — quét vào chỉ tốn thời gian và tạo nhiễu.
    /// Danh sách này phải liệt kê tường minh cả các thư mục bắt đầu bằng dấu chấm:
    /// bỏ qua mọi thư mục dấu chấm là sai, vì <c>.config</c> và <c>.ssh</c> chính là
    /// nơi hay chứa cấu hình nhất.
    /// </summary>
    private static readonly HashSet<string> NoiseDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "build", "out",
        "packages", "venv", "env", "__pycache__",
        "target", "vendor", "coverage", "TestResults", "logs", "temp", "tmp",
        ".git", ".svn", ".hg", ".vs", ".vscode", ".idea", ".venv", ".gradle",
        ".next", ".nuxt", ".nuget", ".cache", ".pytest_cache", ".mypy_cache",
        ".terraform", ".tox", ".angular", ".parcel-cache", ".turbo", ".yarn",
    };

    /// <summary>
    /// File không có phần mở rộng nhưng tên đã nói rõ nó là cấu hình.
    /// Rất phổ biến trên các công cụ gốc Unix: <c>~/.config/&lt;app&gt;/config</c>,
    /// <c>~/.ssh/config</c>, <c>~/.aws/config</c>.
    /// </summary>
    private static readonly HashSet<string> ExtensionlessConfigNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "configuration", "conf", "cfg", "settings", "setting",
        "options", "preferences", "environment", "hosts", "cauhinh", "cau-hinh",
    };

    /// <summary>File do công cụ sinh ra, đúng phần mở rộng nhưng không ai sửa tay bao giờ.</summary>
    private static readonly HashSet<string> GeneratedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml",
        "composer.lock", "project.assets.json", "packages.lock.json",
        "project.nuget.cache", "tsconfig.tsbuildinfo", "launchSettings.json",
    };

    /// <summary>
    /// Manifest của dự án/công cụ: đúng là file cấu hình theo nghĩa rộng, nhưng không phải
    /// thứ người dùng sửa để đổi hành vi lúc chạy. Luôn hạ xuống mức Thấp thay vì loại hẳn,
    /// vì thỉnh thoảng vẫn có người cần sửa chúng.
    /// </summary>
    private static readonly HashSet<string> ProjectManifestNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "manifest.json", "tsconfig.json", "jsconfig.json", "composer.json",
        "bower.json", "angular.json", "nx.json", "deno.json", "jest.config.json",
    };

    /// <summary>Hậu tố cho biết đây là file mẫu, không phải cấu hình đang dùng thật.</summary>
    private static readonly string[] TemplateMarkers = { ".example", ".sample", ".template", ".dist" };

    /// <summary>Thư mục mang tên này thì file bên trong nhiều khả năng là cấu hình.</summary>
    private static readonly HashSet<string> ConfigDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "configs", "conf", "settings", "cauhinh", "cau-hinh", "etc",
    };

    /// <summary>Phần mở rộng mà bản thân nó đã gần như khẳng định là file cấu hình.</summary>
    private static readonly HashSet<string> StrongExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ini", ".cfg", ".conf", ".properties", ".env",
    };

    public ConfigScanResult Scan(string rootDirectory, ConfigScanOptions? options = null)
    {
        options ??= ConfigScanOptions.Default;

        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Chưa chọn thư mục cần quét.", nameof(rootDirectory));

        var root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rootDirectory));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Không tìm thấy thư mục: " + root);

        var candidates = new List<ConfigCandidate>();
        var inspected = 0;
        var truncated = false;

        Walk(root, root, 0, options, candidates, ref inspected, ref truncated);

        var ordered = candidates
            .Where(c => options.IncludeLowConfidence || c.Confidence > ScanConfidence.Low)
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConfigScanResult(ordered, truncated, inspected);
    }

    private void Walk(
        string root,
        string directory,
        int depth,
        ConfigScanOptions options,
        List<ConfigCandidate> sink,
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
            // Thư mục không đọc được thì bỏ qua, không làm hỏng cả lượt quét.
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
            var candidate = Evaluate(root, file, options);
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
            var name = Path.GetFileName(child);
            if (NoiseDirectories.Contains(name))
                continue;

            Walk(root, child, depth + 1, options, sink, ref inspected, ref truncated);
        }
    }

    /// <summary>Chấm điểm một file. Trả về null nếu chắc chắn không phải file cấu hình.</summary>
    private ConfigCandidate? Evaluate(string root, string fullPath, ConfigScanOptions options)
    {
        var fileName = Path.GetFileName(fullPath);

        // "config.example" phải được xét như "config": bỏ hậu tố mẫu rồi mới đọc phần mở rộng.
        var effectiveName = StripTemplateSuffix(fileName);
        var extension = Path.GetExtension(effectiveName);

        // ".env", ".env.local" bị GetExtension trả về sai, nên xét riêng theo tên.
        var isDotEnv = fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase);

        // File không đuôi nhưng tên là "config"/"settings"… — rất phổ biến với công cụ
        // gốc Unix. Nếu bỏ nhánh này thì ~/.config/<app>/config không bao giờ được tìm thấy.
        var isNamedConfig = extension.Length == 0 && IsConfigLikeName(effectiveName);

        // File kiểu rc: .npmrc, .babelrc, .prettierrc.
        var isRcFile = fileName.StartsWith('.')
                       && fileName.EndsWith("rc", StringComparison.OrdinalIgnoreCase);

        if (!isDotEnv && !isNamedConfig && !isRcFile && !CandidateExtensions.Contains(extension))
            return null;

        if (GeneratedFileNames.Contains(fileName))
            return null;

        // File .min.json / .g.xml gần như luôn là sản phẩm build.
        if (fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".g.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        long size;
        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        if (size > options.MaxFileSizeBytes)
            return null;

        // "configure", "config.sh"… là script chứ không phải cấu hình. Shebang là dấu hiệu
        // rẻ và chắc chắn nhất; chỉ cần kiểm tra ở nhánh không đuôi vì file .json/.ini
        // thì không bao giờ có shebang.
        if ((isNamedConfig || isRcFile) && StartsWithShebang(fullPath))
            return null;

        var format = ResolveFormat(fullPath, effectiveName, isDotEnv, sniffAllowed: isNamedConfig || isRcFile);
        var (confidence, reason) = Score(root, fullPath, fileName, extension, isDotEnv, isNamedConfig, isRcFile);

        // Tầng lọc cuối: file JSON/XML không phân tích được thì hạ một bậc tin cậy.
        var parsed = TryParse(fullPath, format, out var parseNote);
        if (!parsed && confidence > ScanConfidence.Low)
        {
            confidence--;
            reason = parseNote ?? reason;
        }

        var relative = Path.GetRelativePath(root, fullPath);
        return new ConfigCandidate(fullPath, relative, format, confidence, reason, size, parsed);
    }

    /// <summary>File mở đầu bằng <c>#!</c> là script thực thi, không phải file cấu hình.</summary>
    private static bool StartsWithShebang(string fullPath)
    {
        try
        {
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.ReadByte() == '#' && stream.ReadByte() == '!';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Tên file không đuôi có phải kiểu "config", "settings", "myapp-config" không.</summary>
    private static bool IsConfigLikeName(string fileName)
        => ExtensionlessConfigNames.Contains(fileName)
           || fileName.Contains("config", StringComparison.OrdinalIgnoreCase)
           || fileName.Contains("setting", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bỏ hậu tố mẫu: "config.example" → "config", "appsettings.json.sample" → "appsettings.json".</summary>
    private static string StripTemplateSuffix(string fileName)
    {
        foreach (var marker in TemplateMarkers)
        {
            if (fileName.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                return fileName[..^marker.Length];
        }

        return fileName;
    }

    /// <summary>
    /// Xác định định dạng. Phần mở rộng là căn cứ đầu tiên.
    /// Chỉ đoán theo nội dung khi file thật sự không có đuôi — file <c>.yaml</c>/<c>.toml</c>
    /// đã biết rõ là gì rồi, đoán thêm chỉ tổ nhận nhầm (một dòng YAML có dấu <c>=</c>
    /// bên trong sẽ bị tưởng là INI).
    /// </summary>
    private static ConfigFormat ResolveFormat(
        string fullPath, string effectiveName, bool isDotEnv, bool sniffAllowed)
    {
        if (isDotEnv)
            return ConfigFormat.Ini;

        var byExtension = ConfigFormat.Auto.Resolve(effectiveName);
        if (byExtension != ConfigFormat.PlainText)
            return byExtension;

        return sniffAllowed ? SniffFormat(fullPath) : ConfigFormat.PlainText;
    }

    /// <summary>
    /// Đoán định dạng từ vài KB đầu file. Chỉ trả về JSON/XML/INI khi thấy dấu hiệu rõ ràng;
    /// còn lại trả về PlainText để người dùng sửa thô — đoán bừa rồi hiện bảng rỗng
    /// gây khó hiểu hơn là nói thẳng "định dạng này chỉ sửa nguồn được".
    /// </summary>
    private static ConfigFormat SniffFormat(string fullPath)
    {
        string head;
        try
        {
            using var reader = new StreamReader(fullPath, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[SniffLength];
            var read = reader.Read(buffer, 0, buffer.Length);
            head = new string(buffer, 0, Math.Max(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigFormat.PlainText;
        }

        if (head.Length == 0)
            return ConfigFormat.PlainText;

        var lines = head.Split('\n');
        var meaningful = lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] != '#' && l[0] != ';')
            .ToList();

        if (meaningful.Count == 0)
            return ConfigFormat.PlainText;

        var first = meaningful[0];

        if (first[0] == '<')
            return ConfigFormat.Xml;

        if (first[0] == '{')
            return ConfigFormat.Json;

        // '[' vừa là mở mảng JSON vừa là mở section INI — phân biệt bằng hình dạng dòng.
        var looksLikeSection = meaningful.Any(IsIniSectionLine);
        if (first[0] == '[')
            return looksLikeSection ? ConfigFormat.Ini : ConfigFormat.Json;

        // Có ít nhất một dòng "khoá = giá trị" thì coi là INI.
        var hasAssignment = meaningful.Any(line =>
        {
            var separator = line.IndexOf('=');
            return separator > 0 && line[..separator].Trim().Length > 0;
        });

        if (hasAssignment || looksLikeSection)
            return ConfigFormat.Ini;

        return ConfigFormat.PlainText;
    }

    private static bool IsIniSectionLine(string line)
        => line.Length > 2 && line[0] == '[' && line[^1] == ']' && !line.Contains(',');

    /// <summary>Số ký tự đầu file dùng để đoán định dạng — đủ để nhìn ra cấu trúc.</summary>
    private const int SniffLength = 4096;

    private static (ScanConfidence, string) Score(
        string root, string fullPath, string fileName, string extension,
        bool isDotEnv, bool isNamedConfig, bool isRcFile)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);

        if (ProjectManifestNames.Contains(fileName))
            return (ScanConfidence.Low, Loc.T("Reason.ProjectManifest"));

        // File mẫu (.env.example) chỉ để tham khảo — không nên tick sẵn cho người dùng.
        var isTemplate = TemplateMarkers.Any(marker =>
            fileName.EndsWith(marker, StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(marker + ".", StringComparison.OrdinalIgnoreCase));

        if (isDotEnv)
        {
            return isTemplate
                ? (ScanConfidence.Medium, Loc.T("Reason.DotEnvTemplate"))
                : (ScanConfidence.High, Loc.T("Reason.DotEnv"));
        }

        if (isTemplate)
            return (ScanConfidence.Medium, Loc.T("Reason.Template"));

        // File không đuôi tên "config"/"settings" — tên đã là bằng chứng đủ mạnh.
        if (isNamedConfig)
        {
            return ExtensionlessConfigNames.Contains(fileName)
                ? (ScanConfidence.High, Loc.T("Reason.ExtensionlessNamed", fileName))
                : (ScanConfidence.High, Loc.T("Reason.ExtensionlessContains"));
        }

        if (isRcFile)
            return (ScanConfidence.High, Loc.T("Reason.RcFile", fileName));

        if (fileName.Equals("web.config", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("app.config", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".exe.config", StringComparison.OrdinalIgnoreCase))
        {
            return (ScanConfidence.High, Loc.T("Reason.DotNetConfig"));
        }

        if (stem.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            return (ScanConfidence.High, Loc.T("Reason.AspNetCoreConfig"));

        if (stem.StartsWith("application", StringComparison.OrdinalIgnoreCase)
            && extension is ".properties" or ".yml" or ".yaml")
        {
            return (ScanConfidence.High, Loc.T("Reason.SpringConfig"));
        }

        // Tên file tự nói lên nó là cấu hình.
        if (stem.Contains("config", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("setting", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("cauhinh", StringComparison.OrdinalIgnoreCase))
        {
            return (ScanConfidence.High, Loc.T("Reason.NameContainsConfig"));
        }

        if (extension.Equals(".config", StringComparison.OrdinalIgnoreCase))
            return (ScanConfidence.High, Loc.T("Reason.ConfigExtension"));

        if (StrongExtensions.Contains(extension))
            return (ScanConfidence.Medium, Loc.T("Reason.StrongExtension", extension));

        // Nằm trong thư mục tên "config", "settings"…
        var parent = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? string.Empty);
        if (ConfigDirectoryNames.Contains(parent))
            return (ScanConfidence.Medium, Loc.T("Reason.InConfigDirectory", parent));

        // Nằm ngay thư mục gốc của app thì khả năng cao hơn nằm sâu bên trong.
        var isAtRoot = string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase);
        return isAtRoot
            ? (ScanConfidence.Medium, Loc.T("Reason.AtAppRoot"))
            : (ScanConfidence.Low, Loc.T("Reason.ExtensionOnly", extension));
    }

    private bool TryParse(string fullPath, ConfigFormat format, out string? note)
    {
        note = null;

        var resolved = format.Resolve(fullPath);
        if (resolved == ConfigFormat.PlainText)
            return true;   // Không có bộ phân tích thì coi như không có ý kiến.

        try
        {
            var document = _configService.Load(fullPath, format);
            if (document.ParseError is null)
                return true;

            note = Loc.T("Reason.SyntaxProblem");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = Loc.T("Reason.Unreadable");
            return false;
        }
    }
}
