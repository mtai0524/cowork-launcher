namespace Cowork.App.ViewModels;

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Một thông báo cần đưa ra ngoài cửa sổ chính — hiện ở khay hệ thống, vì lúc app lỗi
/// thì Cowork thường đang thu nhỏ dưới khay chứ không ai nhìn thanh trạng thái.
/// View-model chỉ phát ra; cửa sổ quyết định hiện thế nào.
/// </summary>
public sealed record UserNotification(string Title, string Message, NotificationSeverity Severity);
