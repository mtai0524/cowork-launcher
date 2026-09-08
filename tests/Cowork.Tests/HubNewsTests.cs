using Cowork.Core.News;
using Cowork.Core.Services;
using Cowork.Hub;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cowork.Tests;

/// <summary>
/// Bảng tin phía hub. Điểm khác biệt duy nhất so với bản trên máy nằm ở đây, nên nó phải
/// được nói rõ bằng test: hub tải nguyên danh mục cho mọi người, không tải theo lựa chọn
/// của người đang mở trang.
/// </summary>
public class HubNewsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(7));

    /// <summary>Trả về một feed hợp lệ cho mọi địa chỉ, và nhớ đã bị hỏi những đâu.</summary>
    private sealed class RecordingFeedClient : INewsFeedClient
    {
        private readonly object _gate = new();

        public List<string> Requested { get; } = new();

        public int Rounds { get; private set; }

        public Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            lock (_gate)
                Requested.Add(url);

            var xml = "<rss version=\"2.0\"><channel><item>"
                      + $"<title>Bài của {url}</title><link>{url.Replace("/feed", "/bai")}</link>"
                      + "<pubDate>Tue, 08 Sep 2026 03:00:00 GMT</pubDate>"
                      + "</item></channel></rss>";

            return Task.FromResult<string?>(xml);
        }

        public void NextRound()
        {
            lock (_gate)
            {
                Rounds++;
                Requested.Clear();
            }
        }
    }

    private static HubNewsFeed Build(INewsFeedClient client, IClock clock)
        => new(client, clock, NullLogger<HubNewsFeed>.Instance);

    /// <summary>
    /// Nếu hub tải theo chủ đề của người đang xem, người chọn "AI" sẽ xoá mất tin công nghệ
    /// của người bên cạnh — <see cref="NewsService"/> bỏ nguồn không nằm trong danh sách được giao.
    /// </summary>
    [Fact]
    public async Task EveryRefresh_PullsTheWholeCatalog()
    {
        var client = new RecordingFeedClient();
        var feed = Build(client, new FixedClock(Now));

        await feed.EnsureFreshAsync();

        Assert.Equal(
            NewsCatalog.BuiltIn.Select(s => s.FeedUrl).OrderBy(u => u, StringComparer.Ordinal),
            client.Requested.OrderBy(u => u, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WithinTheInterval_ASecondVisitorDoesNotTriggerAnotherFetch()
    {
        var client = new RecordingFeedClient();
        var clock = new FixedClock(Now);
        var feed = Build(client, clock);

        Assert.True(await feed.EnsureFreshAsync());
        client.NextRound();

        clock.Advance(HubNewsFeed.RefreshInterval - TimeSpan.FromMinutes(1));

        Assert.False(await feed.EnsureFreshAsync());
        Assert.Empty(client.Requested);
    }

    [Fact]
    public async Task OnceTheIntervalHasPassed_TheNextVisitorRefreshesIt()
    {
        var client = new RecordingFeedClient();
        var clock = new FixedClock(Now);
        var feed = Build(client, clock);

        await feed.EnsureFreshAsync();
        client.NextRound();

        clock.Advance(HubNewsFeed.RefreshInterval);

        Assert.True(await feed.EnsureFreshAsync());
        Assert.NotEmpty(client.Requested);
    }

    /// <summary>Nút "Lấy tin mới" phải tải lại ngay, không đợi hết hạn.</summary>
    [Fact]
    public async Task ExplicitRefresh_IgnoresTheInterval()
    {
        var client = new RecordingFeedClient();
        var feed = Build(client, new FixedClock(Now));

        await feed.EnsureFreshAsync();
        client.NextRound();

        await feed.RefreshAsync();

        Assert.NotEmpty(client.Requested);
    }

    /// <summary>
    /// Hai người chọn hai kiểu khác nhau vẫn đọc chung một bộ tin — lọc là việc của trang,
    /// không phải của lần tải.
    /// </summary>
    [Fact]
    public async Task TwoVisitors_FilterTheSameStoriesDifferently()
    {
        var client = new RecordingFeedClient();
        var clock = new FixedClock(Now);
        var feed = Build(client, clock);

        await feed.EnsureFreshAsync();

        var aiOnly = NewsDigest.Build(
            feed.Items,
            UiPreferences.ParseNews("Ai").ToDigestOptions(),
            clock.Now);

        var securityOnly = NewsDigest.Build(
            feed.Items,
            UiPreferences.ParseNews("Security").ToDigestOptions(),
            clock.Now);

        Assert.NotEmpty(aiOnly);
        Assert.NotEmpty(securityOnly);
        Assert.All(aiOnly, item => Assert.Contains(NewsTopic.Ai, item.Topics));
        Assert.All(securityOnly, item => Assert.Contains(NewsTopic.Security, item.Topics));
    }

    /// <summary>Chưa tick "kèm tin trong nước" thì trang không được hiện bài của báo trong nước.</summary>
    [Fact]
    public async Task VietnamTurnedOffInTheCookie_HidesLocalStories()
    {
        var client = new RecordingFeedClient();
        var clock = new FixedClock(Now);
        var feed = Build(client, clock);

        await feed.EnsureFreshAsync();

        var withVietnam = NewsDigest.Build(feed.Items, UiPreferences.ParseNews("Technology|vn").ToDigestOptions(), clock.Now);
        var without = NewsDigest.Build(feed.Items, UiPreferences.ParseNews("Technology").ToDigestOptions(), clock.Now);

        Assert.Contains(withVietnam, i => i.Region == NewsRegion.Vietnam);
        Assert.DoesNotContain(without, i => i.Region == NewsRegion.Vietnam);
    }
}
