using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Lý do không thử lại. <see cref="None"/> nghĩa là sẽ thử lại.</summary>
public enum RetryRefusal
{
    None = 0,

    /// <summary>App không đặt số lần thử lại.</summary>
    NotConfigured,

    /// <summary>App bật "giữ luôn chạy" — keep-alive đã lo khởi động lại, không chồng thêm.</summary>
    KeepAliveOwnsRestarts,

    /// <summary>App đã bị tắt hoặc chưa có đường dẫn chương trình.</summary>
    CannotRun,

    /// <summary>Kết thúc bình thường, bị dừng tay, hoặc chưa hề chạy được — không phải thứ thử lại được.</summary>
    NotAFailure,

    /// <summary>Đã dùng hết số lần thử lại cho chuỗi này.</summary>
    LimitReached,
}

public sealed record RetryDecision(bool ShouldRetry, TimeSpan Delay, RetryRefusal Refusal)
{
    public static RetryDecision Refuse(RetryRefusal refusal) => new(false, TimeSpan.Zero, refusal);
}

/// <summary>
/// Quyết định "có chạy lại job vừa lỗi không" dưới dạng hàm thuần. Khác keep-alive (dành cho
/// dịch vụ phải luôn sống), thử lại dành cho job chạy xong là thoát: sao lưu, báo cáo — lỗi vì
/// mạng chập chờn thì chạy lại vài lần rồi mới bỏ cuộc.
/// </summary>
public static class RetryPolicy
{
    /// <summary>
    /// Kết quả nào thì đáng thử lại: lỗi, quá giờ, và treo (bị dừng vì kiểm tra sức khoẻ thất bại).
    /// Dừng tay là ý người dùng; chưa chạy được thì chạy lại cũng thế.
    /// </summary>
    public static bool IsRetryable(RunOutcome outcome)
        => outcome is RunOutcome.Failed or RunOutcome.TimedOut or RunOutcome.Unhealthy;

    /// <summary>App có thuộc phạm vi thử lại không, bất kể lần chạy vừa rồi ra sao.</summary>
    public static bool AppliesTo(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.RetryCount > 0 && !app.KeepAlive && app.CanRun;
    }

    /// <param name="app">App vừa kết thúc, ở trạng thái hiện tại.</param>
    /// <param name="record">Bản ghi của lần chạy vừa kết thúc.</param>
    /// <param name="attemptsSoFar">Số lần đã thử lại trong chuỗi hiện tại (0 = lần chạy gốc vừa lỗi).</param>
    public static RetryDecision Decide(ManagedApp app, AppRunRecord record, int attemptsSoFar)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(record);

        if (app.RetryCount <= 0)
            return RetryDecision.Refuse(RetryRefusal.NotConfigured);

        if (app.KeepAlive)
            return RetryDecision.Refuse(RetryRefusal.KeepAliveOwnsRestarts);

        if (!app.CanRun)
            return RetryDecision.Refuse(RetryRefusal.CannotRun);

        if (!IsRetryable(record.Outcome))
            return RetryDecision.Refuse(RetryRefusal.NotAFailure);

        if (attemptsSoFar >= app.RetryCount)
            return RetryDecision.Refuse(RetryRefusal.LimitReached);

        return new RetryDecision(true, TimeSpan.FromSeconds(Math.Max(0, app.RetryDelaySeconds)), RetryRefusal.None);
    }
}
