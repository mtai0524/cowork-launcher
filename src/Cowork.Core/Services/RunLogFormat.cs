using System.Globalization;
using System.Text;
using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Dòng trong log đến từ đâu.</summary>
public enum LogSource
{
    StandardOutput = 0,
    StandardError = 1,

    /// <summary>Do chính Cowork ghi — bắt đầu, dừng, quá giờ — chứ không phải app in ra.</summary>
    Cowork = 2,
}

/// <summary>Những gì cần biết về một lần chạy để dựng khối đầu file log.</summary>
public sealed record RunLogContext(
    string AppName,
    Guid RunId,
    RunTrigger Trigger,
    string Program,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> EnvironmentNames,
    IReadOnlyList<int> SuccessExitCodes,
    int ProcessId,
    DateTimeOffset StartedAt);

/// <summary>
/// Định dạng file log của một lần chạy.
///
/// File phải tự nói lên nó là lần chạy nào: mở ra sau ba tuần thì cái cần biết trước tiên là
/// lệnh nào đã chạy, ai kích hoạt, và nó kết thúc ra sao — chứ không phải một đống dòng
/// output trần không rõ của cái gì.
///
/// Nhãn cấu trúc (Program, Started…) cố ý để tiếng Anh và cố định: đây là định dạng file,
/// grep được và không đổi nghĩa khi có người bật/tắt ngôn ngữ giao diện. Riêng câu chữ của
/// các sự kiện thì đi qua bảng chuỗi, giống mọi thông báo khác của agent.
/// </summary>
public static class RunLogFormat
{
    /// <summary>
    /// Có ngày và mili giây, kèm lệch múi giờ. Mốc chỉ có <c>HH:mm:ss</c> là vô dụng khi đối
    /// chiếu với log của một hệ thống khác, hoặc khi lần chạy vắt qua nửa đêm.
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    private const string Rule = "-----------------------------------------------------------------------";

    /// <summary>Rộng bằng nhãn dài nhất để cột nội dung thẳng hàng.</summary>
    private const int TagWidth = 6;

    private const int LabelWidth = 13;

    public static string Tag(LogSource source) => source switch
    {
        LogSource.StandardError => "ERR",
        LogSource.Cowork => "cowork",
        _ => "out",
    };

    public static string Line(AppOutputLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return string.Concat(
            line.Timestamp.ToString(TimestampFormat),
            "  ",
            Tag(line.Source).PadRight(TagWidth),
            "  ",
            line.Text);
    }

    public static IReadOnlyList<string> Header(RunLogContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lines = new List<string>
        {
            Rule,
            Field("App", context.AppName),
            Field("Run id", context.RunId.ToString("N")),
            Field("Trigger", Loc.T("Trigger." + context.Trigger)),
            Field("Program", context.Program),
        };

        if (!string.IsNullOrWhiteSpace(context.Arguments))
            lines.Add(Field("Arguments", context.Arguments));

        lines.Add(Field("Working dir", context.WorkingDirectory));

        // Chỉ tên biến, không bao giờ giá trị: log này mở được từ web, mà biến môi trường
        // riêng của app là chỗ người ta hay để khoá API và mật khẩu.
        if (context.EnvironmentNames.Count > 0)
            lines.Add(Field("Env vars", string.Join(", ", context.EnvironmentNames)));

        if (context.SuccessExitCodes.Count > 0)
            lines.Add(Field("Success codes", string.Join(", ", context.SuccessExitCodes)));

        lines.Add(Field("Process id", context.ProcessId.ToString()));
        lines.Add(Field("Started", context.StartedAt.ToString(TimestampFormat)));
        lines.Add(Rule);

        return lines;
    }

    public static IReadOnlyList<string> Footer(AppRunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var lines = new List<string> { Rule };

        if (record.FinishedAt is { } finished)
            lines.Add(Field("Finished", finished.ToString(TimestampFormat)));

        if (record.Duration is { } duration)
            lines.Add(Field("Duration", Describe(duration)));

        lines.Add(Field("Exit code", record.ExitCode?.ToString() ?? "?"));
        lines.Add(Field("Outcome", Loc.T("Outcome." + record.Outcome)));

        if (!string.IsNullOrWhiteSpace(record.Error))
            lines.Add(Field("Note", record.Error));

        lines.Add(Rule);
        return lines;
    }

    /// <summary>
    /// Thời lượng viết cho người đọc: mili giây khi rất ngắn, giờ:phút:giây khi dài.
    ///
    /// Định dạng theo culture bất biến chứ không theo máy: dấu thập phân của tiếng Việt là
    /// dấu phẩy, và một file log đổi nội dung tuỳ vùng miền của máy ghi nó thì không đối
    /// chiếu được giữa hai máy.
    /// </summary>
    public static string Describe(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1))
            return duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";

        if (duration < TimeSpan.FromMinutes(1))
            return duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

        return duration.ToString(
            duration < TimeSpan.FromHours(1) ? @"mm\:ss" : @"h\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string Field(string label, string value)
        => new StringBuilder(label.PadRight(LabelWidth)).Append(": ").Append(value).ToString();
}
