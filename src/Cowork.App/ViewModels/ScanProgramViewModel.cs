using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;

namespace Cowork.App.ViewModels;

/// <summary>Một dòng kết quả quét chương trình.</summary>
public sealed class ProgramCandidateViewModel
{
    public ProgramCandidateViewModel(ProgramCandidate candidate) => Candidate = candidate;

    public ProgramCandidate Candidate { get; }

    public string RelativePath => Candidate.RelativePath;
    public string FullPath => Candidate.FullPath;
    public string Reason => Candidate.Reason;
    public ScanConfidence Confidence => Candidate.Confidence;

    /// <summary>Tên file — phần quan trọng nhất, không được để bị cắt mất.</summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>Thư mục chứa, hiển thị mờ ở dòng dưới.</summary>
    public string FolderLabel
    {
        get
        {
            var folder = Path.GetDirectoryName(RelativePath);
            return string.IsNullOrEmpty(folder) ? "(thư mục gốc)" : folder;
        }
    }

    public string KindLabel => Candidate.Kind switch
    {
        ProgramKind.BatchScript => "Batch",
        ProgramKind.PowerShellScript => "PowerShell",
        _ => "Chương trình",
    };

    public string ConfidenceLabel => Candidate.Confidence switch
    {
        ScanConfidence.High => "Cao",
        ScanConfidence.Medium => "Vừa",
        _ => "Thấp",
    };

    public string SizeLabel => Candidate.SizeBytes < 1024
        ? $"{Candidate.SizeBytes} B"
        : (Candidate.SizeBytes < 1024 * 1024
            ? $"{Candidate.SizeBytes / 1024.0:0.#} KB"
            : $"{Candidate.SizeBytes / (1024.0 * 1024):0.#} MB");
}

/// <summary>
/// View-model của hộp thoại "Quét thư mục tìm chương trình".
/// Khác hộp thoại quét cấu hình ở chỗ đây là chọn <em>một</em> — một app chỉ chạy một lệnh.
/// </summary>
public sealed partial class ScanProgramViewModel : ObservableObject
{
    private readonly IProgramScanner _scanner;

    public ScanProgramViewModel(IProgramScanner scanner, string rootDirectory)
    {
        _scanner = scanner;
        _rootDirectory = rootDirectory;
        Candidates = new ObservableCollection<ProgramCandidateViewModel>();
    }

    public ObservableCollection<ProgramCandidateViewModel> Candidates { get; }

    [ObservableProperty] private string _rootDirectory;
    [ObservableProperty] private bool _includeLowConfidence;
    [ObservableProperty] private int _maxDepth = 4;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "Đang quét…";
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    private ProgramCandidateViewModel? _selectedCandidate;

    partial void OnSelectedCandidateChanged(ProgramCandidateViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CommandPreview));
        OnPropertyChanged(nameof(IsPowerShellSelected));
    }

    /// <summary>Chỉ giải thích chuyện bọc powershell.exe khi thật sự đang chọn file .ps1.</summary>
    public bool IsPowerShellSelected
        => SelectedCandidate?.Candidate.Kind == ProgramKind.PowerShellScript;

    partial void OnIncludeLowConfidenceChanged(bool value) => ScanCommand.Execute(null);

    public bool HasSelection => SelectedCandidate is not null;

    /// <summary>
    /// Lệnh sẽ được điền vào app. Hiện sẵn cho người dùng thấy, vì với file .ps1
    /// thì đường dẫn chương trình lại là powershell.exe chứ không phải file họ chọn.
    /// </summary>
    public string CommandPreview
    {
        get
        {
            if (SelectedCandidate is not { } selected)
                return string.Empty;

            var (exe, args) = ProgramScanner.BuildCommand(selected.Candidate);
            return args.Length == 0 ? exe : exe + "  " + args;
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        ErrorMessage = null;
        StatusMessage = "Đang quét…";

        var root = RootDirectory;
        var options = new ProgramScanOptions
        {
            MaxDepth = Math.Clamp(MaxDepth, 0, 10),
            IncludeLowConfidence = IncludeLowConfidence,
        };

        try
        {
            var result = await Task.Run(() => _scanner.Scan(root, options)).ConfigureAwait(true);

            Candidates.Clear();
            foreach (var candidate in result.Candidates)
                Candidates.Add(new ProgramCandidateViewModel(candidate));

            // Chọn sẵn ứng viên đứng đầu để bấm Enter là xong trong trường hợp thường gặp.
            SelectedCandidate = Candidates.FirstOrDefault();
            StatusMessage = BuildSummary(result);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or ArgumentException
                                       or UnauthorizedAccessException or IOException)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Quét thất bại.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static string BuildSummary(ProgramScanResult result)
    {
        if (result.Candidates.Count == 0)
        {
            return $"Đã xem {result.FilesInspected} file, không thấy chương trình nào chạy được "
                   + "(.exe / .bat / .cmd / .ps1).";
        }

        var high = result.Candidates.Count(c => c.Confidence == ScanConfidence.High);
        var summary = $"Tìm thấy {result.Candidates.Count} chương trình "
                      + $"({high} ở mức tin cậy Cao) trong {result.FilesInspected} file đã xem.";

        return result.Truncated
            ? summary + " Đã đạt trần kết quả — thu hẹp thư mục để quét kỹ hơn."
            : summary;
    }
}
