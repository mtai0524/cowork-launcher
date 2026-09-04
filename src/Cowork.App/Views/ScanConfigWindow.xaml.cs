using System.IO;
using System.Windows;
using Cowork.App.ViewModels;

namespace Cowork.App.Views;

public partial class ScanConfigWindow : Window
{
    public ScanConfigWindow(ScanConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;

        // Quét ngay khi mở để người dùng thấy kết quả mà không phải bấm thêm.
        Loaded += (_, _) => viewModel.ScanCommand.Execute(null);
    }

    public ScanConfigViewModel ViewModel { get; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Chọn thư mục cần quét",
            InitialDirectory = Directory.Exists(ViewModel.RootDirectory) ? ViewModel.RootDirectory : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.RootDirectory = dialog.FolderName;
            ViewModel.ScanCommand.Execute(null);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCandidates.Count == 0)
        {
            MessageBox.Show(this, "Chưa tick file nào.", "Cowork",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
