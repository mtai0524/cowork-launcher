using System.IO;
using System.Windows;
using Cowork.App.ViewModels;
using Cowork.Core.Localization;

namespace Cowork.App.Views;

public partial class ScanProgramWindow : Window
{
    public ScanProgramWindow(ScanProgramViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;

        Loaded += (_, _) => viewModel.ScanCommand.Execute(null);
    }

    public ScanProgramViewModel ViewModel { get; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("Dialog.PickScanFolder"),
            InitialDirectory = Directory.Exists(ViewModel.RootDirectory) ? ViewModel.RootDirectory : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.RootDirectory = dialog.FolderName;
            ViewModel.ScanCommand.Execute(null);
        }
    }

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedCandidate is not null)
            DialogResult = true;
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedCandidate is null)
            return;

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
