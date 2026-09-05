using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;
using Cowork.Core.Localization;

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
            return string.IsNullOrEmpty(folder) ? Loc.T("Scan.RootFolder") : folder;
        }
    }

    public string KindLabel => Candidate.Kind switch
    {
        ProgramKind.BatchScript => Loc.T("ProgramKind.Batch"),
        ProgramKind.PowerShellScript => Loc.T("ProgramKind.PowerShell"),
        _ => Loc.T("ProgramKind.Executable"),
    };

    public string ConfidenceLabel => Loc.T("Confidence." + Candidate.Confidence);

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
    [ObservableProperty] private string _statusMessage = Loc.T("Scan.Scanning");
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
        StatusMessage = Loc.T("Scan.Scanning");

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
            StatusMessage = Loc.T("Scan.Failed");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static string BuildSummary(ProgramScanResult result)
    {
        if (result.Candidates.Count == 0)
            return Loc.T("Scan.ProgramNone", result.FilesInspected);

        var high = result.Candidates.Count(c => c.Confidence == ScanConfidence.High);
        var summary = Loc.T("Scan.ProgramFound", result.Candidates.Count, high, result.FilesInspected);

        return result.Truncated ? summary + Loc.T("Scan.Truncated") : summary;
    }
}
