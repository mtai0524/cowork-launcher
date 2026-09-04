using System.Diagnostics;
using Microsoft.Win32;
using Cowork.Core.Services;

namespace Cowork.App.ViewModels;

/// <summary>
/// Đăng ký Cowork khởi động cùng Windows qua khoá Run của người dùng hiện tại
/// (không cần quyền admin).
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Cowork";

    public static void Apply(bool enabled, ICoworkLogger logger)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                logger.Warning("Không mở được khoá Run trong registry.");
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    logger.Warning("Không xác định được đường dẫn Cowork.exe để đăng ký khởi động.");
                    return;
                }

                key.SetValue(ValueName, $"\"{exePath}\" --minimized");
                logger.Info("Đã bật khởi động cùng Windows.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                logger.Info("Đã tắt khởi động cùng Windows.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            logger.Error("Không đổi được thiết lập khởi động cùng Windows.", ex);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
