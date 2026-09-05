using CommunityToolkit.Mvvm.ComponentModel;

namespace Cowork.App.ViewModels;

/// <summary>
/// Một lựa chọn trong ComboBox: giữ giá trị gốc và nhãn hiển thị dịch được.
///
/// Không bind thẳng danh sách enum vào ComboBox vì enum không có PropertyChanged,
/// nên đổi ngôn ngữ sẽ không làm nhãn vẽ lại. Thay ItemsSource để ép vẽ lại thì
/// ComboBox lại xoá mất lựa chọn đang chọn. Bọc thành đối tượng như đây giải quyết cả hai.
/// </summary>
public sealed class ChoiceViewModel<T> : ObservableObject
    where T : notnull
{
    private readonly Func<T, string> _label;

    public ChoiceViewModel(T value, Func<T, string> label)
    {
        Value = value;
        _label = label;
    }

    public T Value { get; }

    public string Label => _label(Value);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}
