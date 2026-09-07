using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Quyết định "cảnh báo này có gửi ra ngoài không", hàm thuần nhận <c>now</c>.
///
/// Lý do tồn tại: một app hỏng hẳn có thể lỗi liên tục, và một lượt "Chạy tất cả" hỏng có thể sinh
/// một loạt cảnh báo cùng lúc. Gửi hết thì điện thoại rung cả đêm cho đúng một sự việc.
/// </summary>
public static class AlertPolicy
{
    /// <summary>Có kênh nào đủ thông tin để gửi không.</summary>
    public static bool AnyChannelConfigured(NotificationSettings settings, IEnumerable<IAlertChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(channels);

        return channels.Any(c => c.IsConfigured(settings));
    }

    /// <summary>
    /// Cảnh báo trùng khoá với một cảnh báo vừa gửi, trong khoảng lặng, thì bỏ.
    /// </summary>
    /// <param name="lastSentAt">Lần cuối gửi cảnh báo cùng khoá; null nếu chưa lần nào.</param>
    public static bool ShouldSend(
        NotificationSettings settings, DateTimeOffset? lastSentAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
            return false;

        if (settings.DedupeMinutes <= 0 || lastSentAt is not { } last)
            return true;

        return now - last >= TimeSpan.FromMinutes(settings.DedupeMinutes);
    }
}
