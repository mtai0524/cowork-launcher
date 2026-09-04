using CommunityToolkit.Mvvm.ComponentModel;
using Cowork.Core.Configuration;

namespace Cowork.App.ViewModels;

/// <summary>Một dòng khoá-giá trị trong bảng cấu hình, theo dõi được đã sửa hay chưa.</summary>
public sealed partial class ConfigEntryViewModel : ObservableObject
{
    private readonly string _originalValue;

    public ConfigEntryViewModel(ConfigEntry entry)
    {
        Entry = entry;
        _originalValue = entry.Value;
        _value = entry.Value;
    }

    public ConfigEntry Entry { get; }

    public string Path => Entry.Path;
    public string Key => Entry.Key;
    public string Section => Entry.Section;
    public string? Comment => Entry.Comment;
    public ConfigValueKind Kind => Entry.Kind;

    public string KindLabel => Entry.Kind switch
    {
        ConfigValueKind.Number => "số",
        ConfigValueKind.Boolean => "bool",
        ConfigValueKind.Null => "null",
        _ => "chuỗi",
    };

    [ObservableProperty]
    private string _value;

    public bool IsDirty => !string.Equals(Value, _originalValue, StringComparison.Ordinal);

    /// <summary>Cảnh báo khi giá trị nhập vào không khớp kiểu gốc của khoá.</summary>
    public string? TypeWarning => Entry.Kind switch
    {
        ConfigValueKind.Number when !decimal.TryParse(Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _) => "Giá trị phải là số.",
        ConfigValueKind.Boolean when !bool.TryParse(Value, out _) => "Giá trị phải là true hoặc false.",
        _ => null,
    };

    public bool HasTypeWarning => TypeWarning is not null;

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TypeWarning));
        OnPropertyChanged(nameof(HasTypeWarning));
    }
}
