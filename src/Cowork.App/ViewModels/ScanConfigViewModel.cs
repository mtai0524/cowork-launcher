using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;

namespace Cowork.App.ViewModels;

/// <summary>Một dòng kết quả quét, kèm ô tick chọn.</summary>
public sealed partial class ScanCandidateViewModel : ObservableObject
{
    public ScanCandidateViewModel(ConfigCandidate candidate, bool isSelected)
    {
        Candidate = candidate;
        _isSelected = isSelected;
    }

    public ConfigCandidate Candidate { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string RelativePath => Candidate.RelativePath;
    public string FullPath => Candidate.FullPath;
    public string FormatLabel => Candidate.Format.ToLabel();
    public string Reason => Candidate.Reason;
    public ScanConfidence Confidence => Candidate.Confidence;

    public string ConfidenceLabel => Candidate.Confidence switch
    {
        ScanConfidence.High => "Cao",
        ScanConfidence.Medium => "Vừa",
        _ => "Thấp",
    };

    public string SizeLabel => Candidate.SizeBytes < 1024
        ? $"{Candidate.SizeBytes} B"
        : $"{Candidate.SizeBytes / 1024.0:0.#} KB";

    /// <summary>Cảnh báo hiển thị khi file đúng dạng nhưng sai cú pháp.</summary>
    public bool HasSyntaxProblem => !Candidate.ParsedSuccessfully;
}

/// <summary>
/// View-model của hộp thoại "Quét thư mục tìm file cấu hình".
/// Mặc định chỉ tick sẵn những ứng viên mức Cao — người dùng vẫn phải xác nhận,
/// vì thêm nhầm một file rồi ghi đè lên nó là hỏng dữ liệu thật.
/// </summary>
public sealed partial class ScanConfigViewModel : ObservableObject
{
    private readonly IConfigFileScanner _scanner;

    /// <summary>Các đường dẫn đã có trong app, để không thêm trùng.</summary>
    private readonly HashSet<string> _alreadyAdded;

    public ScanConfigViewModel(
        IConfigFileScanner scanner,
        string rootDirectory,
        IEnumerable<string> alreadyAddedFullPaths)
    {
        _scanner = scanner;
        _rootDirectory = rootDirectory;
        _alreadyAdded = new HashSet<string>(alreadyAddedFullPaths, StringComparer.OrdinalIgnoreCase);

        Candidates = new ObservableCollection<ScanCandidateViewModel>();
    }

    public ObservableCollection<ScanCandidateViewModel> Candidates { get; }

    [ObservableProperty]
    private string _rootDirectory;

    [ObservableProperty]
    private bool _includeLowConfidence;

    [ObservableProperty]
    private int _maxDepth = 4;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "Bấm “Quét” để bắt đầu.";

    [ObservableProperty]
    private string? _errorMessage;

    public int SelectedCount => Candidates.Count(c => c.IsSelected);

    public bool HasResults => Candidates.Count > 0;

    /// <summary>Các file người dùng đã tick, dùng khi hộp thoại đóng với kết quả OK.</summary>
    public IReadOnlyList<ConfigCandidate> SelectedCandidates
        => Candidates.Where(c => c.IsSelected).Select(c => c.Candidate).ToList();

    partial void OnIncludeLowConfidenceChanged(bool value) => ScanCommand.Execute(null);

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
            return;

        IsScanning = true;
        ErrorMessage = null;
        StatusMessage = "Đang quét…";

        var root = RootDirectory;
        var options = new ConfigScanOptions
        {
            MaxDepth = Math.Clamp(MaxDepth, 0, 10),
            IncludeLowConfidence = IncludeLowConfidence,
        };

        try
        {
            // Quét chạm đĩa, đẩy sang thread pool để cửa sổ không đơ.
            var result = await Task.Run(() => _scanner.Scan(root, options)).ConfigureAwait(true);

            foreach (var existing in Candidates)
                existing.PropertyChanged -= OnCandidateChanged;
            Candidates.Clear();

            foreach (var candidate in result.Candidates)
            {
                // File đã khai báo rồi thì hiện nhưng không tick sẵn.
                var isNew = !_alreadyAdded.Contains(candidate.FullPath);
                var viewModel = new ScanCandidateViewModel(
                    candidate,
                    isSelected: isNew && candidate.Confidence == ScanConfidence.High);

                viewModel.PropertyChanged += OnCandidateChanged;
                Candidates.Add(viewModel);
            }

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
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    private string BuildSummary(ConfigScanResult result)
    {
        if (result.Candidates.Count == 0)
            return $"Đã xem {result.FilesInspected} file, không thấy file cấu hình nào.";

        var high = result.Candidates.Count(c => c.Confidence == ScanConfidence.High);
        var summary = $"Tìm thấy {result.Candidates.Count} file "
                      + $"({high} ở mức tin cậy Cao) trong {result.FilesInspected} file đã xem.";

        return result.Truncated
            ? summary + " Đã đạt trần kết quả — thu hẹp thư mục để quét kỹ hơn."
            : summary;
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanCandidateViewModel.IsSelected))
            OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void SelectHighConfidence() => SetSelection(c => c.Confidence == ScanConfidence.High);

    [RelayCommand]
    private void SelectAll() => SetSelection(_ => true);

    [RelayCommand]
    private void ClearSelection() => SetSelection(_ => false);

    private void SetSelection(Func<ScanCandidateViewModel, bool> predicate)
    {
        foreach (var candidate in Candidates)
            candidate.IsSelected = predicate(candidate);

        OnPropertyChanged(nameof(SelectedCount));
    }
}
