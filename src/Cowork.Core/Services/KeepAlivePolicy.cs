using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Lý do không khởi động lại. <see cref="None"/> nghĩa là sẽ khởi động lại.</summary>
public enum RestartRefusal
{
    None = 0,

    /// <summary>App không bật "giữ luôn chạy".</summary>
    NotKeepAlive,

    /// <summary>App đã bị tắt hoặc chưa có đường dẫn chương trình.</summary>
    CannotRun,

    /// <summary>Người dùng bấm Dừng — dừng tay là ý người dùng, không cãi.</summary>
    StoppedByUser,

    /// <summary>Tiến trình chưa hề chạy được (thiếu file, bị từ chối quyền…); chạy lại cũng thế.</summary>
    NeverStarted,

    /// <summary>Đã chạm trần số lần khởi động lại trong cửa sổ thời gian.</summary>
    LimitReached,
}

public sealed record RestartDecision(bool ShouldRestart, TimeSpan Delay, RestartRefusal Refusal)
{
    public static RestartDecision Refuse(RestartRefusal refusal) => new(false, TimeSpan.Zero, refusal);
}

/// <summary>
/// Quyết định "có khởi động lại app vừa thoát không" dưới dạng hàm thuần, nhận <c>now</c>
/// làm tham số để kiểm thử được cửa sổ đếm mà không cần chờ một giờ thật.
/// </summary>
public static class KeepAlivePolicy
{
    /// <summary>Cửa sổ đếm số lần khởi động lại cho <see cref="ManagedApp.MaxRestartsPerHour"/>.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <param name="app">App vừa kết thúc, ở trạng thái hiện tại (người dùng có thể vừa tắt keep-alive).</param>
    /// <param name="record">Bản ghi của lần chạy vừa kết thúc.</param>
    /// <param name="recentRestarts">Các mốc Cowork đã tự khởi động lại app này trước đó.</param>
    /// <param name="now">Thời điểm xét.</param>
    public static RestartDecision Decide(
        ManagedApp app,
        AppRunRecord record,
        IReadOnlyCollection<DateTimeOffset> recentRestarts,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(recentRestarts);

        if (!app.KeepAlive)
            return RestartDecision.Refuse(RestartRefusal.NotKeepAlive);

        if (!app.CanRun)
            return RestartDecision.Refuse(RestartRefusal.CannotRun);

        switch (record.Outcome)
        {
            case RunOutcome.Cancelled:
                return RestartDecision.Refuse(RestartRefusal.StoppedByUser);

            case RunOutcome.NotStarted:
                return RestartDecision.Refuse(RestartRefusal.NeverStarted);

            case RunOutcome.Running:
                // Chưa kết thúc thì không có gì để khởi động lại.
                return RestartDecision.Refuse(RestartRefusal.NotKeepAlive);
        }

        // Thoát êm (mã 0), lỗi, hay bị dừng vì quá giờ đều tính là "app không còn chạy" —
        // với keep-alive thì chỉ mỗi chuyện đó quan trọng. Timeout + keep-alive vì thế
        // trở thành cách tự khởi động lại định kỳ.
        if (app.MaxRestartsPerHour > 0 && CountWithinWindow(recentRestarts, now) >= app.MaxRestartsPerHour)
            return RestartDecision.Refuse(RestartRefusal.LimitReached);

        return new RestartDecision(true, TimeSpan.FromSeconds(Math.Max(0, app.RestartDelaySeconds)), RestartRefusal.None);
    }

    /// <summary>Số lần khởi động lại còn nằm trong cửa sổ đếm tính tới <paramref name="now"/>.</summary>
    public static int CountWithinWindow(IEnumerable<DateTimeOffset> restarts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(restarts);
        return restarts.Count(at => now - at < Window);
    }
}
