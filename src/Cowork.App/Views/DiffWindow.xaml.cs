using System.Windows;
using Cowork.App.ViewModels;

namespace Cowork.App.Views;

public partial class DiffWindow : Window
{
    public DiffWindow(DiffViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Đóng ngay sau khi khôi phục: nội dung vừa đổi nên bảng so sánh đang hiện đã cũ.</summary>
    private void OnRestoreClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
