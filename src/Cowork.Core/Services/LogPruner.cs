using System.Globalization;
using System.Text.RegularExpressions;

namespace Cowork.Core.Services;

/// <summary>
/// Dọn file log cũ trong thư mục logs. Mỗi app mỗi ngày một file, không dọn thì vài tháng là đầy
/// đĩa. Phần chọn file là hàm thuần nhận <c>today</c> để kiểm thử được mà không phải chờ ngày trôi.
/// </summary>
public static class LogPruner
{
    // cowork-20260905.log · app-<32 ký tự hex>-20260905.log — chỉ đụng đúng hai mẫu Cowork tự sinh,
    // thứ gì khác nằm trong thư mục này (người dùng tự chép vào) để nguyên.
    private static readonly Regex LogFileName =
        new(@"^(?:cowork|app-[0-9a-f]{32})-(\d{8})\.log$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Trong danh sách file, chọn ra những file có ngày cũ hơn <paramref name="retentionDays"/> tính tới
    /// <paramref name="today"/>. File đúng <paramref name="retentionDays"/> ngày tuổi vẫn được giữ.
    /// </summary>
    public static IReadOnlyList<string> SelectStale(IEnumerable<string> files, DateTime today, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (retentionDays <= 0)
            return Array.Empty<string>();

        var cutoff = today.Date.AddDays(-retentionDays);
        var stale = new List<string>();

        foreach (var file in files)
        {
            var match = LogFileName.Match(Path.GetFileName(file));
            if (!match.Success)
                continue;

            if (!DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
            {
                continue;
            }

            if (day < cutoff)
                stale.Add(file);
        }

        return stale;
    }

    /// <summary>Xoá file log cũ. Trả về số file đã xoá; một file không xoá được không chặn phần còn lại.</summary>
    public static int Prune(CoworkPaths paths, DateTime today, int retentionDays, ICoworkLogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        if (retentionDays <= 0 || !Directory.Exists(paths.LogDirectory))
            return 0;

        var removed = 0;
        foreach (var file in SelectStale(Directory.EnumerateFiles(paths.LogDirectory, "*.log"), today, retentionDays))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.Warning($"Không xoá được log cũ {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (removed > 0)
            logger.Info($"Đã xoá {removed} file log cũ hơn {retentionDays} ngày.");

        return removed;
    }
}
