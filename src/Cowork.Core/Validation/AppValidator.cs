using Cowork.Core.Models;
using Cowork.Core.Localization;

namespace Cowork.Core.Validation;

public sealed record ValidationIssue(string Field, string Message, bool IsError);

/// <summary>
/// Kiểm tra cấu hình app trước khi lưu. Phân biệt rõ lỗi (chặn lưu)
/// và cảnh báo (vẫn lưu được, chỉ nhắc người dùng).
/// </summary>
public static class AppValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(app.Name))
            issues.Add(new ValidationIssue(nameof(app.Name), Loc.T("Val.NameRequired"), true));

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            issues.Add(new ValidationIssue(nameof(app.ExecutablePath), Loc.T("Val.ExecutableRequired"), true));
        }
        else
        {
            var exe = Environment.ExpandEnvironmentVariables(app.ExecutablePath);
            if (Path.IsPathRooted(exe) && !File.Exists(exe))
            {
                issues.Add(new ValidationIssue(nameof(app.ExecutablePath),
                    Loc.T("Val.ExecutableNotFound"), false));
            }
        }

        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
        {
            var dir = Environment.ExpandEnvironmentVariables(app.WorkingDirectory);
            if (!Directory.Exists(dir))
                issues.Add(new ValidationIssue(nameof(app.WorkingDirectory), Loc.T("Val.WorkingDirMissing"), false));
        }

        if (app.TimeoutMinutes < 0)
            issues.Add(new ValidationIssue(nameof(app.TimeoutMinutes), Loc.T("Val.TimeoutNegative"), true));

        if (app.RestartDelaySeconds < 0)
            issues.Add(new ValidationIssue(nameof(app.RestartDelaySeconds), Loc.T("Val.RestartDelayNegative"), true));

        if (app.MaxRestartsPerHour < 0)
            issues.Add(new ValidationIssue(nameof(app.MaxRestartsPerHour), Loc.T("Val.RestartLimitNegative"), true));

        if (app.RunAsAdministrator && app.CaptureOutput)
        {
            issues.Add(new ValidationIssue(nameof(app.CaptureOutput),
                Loc.T("Val.AdminNoOutput"), false));
        }

        ValidateSchedule(app.Schedule, issues);
        ValidateConfigFiles(app, issues);

        return issues;
    }

    private static void ValidateSchedule(ScheduleRule schedule, List<ValidationIssue> issues)
    {
        if (!schedule.Enabled)
            return;

        switch (schedule.Kind)
        {
            case ScheduleKind.DailyAtTimes when schedule.Times.Count == 0:
                issues.Add(new ValidationIssue(nameof(schedule.Times),
                    Loc.T("Val.TimesRequired"), true));
                break;

            case ScheduleKind.Interval when schedule.Interval < TimeSpan.FromMinutes(1):
                issues.Add(new ValidationIssue(nameof(schedule.Interval),
                    Loc.T("Val.IntervalTooShort"), true));
                break;

            case ScheduleKind.Interval
                when schedule.WindowStart is { } start && schedule.WindowEnd is { } end && start >= end:
                issues.Add(new ValidationIssue(nameof(schedule.WindowStart),
                    Loc.T("Val.WindowOrder"), true));
                break;
        }
    }

    private static void ValidateConfigFiles(ManagedApp app, List<ValidationIssue> issues)
    {
        var workingDirectory = app.ResolveWorkingDirectory();

        foreach (var config in app.ConfigFiles)
        {
            if (string.IsNullOrWhiteSpace(config.Path))
            {
                issues.Add(new ValidationIssue(nameof(config.Path), Loc.T("Val.ConfigPathMissing"), true));
                continue;
            }

            var full = config.ResolveFullPath(workingDirectory);
            if (!File.Exists(full))
            {
                issues.Add(new ValidationIssue(nameof(config.Path),
                    Loc.T("Val.ConfigFileNotFound", config.ResolveDisplayName()), false));
            }
        }
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) => issues.Any(i => i.IsError);
}
