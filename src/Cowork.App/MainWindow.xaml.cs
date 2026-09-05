using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cowork.App.ViewModels;
using Cowork.Core.Localization;
using Hardcodet.Wpf.TaskbarNotification;

namespace Cowork.App;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Nhật ký tự cuộn xuống dòng mới nhất.
        if (OutputList.Items is INotifyCollectionChanged items)
            items.CollectionChanged += OnOutputCollectionChanged;

        if (ViewModel is { } viewModel)
            viewModel.NotificationRaised += OnNotificationRaised;

        TrayIcon.TrayBalloonTipClicked += (_, _) => RestoreWindow();
    }

    /// <summary>Thông báo bong bóng ở khay: thứ duy nhất còn thấy được khi Cowork đang thu nhỏ.</summary>
    private void OnNotificationRaised(object? sender, UserNotification notification)
    {
        var icon = notification.Severity switch
        {
            NotificationSeverity.Error => BalloonIcon.Error,
            NotificationSeverity.Warning => BalloonIcon.Warning,
            _ => BalloonIcon.Info,
        };

        TrayIcon.ShowBalloonTip(notification.Title, notification.Message, icon);
    }

    private void OnOutputCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || OutputList.Items.Count == 0)
            return;

        OutputList.ScrollIntoView(OutputList.Items[OutputList.Items.Count - 1]);
    }

    /// <summary>Mở thẳng xuống khay khi Windows tự khởi động Cowork.</summary>
    public void StartHidden()
    {
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
        Hide();
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && ViewModel?.MinimizeToTray == true)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        if (_forceClose || ViewModel is not { } viewModel)
            return;

        // Bấm X mà đang bật thu nhỏ xuống khay: giấu cửa sổ, giữ scheduler chạy tiếp.
        if (viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            return;
        }

        if (!ConfirmExitWhileRunning(viewModel))
            e.Cancel = true;
    }

    private bool ConfirmExitWhileRunning(MainViewModel viewModel)
    {
        if (!viewModel.AnyRunning)
            return true;

        var answer = MessageBox.Show(
            Loc.T("Msg.ExitWhileRunning"),
            Loc.T("Common.AppName"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

        return answer == MessageBoxResult.Yes;
    }

    private void RestoreWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e) => RestoreWindow();

    private void OnTrayOpenClick(object sender, RoutedEventArgs e) => RestoreWindow();

    private void OnTrayExitClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && !ConfirmExitWhileRunning(viewModel))
            return;

        _forceClose = true;
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    // Hai handler dưới đây thay cho binding hai chiều: RadioButton chỉ báo được
    // lúc nó ĐƯỢC chọn, nên ta đặt cờ tương ứng trên view-model.
    private void OnTableModeChecked(object sender, RoutedEventArgs e)
        => SetRawMode(sender, isRaw: false);

    private void OnRawModeChecked(object sender, RoutedEventArgs e)
        => SetRawMode(sender, isRaw: true);

    private static void SetRawMode(object sender, bool isRaw)
    {
        if (sender is FrameworkElement { DataContext: ConfigFileViewModel config })
            config.IsRawMode = isRaw;
    }
}
