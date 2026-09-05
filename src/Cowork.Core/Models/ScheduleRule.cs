using System.Text.Json.Serialization;
using Cowork.Core.Localization;

namespace Cowork.Core.Models;

/// <summary>Kiểu lịch chạy của một app.</summary>
public enum ScheduleKind
{
    /// <summary>Chỉ chạy khi người dùng bấm nút.</summary>
    Manual = 0,

    /// <summary>Chạy vào các mốc giờ cố định trong ngày.</summary>
    DailyAtTimes = 1,

    /// <summary>Chạy lặp lại sau mỗi khoảng thời gian.</summary>
    Interval = 2,

    /// <summary>Chạy một lần ngay khi Cowork khởi động.</summary>
    OnCoworkStartup = 3,
}

/// <summary>
/// Quy tắc lịch chạy. Được thiết kế thuần dữ liệu + hàm thuần
/// (<see cref="GetNextOccurrence"/>) để có thể unit-test không cần đồng hồ thật.
/// </summary>
public sealed class ScheduleRule
{
    /// <summary>Bật/tắt lịch mà không cần xoá cấu hình.</summary>
    public bool Enabled { get; set; }

    public ScheduleKind Kind { get; set; } = ScheduleKind.Manual;

    /// <summary>Các mốc giờ trong ngày, dùng cho <see cref="ScheduleKind.DailyAtTimes"/>.</summary>
    public List<TimeSpan> Times { get; set; } = new();

    /// <summary>Chu kỳ lặp, dùng cho <see cref="ScheduleKind.Interval"/>.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Các ngày trong tuần được phép chạy. Rỗng = mọi ngày.</summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>Giờ bắt đầu cửa sổ chạy (áp dụng cho Interval). Null = không giới hạn.</summary>
    public TimeSpan? WindowStart { get; set; }

    /// <summary>Giờ kết thúc cửa sổ chạy (áp dụng cho Interval). Null = không giới hạn.</summary>
    public TimeSpan? WindowEnd { get; set; }

    /// <summary>
    /// Nếu Cowork đang tắt lúc tới hạn thì có chạy bù khi mở lại hay không.
    /// </summary>
    public bool CatchUpMissedRun { get; set; }

    [JsonIgnore]
    public bool IsAutomatic => Enabled && Kind != ScheduleKind.Manual;

    public bool AllowsDay(DayOfWeek day) => DaysOfWeek.Count == 0 || DaysOfWeek.Contains(day);

    /// <summary>
    /// Tính thời điểm chạy kế tiếp tính từ <paramref name="after"/> (không bao gồm chính nó).
    /// Trả về null nếu lịch không tự động hoặc không xác định được mốc kế tiếp.
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        if (!IsAutomatic)
            return null;

        return Kind switch
        {
            ScheduleKind.DailyAtTimes => NextDailyOccurrence(after),
            ScheduleKind.Interval => NextIntervalOccurrence(after),
            ScheduleKind.OnCoworkStartup => null,
            _ => null,
        };
    }

    /// <summary>
    /// Mốc chạy gần nhất đã qua tính tới <paramref name="now"/> (bao gồm cả chính nó).
    /// Chỉ có ý nghĩa với <see cref="ScheduleKind.DailyAtTimes"/>.
    /// </summary>
    public DateTimeOffset? GetPreviousOccurrence(DateTimeOffset now)
    {
        if (Kind != ScheduleKind.DailyAtTimes || Times.Count == 0)
            return null;

        var ordered = Times.Where(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                           .OrderByDescending(t => t)
                           .ToList();
        if (ordered.Count == 0)
            return null;

        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var day = now.Date.AddDays(-dayOffset);
            if (!AllowsDay(day.DayOfWeek))
                continue;

            foreach (var time in ordered)
            {
                var candidate = new DateTimeOffset(day + time, now.Offset);
                if (candidate <= now)
                    return candidate;
            }
        }

        return null;
    }

    private DateTimeOffset? NextDailyOccurrence(DateTimeOffset after)
    {
        if (Times.Count == 0)
            return null;

        var ordered = Times.Where(t => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
                           .OrderBy(t => t)
                           .ToList();
        if (ordered.Count == 0)
            return null;

        // Quét tối đa 8 ngày để phủ hết mọi cấu hình ngày-trong-tuần.
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var day = after.Date.AddDays(dayOffset);
            if (!AllowsDay(day.DayOfWeek))
                continue;

            foreach (var time in ordered)
            {
                var candidate = new DateTimeOffset(day + time, after.Offset);
                if (candidate > after)
                    return candidate;
            }
        }

        return null;
    }

    private DateTimeOffset? NextIntervalOccurrence(DateTimeOffset after)
    {
        if (Interval <= TimeSpan.Zero)
            return null;

        var candidate = after + Interval;

        // Đẩy tới thời điểm hợp lệ đầu tiên (đúng ngày, nằm trong cửa sổ giờ).
        for (var guard = 0; guard < 64; guard++)
        {
            if (!AllowsDay(candidate.DayOfWeek))
            {
                candidate = new DateTimeOffset(candidate.Date.AddDays(1) + (WindowStart ?? TimeSpan.Zero), candidate.Offset);
                continue;
            }

            var timeOfDay = candidate.TimeOfDay;

            if (WindowStart.HasValue && timeOfDay < WindowStart.Value)
            {
                candidate = new DateTimeOffset(candidate.Date + WindowStart.Value, candidate.Offset);
                continue;
            }

            if (WindowEnd.HasValue && timeOfDay > WindowEnd.Value)
            {
                candidate = new DateTimeOffset(candidate.Date.AddDays(1) + (WindowStart ?? TimeSpan.Zero), candidate.Offset);
                continue;
            }

            return candidate;
        }

        return null;
    }

    public ScheduleRule Clone() => new()
    {
        Enabled = Enabled,
        Kind = Kind,
        Times = new List<TimeSpan>(Times),
        Interval = Interval,
        DaysOfWeek = new List<DayOfWeek>(DaysOfWeek),
        WindowStart = WindowStart,
        WindowEnd = WindowEnd,
        CatchUpMissedRun = CatchUpMissedRun,
    };

    public string Describe()
    {
        if (!Enabled)
            return Loc.T("Sched.Off");

        var days = DaysOfWeek.Count == 0
            ? Loc.T("Sched.EveryDay")
            : string.Join(", ", DaysOfWeek.OrderBy(d => (int)d).Select(Loc.DayName));

        return Kind switch
        {
            ScheduleKind.Manual => Loc.T("Sched.Manual"),
            ScheduleKind.DailyAtTimes when Times.Count > 0 => Loc.T("Sched.Daily",
                string.Join(", ", Times.OrderBy(t => t).Select(t => t.ToString(@"hh\:mm"))), days),
            ScheduleKind.DailyAtTimes => Loc.T("Sched.NoTimes"),
            ScheduleKind.Interval => Loc.T("Sched.Interval", FormatInterval(Interval), days),
            ScheduleKind.OnCoworkStartup => Loc.T("Sched.OnStartup"),
            _ => Loc.T("Sched.Unknown"),
        };
    }

    private static string FormatInterval(TimeSpan value)
    {
        if (value.TotalMinutes < 60)
            return Loc.T("Sched.IntervalMinutes", (int)value.TotalMinutes);

        return value.Minutes == 0
            ? Loc.T("Sched.IntervalHours", (int)value.TotalHours)
            : Loc.T("Sched.IntervalHoursMinutes", (int)value.TotalHours, value.Minutes.ToString("00"));
    }
}
