using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;
using Cowork.Core.Models;
using Cowork.Core.Localization;

namespace Cowork.App.ViewModels;

/// <summary>
/// Một file cấu hình đang mở: vừa ở chế độ bảng khoá-giá trị, vừa ở chế độ sửa text thô.
/// Hai chế độ dùng chung một nguồn sự thật là nội dung trên đĩa.
/// </summary>
public sealed partial class ConfigFileViewModel : ObservableObject
{
    private readonly IConfigFileService _service;
    private readonly Func<string> _workingDirectoryProvider;
    private ConfigDocument? _document;

    public ConfigFileViewModel(ConfigFileRef model, IConfigFileService service, Func<string> workingDirectoryProvider)
    {
        Model = model;
        _service = service;
        _workingDirectoryProvider = workingDirectoryProvider;

        Entries = new ObservableCollection<ConfigEntryViewModel>();
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConfigEntryViewModel.Section)));
        EntriesView.Filter = FilterEntry;
    }

    public ConfigFileRef Model { get; }

    public ObservableCollection<ConfigEntryViewModel> Entries { get; }

    public ICollectionView EntriesView { get; }

    public string DisplayName => Model.ResolveDisplayName();

    public string FullPath => Model.ResolveFullPath(_workingDirectoryProvider());

    public string FormatLabel => Model.Format.Resolve(FullPath).ToLabel();

    [ObservableProperty]
    private string _rawText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isRawMode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>File đã nạp được thành bảng hay chỉ sửa thô được.</summary>
    [ObservableProperty]
    private bool _supportsTable;

    /// <summary>
    /// File trên đĩa đã bị công cụ khác sửa kể từ lúc Cowork mở nó. Lưu lúc này sẽ ghi đè
    /// thay đổi của người kia, nên phải hỏi trước.
    /// </summary>
    [ObservableProperty]
    private bool _changedOnDisk;

    private string _loadedRawText = string.Empty;

    public bool HasUnsavedChanges =>
        IsRawMode
            ? !string.Equals(RawText, _loadedRawText, StringComparison.Ordinal)
            : Entries.Any(e => e.IsDirty);

    partial void OnSearchTextChanged(string value) => EntriesView.Refresh();

    partial void OnIsRawModeChanged(bool value) => OnPropertyChanged(nameof(HasUnsavedChanges));

    private bool FilterEntry(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        if (item is not ConfigEntryViewModel entry)
            return false;

        return entry.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || entry.Value.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Gọi khi thư mục làm việc của app đổi, khiến đường dẫn tương đối trỏ chỗ khác.</summary>
    public void NotifyPathChanged()
    {
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>
    /// Soi lại file trên đĩa xem có ai sửa không. Gọi từ nhịp làm tươi của giao diện, nên phải rẻ:
    /// chỉ đọc metadata của file, không đọc nội dung.
    /// </summary>
    public void CheckForExternalChange()
    {
        if (_document is null)
            return;

        ChangedOnDisk = _service.HasChangedOnDisk(_document);
    }

    [RelayCommand]
    public void Reload()
    {
        ErrorMessage = null;
        StatusMessage = null;
        ChangedOnDisk = false;
        Entries.Clear();

        var path = FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorMessage = Loc.T("Config.NoPath");
            SupportsTable = false;
            return;
        }

        try
        {
            _document = _service.Load(path, Model.Format);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = Loc.T("Config.ReadFailed", ex.Message);
            SupportsTable = false;
            return;
        }

        RawText = _document.RawText;
        _loadedRawText = _document.RawText;
        SupportsTable = _document.SupportsStructuredEditing;

        if (_document.ParseError is not null)
        {
            ErrorMessage = _document.ParseError;
            IsRawMode = true;
        }
        else if (!SupportsTable)
        {
            IsRawMode = true;
        }

        foreach (var entry in _document.Entries)
            Entries.Add(new ConfigEntryViewModel(entry));

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    /// <summary>
    /// Ghi thay đổi xuống đĩa. Trả về true nếu lưu thành công.
    /// Từ chối khi file đã bị sửa bên ngoài, trừ khi <paramref name="overwriteExternalChange"/> bật —
    /// đây chính là chỗ bịt lỗ ghi đè im lặng.
    /// </summary>
    public bool Save(bool overwriteExternalChange = false)
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (!overwriteExternalChange && _document is not null && _service.HasChangedOnDisk(_document))
        {
            ChangedOnDisk = true;
            ErrorMessage = Loc.T("Config.ChangedOnDisk");
            return false;
        }

        try
        {
            if (IsRawMode)
            {
                var validationError = _service.Validate(RawText, Model.Format, FullPath);
                if (validationError is not null)
                {
                    ErrorMessage = validationError;
                    return false;
                }

                _service.SaveRaw(FullPath, RawText, Model.BackupOnSave);
            }
            else
            {
                if (_document is null)
                {
                    ErrorMessage = Loc.T("Config.NotLoaded");
                    return false;
                }

                var changed = Entries
                    .Where(e => e.IsDirty)
                    .ToDictionary(e => e.Path, e => e.Value, StringComparer.Ordinal);

                if (changed.Count == 0)
                {
                    StatusMessage = Loc.T("Config.NoChanges");
                    return true;
                }

                _service.SaveChanges(_document, changed, Model.BackupOnSave);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ErrorMessage = Loc.T("Config.WriteFailed", ex.Message);
            return false;
        }

        Reload();
        StatusMessage = Loc.T("Config.SavedAt", DateTime.Now.ToString("HH:mm:ss"))
                        + (Model.BackupOnSave ? Loc.T("Config.BackupSuffix") : string.Empty);
        return true;
    }

    /// <summary>Nạp lại nhãn định dạng và nhãn kiểu của từng dòng sau khi đổi ngôn ngữ.</summary>
    public void RefreshLocalizedText()
    {
        foreach (var entry in Entries)
            entry.RefreshLocalizedText();

        OnPropertyChanged(nameof(FormatLabel));
    }

    [RelayCommand]
    private void SaveFile() => Save();

    /// <summary>Lưu đè lên thay đổi của công cụ khác, sau khi người dùng đã được hỏi.</summary>
    [RelayCommand]
    private void SaveOverwriting()
    {
        if (Save(overwriteExternalChange: true))
            StatusMessage += Loc.T("Config.OverwroteSuffix");
    }

    /// <summary>Bỏ mọi chỉnh sửa chưa lưu, nạp lại từ đĩa.</summary>
    [RelayCommand]
    private void Discard() => Reload();
}
