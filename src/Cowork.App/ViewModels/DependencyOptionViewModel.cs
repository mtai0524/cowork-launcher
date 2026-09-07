using CommunityToolkit.Mvvm.ComponentModel;
using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.App.ViewModels;

/// <summary>
/// Một dòng trong bảng phụ thuộc: một app khác, có tick hay không, và chờ tới mức nào.
/// Danh sách được dựng lại mỗi lần người dùng chọn app khác, nên nó luôn phản ánh
/// đúng các app đang có.
/// </summary>
public sealed partial class DependencyOptionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _suspendSync;

    public DependencyOptionViewModel(ManagedApp target, AppDependency? existing, Action changed)
    {
        Target = target;
        _changed = changed;

        _suspendSync = true;
        _isSelected = existing is not null;

        WaitOptions = new[] { DependencyWait.Completed, DependencyWait.Running }
            .Select(w => new ChoiceViewModel<DependencyWait>(w, x => Loc.T("DependencyWait." + x)))
            .ToList();
        _selectedWait = WaitOptions.First(o => o.Value == (existing?.Wait ?? DependencyWait.Completed));
        _suspendSync = false;
    }

    public ManagedApp Target { get; }

    public Guid TargetId => Target.Id;

    public string DisplayName => string.IsNullOrWhiteSpace(Target.Name) ? Loc.T("App.Untitled") : Target.Name;

    public IReadOnlyList<ChoiceViewModel<DependencyWait>> WaitOptions { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private ChoiceViewModel<DependencyWait>? _selectedWait;

    public DependencyWait Wait => SelectedWait?.Value ?? DependencyWait.Completed;

    partial void OnIsSelectedChanged(bool value) => Notify();

    partial void OnSelectedWaitChanged(ChoiceViewModel<DependencyWait>? value)
    {
        // ComboBox có lúc đẩy null trong lúc dựng lại danh sách; bỏ qua để không mất lựa chọn.
        if (value is not null)
            Notify();
    }

    public void RefreshLocalizedText()
    {
        foreach (var option in WaitOptions)
            option.RefreshLabel();

        OnPropertyChanged(nameof(DisplayName));
    }

    private void Notify()
    {
        if (_suspendSync)
            return;

        _changed();
    }
}
