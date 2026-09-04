using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>
/// Toàn bộ logic "app này đã tới giờ chạy chưa" nằm ở đây dưới dạng hàm thuần,
/// tách khỏi bộ đếm thời gian để có thể kiểm thử đầy đủ.
/// </summary>
public static class ScheduleEvaluator
{
    /// <summary>
    /// Dung sai mặc định: nếu bộ đếm tick trễ vài phút so với mốc giờ thì vẫn tính là tới hạn.
    /// Quá dung sai này mà không bật chạy bù thì coi như bỏ lỡ.
    /// </summary>
    public const int DefaultGraceMinutes = 10;

    public static bool IsDue(ManagedApp app, DateTimeOffset now, int graceMinutes = DefaultGraceMinutes)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.CanRun || !app.Schedule.IsAutomatic)
            return false;

        return app.Schedule.Kind switch
        {
            ScheduleKind.DailyAtTimes => IsDailyDue(app, now, graceMinutes),
            ScheduleKind.Interval => IsIntervalDue(app, now),
            _ => false,
        };
    }

    private static bool IsDailyDue(ManagedApp app, DateTimeOffset now, int graceMinutes)
    {
        var occurrence = app.Schedule.GetPreviousOccurrence(now);
        if (occurrence is null)
            return false;

        // Mốc này đã được kích hoạt rồi.
        if (app.LastScheduledRunAt is { } last && last >= occurrence.Value)
            return false;

        if (app.Schedule.CatchUpMissedRun)
            return true;

        return now - occurrence.Value <= TimeSpan.FromMinutes(graceMinutes);
    }

    private static bool IsIntervalDue(ManagedApp app, DateTimeOffset now)
    {
        var schedule = app.Schedule;

        if (schedule.Interval <= TimeSpan.Zero)
            return false;

        if (!schedule.AllowsDay(now.DayOfWeek))
            return false;

        if (!IsInsideWindow(schedule, now.TimeOfDay))
            return false;

        // Chưa từng chạy: kích hoạt ngay lần tick đầu tiên nằm trong cửa sổ.
        if (app.LastScheduledRunAt is not { } last)
            return true;

        return now - last >= schedule.Interval;
    }

    private static bool IsInsideWindow(ScheduleRule schedule, TimeSpan timeOfDay)
    {
        if (schedule.WindowStart is { } start && timeOfDay < start)
            return false;
        if (schedule.WindowEnd is { } end && timeOfDay > end)
            return false;
        return true;
    }

    /// <summary>Mốc chạy tự động kế tiếp, để hiển thị trên dashboard. Null nếu không có.</summary>
    public static DateTimeOffset? NextRun(ManagedApp app, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.CanRun || !app.Schedule.IsAutomatic)
            return null;

        if (app.Schedule.Kind == ScheduleKind.Interval)
        {
            // Neo vào lần chạy trước; nếu chưa từng chạy thì mốc kế tiếp là ngay bây giờ.
            var anchor = app.LastScheduledRunAt ?? now - app.Schedule.Interval;
            return app.Schedule.GetNextOccurrence(anchor);
        }

        return app.Schedule.GetNextOccurrence(now);
    }
}
