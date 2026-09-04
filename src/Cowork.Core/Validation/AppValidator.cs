using Cowork.Core.Models;

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
            issues.Add(new ValidationIssue(nameof(app.Name), "Tên app không được để trống.", true));

        if (string.IsNullOrWhiteSpace(app.ExecutablePath))
        {
            issues.Add(new ValidationIssue(nameof(app.ExecutablePath), "Chưa chọn chương trình để chạy.", true));
        }
        else
        {
            var exe = Environment.ExpandEnvironmentVariables(app.ExecutablePath);
            if (Path.IsPathRooted(exe) && !File.Exists(exe))
            {
                issues.Add(new ValidationIssue(nameof(app.ExecutablePath),
                    "Không tìm thấy file tại đường dẫn này.", false));
            }
        }

        if (!string.IsNullOrWhiteSpace(app.WorkingDirectory))
        {
            var dir = Environment.ExpandEnvironmentVariables(app.WorkingDirectory);
            if (!Directory.Exists(dir))
                issues.Add(new ValidationIssue(nameof(app.WorkingDirectory), "Thư mục làm việc không tồn tại.", false));
        }

        if (app.TimeoutMinutes < 0)
            issues.Add(new ValidationIssue(nameof(app.TimeoutMinutes), "Thời gian tối đa không được âm.", true));

        if (app.RunAsAdministrator && app.CaptureOutput)
        {
            issues.Add(new ValidationIssue(nameof(app.CaptureOutput),
                "Chạy quyền admin thì không thu được output; mục Nhật ký sẽ trống.", false));
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
                    "Lịch theo giờ cần ít nhất một mốc giờ.", true));
                break;

            case ScheduleKind.Interval when schedule.Interval < TimeSpan.FromMinutes(1):
                issues.Add(new ValidationIssue(nameof(schedule.Interval),
                    "Chu kỳ lặp tối thiểu là 1 phút.", true));
                break;

            case ScheduleKind.Interval
                when schedule.WindowStart is { } start && schedule.WindowEnd is { } end && start >= end:
                issues.Add(new ValidationIssue(nameof(schedule.WindowStart),
                    "Giờ bắt đầu phải sớm hơn giờ kết thúc.", true));
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
                issues.Add(new ValidationIssue(nameof(config.Path), "Có file cấu hình chưa nhập đường dẫn.", true));
                continue;
            }

            var full = config.ResolveFullPath(workingDirectory);
            if (!File.Exists(full))
            {
                issues.Add(new ValidationIssue(nameof(config.Path),
                    $"Không tìm thấy file cấu hình: {config.ResolveDisplayName()}", false));
            }
        }
    }

    public static bool HasErrors(IEnumerable<ValidationIssue> issues) => issues.Any(i => i.IsError);
}
