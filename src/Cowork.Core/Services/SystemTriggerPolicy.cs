using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Lý do không chạy theo sự kiện hệ thống. <see cref="None"/> nghĩa là sẽ chạy.</summary>
public enum SystemTriggerRefusal
{
    None = 0,

    /// <summary>App không đăng ký sự kiện này.</summary>
    NotSubscribed,

    /// <summary>App đã bị tắt hoặc chưa có đường dẫn chương trình.</summary>
    CannotRun,

    /// <summary>Vừa chạy vì đúng sự kiện này cách đây chưa lâu.</summary>
    TooSoon,

    /// <summary>App đang chạy và có bật "không chạy chồng".</summary>
    AlreadyRunning,
}

public sealed record SystemTriggerDecision(bool ShouldRun, TimeSpan Delay, SystemTriggerRefusal Refusal)
{
    public static SystemTriggerDecision Refuse(SystemTriggerRefusal refusal)
        => new(false, TimeSpan.Zero, refusal);
}

/// <summary>
/// Quyết định "sự kiện này có làm app chạy không", hàm thuần nhận <c>now</c>.
///
/// Điểm mấu chốt là <see cref="Cooldown"/>: Windows bắn <c>Resume</c> nhiều lần cho một lần thức
/// dậy, và mạng chập chờn thì <c>NetworkAvailable</c> nảy liên tục. Không chặn thì một lần mở nắp
/// máy có thể chạy cùng một job dăm lần.
/// </summary>
public static class SystemTriggerPolicy
{
    /// <summary>Khoảng lặng sau mỗi lần chạy vì một sự kiện, tính riêng cho từng cặp app–sự kiện.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    /// <summary>App có đăng ký sự kiện hệ thống nào không.</summary>
    public static bool AppliesTo(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.SystemTriggers.Count > 0;
    }

    /// <param name="app">App ở trạng thái hiện tại.</param>
    /// <param name="kind">Sự kiện vừa xảy ra.</param>
    /// <param name="lastTriggeredAt">Lần cuối chính app này chạy vì chính sự kiện này; null nếu chưa lần nào.</param>
    /// <param name="isRunning">App đang có tiến trình sống do Cowork khởi chạy.</param>
    /// <param name="now">Thời điểm xét.</param>
    public static SystemTriggerDecision Decide(
        ManagedApp app,
        SystemEventKind kind,
        DateTimeOffset? lastTriggeredAt,
        bool isRunning,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.SystemTriggers.Contains(kind))
            return SystemTriggerDecision.Refuse(SystemTriggerRefusal.NotSubscribed);

        if (!app.CanRun)
            return SystemTriggerDecision.Refuse(SystemTriggerRefusal.CannotRun);

        // Để ProcessManager từ chối thì lịch sử ghi thêm một lần "không chạy được" vô nghĩa.
        if (isRunning && app.SingleInstance)
            return SystemTriggerDecision.Refuse(SystemTriggerRefusal.AlreadyRunning);

        if (lastTriggeredAt is { } last && now - last < Cooldown)
            return SystemTriggerDecision.Refuse(SystemTriggerRefusal.TooSoon);

        return new SystemTriggerDecision(
            true, TimeSpan.FromSeconds(Math.Max(0, app.SystemTriggerDelaySeconds)), SystemTriggerRefusal.None);
    }
}
