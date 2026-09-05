using System.Windows;
using System.Windows.Threading;
using Cowork.App.ViewModels;
using Cowork.Core.Configuration;
using Cowork.Core.Services;
using Cowork.Core.Localization;
using Cowork.App.Themes;

namespace Cowork.App;

/// <summary>
/// Điểm khởi động và cũng là nơi lắp ráp toàn bộ phụ thuộc (composition root).
/// Dự án chủ ý không dùng DI container để giữ luồng khởi tạo dễ đọc.
/// </summary>
public partial class App : Application
{
    private ProcessManager? _processManager;
    private MainViewModel? _mainViewModel;
    private ICoworkLogger? _logger;

    /// <summary>Khởi động ở chế độ thu nhỏ khi Windows tự mở Cowork lúc đăng nhập.</summary>
    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartMinimized = e.Args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)
            || a.Equals("/minimized", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        var paths = new CoworkPaths();
        _logger = new FileLogger(paths);
        _logger.Info("Cowork khởi động.");

        var workspaceStore = new JsonWorkspaceStore(paths, _logger);
        var settings = workspaceStore.Load().Settings;

        // Ngôn ngữ và chủ đề phải được đặt trước khi dựng cửa sổ đầu tiên, nếu không
        // người dùng sẽ thấy giao diện nháy một nhịp từ mặc định sang thiết lập của họ.
        Loc.Current = settings.Language;
        ThemeManager.Apply(settings.Theme);

        _processManager = new ProcessManager(_logger, paths, settings.OutputBufferLines);
        var history = new JsonRunHistoryStore(paths, _logger);
        var configService = new ConfigFileService();

        _mainViewModel = new MainViewModel(
            workspaceStore, _processManager, history, configService, _logger, paths, Dispatcher);

        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;

        if (StartMinimized && settings.MinimizeToTray)
            window.StartHidden();
        else
            window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Cowork thoát.");

        _mainViewModel?.SaveQuietly();
        _mainViewModel?.Dispose();
        _processManager?.Dispose();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Lỗi không bắt được ở tầng giao diện.", e.Exception);

        MessageBox.Show(
            Loc.T("Msg.UnhandledError", e.Exception.Message),
            Loc.T("Common.AppName"), MessageBoxButton.OK, MessageBoxImage.Error);

        // Đánh dấu đã xử lý để một lỗi trên UI không làm sập cả app đang quản lý tiến trình.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => _logger?.Error("Lỗi không bắt được ở tầng nền.", e.ExceptionObject as Exception);
}
