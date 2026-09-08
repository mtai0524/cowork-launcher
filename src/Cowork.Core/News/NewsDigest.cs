namespace Cowork.Core.News;

/// <summary>Tham số cho một lần dựng bảng tin. Tách khỏi lớp thiết lập để phần chọn bài test được một mình.</summary>
public sealed record NewsDigestOptions
{
    public IReadOnlyList<NewsTopic> Topics { get; init; } = Array.Empty<NewsTopic>();

    public bool IncludeVietnam { get; init; } = true;

    /// <summary>Phần trăm chỗ dành cho tin trong nước.</summary>
    public int VietnamPercent { get; init; } = 25;

    public int MaxItems { get; init; } = 60;

    public int MaxAgeDays { get; init; } = 3;

    public int MaxPerSource { get; init; } = 6;
}

/// <summary>
/// Chọn ra bảng tin của một ngày từ tất cả những gì đã tải về.
///
/// Toàn bộ là hàm thuần và <c>now</c> là tham số: "bài này còn mới không" là một quyết định
/// về thời gian, và quyết định về thời gian trong Cowork không được tự gọi đồng hồ.
/// </summary>
public static class NewsDigest
{
    /// <summary>Ngày trong tương lai xa hơn ngần này là feed ghi sai — nếu tin, bài đó sẽ đứng đầu bảng mãi.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);

    public static IReadOnlyList<NewsItem> Build(
        IEnumerable<NewsItem> items,
        NewsDigestOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var wanted = options.Topics.ToHashSet();
        if (wanted.Count == 0 || options.MaxItems <= 0)
            return Array.Empty<NewsItem>();

        var maxAge = TimeSpan.FromDays(Math.Max(1, options.MaxAgeDays));

        var fresh = items
            .Where(item => item.Topics.Any(wanted.Contains))
            .Where(item => item.PublishedAt <= now + FutureTolerance)
            .Where(item => now - item.PublishedAt <= maxAge)
            .OrderByDescending(item => item.PublishedAt)
            .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = fresh.Where(item => seen.Add(item.DedupeKey)).ToList();

        var global = CapPerSource(unique.Where(i => i.Region != NewsRegion.Vietnam), options.MaxPerSource);
        var local = options.IncludeVietnam
            ? CapPerSource(unique.Where(i => i.Region == NewsRegion.Vietnam), options.MaxPerSource)
            : new List<NewsItem>();

        var localQuota = LocalQuota(options, local.Count);

        // Nước ngoài lấy trước phần của mình, rồi mới tới trong nước — đó là chỗ "ưu tiên
        // nước ngoài" thành hình. Bên nào không đủ bài thì bên kia lấp vào, để một ngày
        // các báo trong nước im ắng không làm bảng tin ngắn đi.
        var chosenGlobal = global.Take(options.MaxItems - localQuota).ToList();
        var chosenLocal = local.Take(localQuota).ToList();

        var room = options.MaxItems - chosenGlobal.Count - chosenLocal.Count;
        if (room > 0)
        {
            var extraGlobal = global.Skip(chosenGlobal.Count).Take(room).ToList();
            chosenGlobal.AddRange(extraGlobal);
            room -= extraGlobal.Count;
        }

        if (room > 0)
            chosenLocal.AddRange(local.Skip(chosenLocal.Count).Take(room));

        return Interleave(chosenGlobal, chosenLocal);
    }

    /// <summary>
    /// Trộn hai bên theo đúng tỉ lệ đã chia, thay vì nối lại rồi sắp chung theo thời gian.
    ///
    /// Sắp chung theo thời gian thì hạn mức vẫn đúng trên cả bảng, nhưng màn hình đầu tiên
    /// lại không: sáng nào các báo trong nước đăng dày hơn thì bảy tám bài đầu đều là tin
    /// trong nước, và người đọc — vốn chỉ nhìn tới đó — thấy ngược hẳn với "ưu tiên nước
    /// ngoài". Rải đều thì mọi đoạn của bảng đều giữ đúng tỉ lệ.
    ///
    /// Trong từng bên thứ tự thời gian vẫn nguyên: cả hai danh sách vào đây đã sắp mới nhất trước.
    /// </summary>
    private static List<NewsItem> Interleave(List<NewsItem> global, List<NewsItem> local)
    {
        var total = global.Count + local.Count;
        if (local.Count == 0 || global.Count == 0)
            return global.Count == 0 ? local : global;

        var share = (double)local.Count / total;
        var result = new List<NewsItem>(total);
        int g = 0, l = 0;

        while (g < global.Count || l < local.Count)
        {
            bool takeLocal;

            if (g >= global.Count)
                takeLocal = true;
            else if (l >= local.Count)
                takeLocal = false;
            else
                takeLocal = (double)l / (g + l + 1) < share;

            result.Add(takeLocal ? local[l++] : global[g++]);
        }

        return result;
    }

    /// <summary>
    /// Bao nhiêu chỗ để dành cho tin trong nước. Bật mà làm tròn xuống 0 thì vẫn cho một chỗ:
    /// người dùng đã tick "có tin Việt Nam", trả về đúng không bài nào là sai ý.
    /// </summary>
    private static int LocalQuota(NewsDigestOptions options, int available)
    {
        if (!options.IncludeVietnam || available == 0 || options.VietnamPercent <= 0)
            return 0;

        var quota = (int)Math.Round(options.MaxItems * (options.VietnamPercent / 100.0), MidpointRounding.AwayFromZero);

        return Math.Clamp(Math.Max(quota, 1), 0, options.MaxItems);
    }

    /// <summary>Giữ nguyên thứ tự đã sắp, chỉ bỏ bài thứ N+1 trở đi của cùng một nguồn.</summary>
    private static List<NewsItem> CapPerSource(IEnumerable<NewsItem> ordered, int maxPerSource)
    {
        var result = new List<NewsItem>();

        if (maxPerSource <= 0)
            return ordered.ToList();

        var taken = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ordered)
        {
            taken.TryGetValue(item.SourceId, out var count);
            if (count >= maxPerSource)
                continue;

            taken[item.SourceId] = count + 1;
            result.Add(item);
        }

        return result;
    }
}
