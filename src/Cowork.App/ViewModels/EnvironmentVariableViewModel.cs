using CommunityToolkit.Mvvm.ComponentModel;
using Cowork.Core.Localization;

namespace Cowork.App.ViewModels;

/// <summary>Một cặp biến môi trường trong bảng cấu hình app.</summary>
public sealed partial class EnvironmentVariableViewModel : ObservableObject
{
    public EnvironmentVariableViewModel(string name, string value)
    {
        _name = name;
        _value = value;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _value;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name);
}

/// <summary>Ô chọn một ngày trong tuần cho lịch chạy.</summary>
public sealed partial class DayToggleViewModel : ObservableObject
{
    public DayToggleViewModel(DayOfWeek day, bool isSelected)
    {
        Day = day;
        _isSelected = isSelected;
    }

    public DayOfWeek Day { get; }

    public string Label => Loc.DayName(Day);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));

    [ObservableProperty]
    private bool _isSelected;
}
