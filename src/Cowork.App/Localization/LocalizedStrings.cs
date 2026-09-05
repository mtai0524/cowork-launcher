using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using Cowork.Core.Localization;

namespace Cowork.App.Localization;

/// <summary>
/// Nguồn binding cho các nhãn tĩnh trên XAML.
///
/// XAML không nói chuyện được với hàm tĩnh <see cref="Loc.T(string)"/> theo kiểu
/// làm tươi được, nên ta bọc nó thành một chỉ mục có <see cref="INotifyPropertyChanged"/>:
/// đổi ngôn ngữ chỉ cần bắn một sự kiện là mọi nhãn tự đọc lại.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public static LocalizedStrings Instance { get; } = new();

    private LocalizedStrings()
    {
    }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Báo cho mọi binding rằng toàn bộ chỉ mục đã đổi.</summary>
    public void Refresh()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
}

/// <summary>
/// Cú pháp gọn cho nhãn đa ngôn ngữ: <c>Text="{loc:Tr AppList.Title}"</c>.
/// Trả về một binding thật (không phải chuỗi cố định) để nhãn đổi theo ngôn ngữ ngay lúc chạy.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizedStrings.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
