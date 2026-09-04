using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

public class ScheduleRuleTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(new DateTime(year, month, day, hour, minute, 0), TimeSpan.FromHours(7));

    [Fact]
    public void DailySchedule_ReturnsNextTimeLaterToday()
    {
        var rule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(7, 30, 0), new TimeSpan(18, 0, 0) },
        };

        var next = rule.GetNextOccurrence(At(2026, 9, 4, 9, 0));

        Assert.Equal(At(2026, 9, 4, 18, 0), next);
    }

    [Fact]
    public void DailySchedule_RollsOverToNextDayAfterLastTime()
    {
        var rule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(7, 30, 0) },
        };

        var next = rule.GetNextOccurrence(At(2026, 9, 4, 20, 0));

        Assert.Equal(At(2026, 9, 5, 7, 30), next);
    }

    [Fact]
    public void DailySchedule_SkipsDaysNotSelected()
    {
        // 2026-09-04 là thứ Sáu; chỉ cho phép thứ Hai.
        var rule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(8, 0, 0) },
            DaysOfWeek = { DayOfWeek.Monday },
        };

        var next = rule.GetNextOccurrence(At(2026, 9, 4, 9, 0));

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Monday, next!.Value.DayOfWeek);
        Assert.Equal(At(2026, 9, 7, 8, 0), next.Value);
    }

    [Fact]
    public void PreviousOccurrence_FindsMostRecentPastTime()
    {
        var rule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(7, 0, 0), new TimeSpan(12, 0, 0), new TimeSpan(19, 0, 0) },
        };

        var previous = rule.GetPreviousOccurrence(At(2026, 9, 4, 13, 15));

        Assert.Equal(At(2026, 9, 4, 12, 0), previous);
    }

    [Fact]
    public void IntervalSchedule_RespectsTimeWindow()
    {
        var rule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.Interval,
            Interval = TimeSpan.FromHours(2),
            WindowStart = new TimeSpan(8, 0, 0),
            WindowEnd = new TimeSpan(17, 0, 0),
        };

        // 16:00 + 2 giờ = 18:00, vượt cửa sổ nên phải nhảy sang 08:00 hôm sau.
        var next = rule.GetNextOccurrence(At(2026, 9, 4, 16, 0));

        Assert.Equal(At(2026, 9, 5, 8, 0), next);
    }

    [Fact]
    public void ManualSchedule_HasNoNextOccurrence()
    {
        var rule = new ScheduleRule { Enabled = true, Kind = ScheduleKind.Manual };

        Assert.Null(rule.GetNextOccurrence(At(2026, 9, 4, 10, 0)));
    }

    [Fact]
    public void DisabledSchedule_HasNoNextOccurrence()
    {
        var rule = new ScheduleRule
        {
            Enabled = false,
            Kind = ScheduleKind.DailyAtTimes,
            Times = { new TimeSpan(7, 0, 0) },
        };

        Assert.Null(rule.GetNextOccurrence(At(2026, 9, 4, 5, 0)));
    }
}

public class ScheduleEvaluatorTests
{
    private static DateTimeOffset At(int day, int hour, int minute)
        => new(new DateTime(2026, 9, day, hour, minute, 0), TimeSpan.FromHours(7));

    private static ManagedApp DailyApp(params TimeSpan[] times) => new()
    {
        Name = "Test",
        ExecutablePath = @"C:\Windows\System32\cmd.exe",
        Schedule = new ScheduleRule
        {
            Enabled = true,
            Kind = ScheduleKind.DailyAtTimes,
            Times = times.ToList(),
        },
    };

    [Fact]
    public void IsDue_WhenTimeJustPassedAndNeverRun()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));

        Assert.True(ScheduleEvaluator.IsDue(app, At(4, 7, 31)));
    }

    [Fact]
    public void IsNotDue_WhenAlreadyRanThisOccurrence()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));
        app.LastScheduledRunAt = At(4, 7, 30);

        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 7, 35)));
    }

    [Fact]
    public void IsDue_ForNextOccurrenceAfterEarlierRunSameDay()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0), new TimeSpan(13, 0, 0));
        app.LastScheduledRunAt = At(4, 7, 30);

        Assert.True(ScheduleEvaluator.IsDue(app, At(4, 13, 1)));
    }

    [Fact]
    public void IsNotDue_WhenOccurrenceMissedBeyondGraceAndCatchUpOff()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));

        // Mở Cowork lúc 11h, mốc 7:30 đã trôi qua quá lâu.
        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 11, 0)));
    }

    [Fact]
    public void IsDue_WhenOccurrenceMissedButCatchUpOn()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));
        app.Schedule.CatchUpMissedRun = true;

        Assert.True(ScheduleEvaluator.IsDue(app, At(4, 11, 0)));
    }

    [Fact]
    public void IsNotDue_WhenAppDisabled()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));
        app.Enabled = false;

        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 7, 31)));
    }

    [Fact]
    public void IsNotDue_WhenExecutablePathMissing()
    {
        var app = DailyApp(new TimeSpan(7, 30, 0));
        app.ExecutablePath = string.Empty;

        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 7, 31)));
    }

    [Fact]
    public void IntervalApp_IsDueOnFirstCheckInsideWindow()
    {
        var app = new ManagedApp
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Schedule = new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleKind.Interval,
                Interval = TimeSpan.FromMinutes(30),
                WindowStart = new TimeSpan(8, 0, 0),
                WindowEnd = new TimeSpan(17, 0, 0),
            },
        };

        Assert.True(ScheduleEvaluator.IsDue(app, At(4, 9, 0)));
        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 7, 0)));
    }

    [Fact]
    public void IntervalApp_WaitsFullIntervalAfterPreviousRun()
    {
        var app = new ManagedApp
        {
            ExecutablePath = @"C:\Windows\System32\cmd.exe",
            Schedule = new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleKind.Interval,
                Interval = TimeSpan.FromMinutes(30),
            },
            LastScheduledRunAt = At(4, 9, 0),
        };

        Assert.False(ScheduleEvaluator.IsDue(app, At(4, 9, 20)));
        Assert.True(ScheduleEvaluator.IsDue(app, At(4, 9, 30)));
    }
}
