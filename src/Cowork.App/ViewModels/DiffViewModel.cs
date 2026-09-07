using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Configuration;
using Cowork.Core.Localization;

namespace Cowork.App.ViewModels;

/// <summary>
/// Bảng so sánh hai văn bản. Dùng cho cả hai câu hỏi hay gặp: "mình sắp ghi gì lên file" và
/// "bản sao lưu khác bản hiện tại chỗ nào".
/// </summary>
public sealed partial class DiffViewModel : ObservableObject
{
    private readonly string _left;
    private readonly string _right;

    /// <param name="restore">Hành động khôi phục; null nghĩa là bảng chỉ để xem.</param>
    public DiffViewModel(
        string title, string leftLabel, string rightLabel, string? left, string? right, Action? restore = null)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;
        _left = left ?? string.Empty;
        _right = right ?? string.Empty;
        RestoreAction = restore;

        Lines = new ObservableCollection<DiffLine>();
        Refresh();
    }

    public string Title { get; }
    public string LeftLabel { get; }
    public string RightLabel { get; }

    private Action? RestoreAction { get; }

    public bool CanRestore => RestoreAction is not null;

    public ObservableCollection<DiffLine> Lines { get; }

    /// <summary>Chỉ hiện các cụm đã đổi kèm vài dòng ngữ cảnh; tắt để xem cả file.</summary>
    [ObservableProperty]
    private bool _onlyChanges = true;

    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>Người dùng đã bấm khôi phục — cửa sổ gọi để biết có phải nạp lại không.</summary>
    public bool Restored { get; private set; }

    partial void OnOnlyChangesChanged(bool value) => Refresh();

    private void Refresh()
    {
        var full = LineDiff.Compare(_left, _right);
        var shown = OnlyChanges ? LineDiff.OnlyChanges(full) : full;

        Lines.Clear();
        foreach (var line in shown.Lines)
            Lines.Add(line);

        Summary = full.HasChanges
            ? Loc.T("Diff.Summary", full.Added, full.Removed)
              + (full.Truncated ? Loc.T("Diff.TruncatedSuffix") : string.Empty)
            : Loc.T("Diff.Identical");
    }

    [RelayCommand]
    private void Restore()
    {
        RestoreAction?.Invoke();
        Restored = true;
    }
}
