using Cowork.Core.News;
using Cowork.Core.Services;

namespace Cowork.Hub;

/// <summary>
/// Bảng tin của hub: một bộ tin dùng chung cho mọi người đang mở trang.
///
/// Hub tự gọi RSS chứ không xin tin từ máy nào — bảng tin không phải trạng thái của một
/// máy cụ thể, và nếu phải chờ máy ở nhà bật lên thì mở web buổi sáng sẽ chẳng có gì đọc.
///
/// Luôn tải nguyên danh mục thay vì chỉ chủ đề của người đang xem: nhiều người mỗi người
/// một lựa chọn, mà <see cref="NewsService"/> bỏ nguồn không nằm trong danh sách được giao —
/// tải theo lựa chọn của người này sẽ xoá tin của người kia. Lọc là việc của từng trang.
/// </summary>
public sealed class HubNewsFeed
{
    /// <summary>Đủ tươi cho một bảng tin theo ngày, đủ thưa để không dội vào ba mươi báo mỗi lần có người mở trang.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    private readonly NewsService _service;
    private readonly IClock _clock;

    public HubNewsFeed(INewsFeedClient client, IClock clock, ILogger<HubNewsFeed> logger)
    {
        _clock = clock;
        _service = new NewsService(client, clock, new HubLogger(logger));
    }

    public IReadOnlyList<NewsItem> Items => _service.Items;

    public DateTimeOffset? LastRefreshedAt => _service.LastRefreshedAt;

    public IReadOnlyList<string> FailedSources => _service.FailedSources;

    /// <summary>Tải lại nếu đã quá hạn. Trả về true nếu có gọi ra mạng lần này.</summary>
    public async Task<bool> EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_service.IsStale(_clock.Now, RefreshInterval))
            return false;

        await _service.RefreshAsync(NewsCatalog.BuiltIn, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Người dùng bấm "lấy tin mới": tải lại bất kể còn hạn hay không.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => _service.RefreshAsync(NewsCatalog.BuiltIn, cancellationToken);
}
