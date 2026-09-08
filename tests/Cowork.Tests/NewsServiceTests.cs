using Cowork.Core.News;
using Cowork.Core.Services;

namespace Cowork.Tests;

/// <summary>
/// Lấy tin và giữ tin. Điều đáng kiểm nhất không phải lúc mọi thứ chạy được, mà lúc một
/// nguồn chết: bảng tin phải mất đúng phần của nguồn đó, không mất cả trang.
/// </summary>
public class NewsServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(7));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cowork-news-" + Guid.NewGuid().ToString("N"));

    public NewsServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static NewsSource Source(string id, NewsTopic topic = NewsTopic.Ai)
        => new(id, id, $"https://{id}.example/feed", NewsRegion.Global, new[] { topic });

    private static string Feed(params string[] titles)
        => "<rss version=\"2.0\"><channel>"
           + string.Concat(titles.Select(t =>
               $"<item><title>{t}</title><link>https://x.example/{Uri.EscapeDataString(t)}</link>"
               + "<pubDate>Tue, 08 Sep 2026 03:00:00 GMT</pubDate></item>"))
           + "</channel></rss>";

    /// <summary>Client giả: trả về đúng nội dung đã khai cho mỗi địa chỉ, null nghĩa là nguồn hỏng.</summary>
    private sealed class FakeFeedClient : INewsFeedClient
    {
        public Dictionary<string, string?> Responses { get; } = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = new();

        public Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
        {
            lock (Requested)
                Requested.Add(url);

            return Task.FromResult(Responses.TryGetValue(url, out var body) ? body : null);
        }
    }

    private sealed class SilentLogger : ICoworkLogger
    {
        public List<string> Messages { get; } = new();

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            lock (Messages)
                Messages.Add(message);
        }
    }

    [Fact]
    public async Task Refresh_CollectsEverySource()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("A1", "A2");
        client.Responses["https://b.example/feed"] = Feed("B1");

        var service = new NewsService(client, new FixedClock(Now), new SilentLogger());

        var result = await service.RefreshAsync(new[] { Source("a"), Source("b") });

        Assert.Equal(2, result.SourceCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(3, result.ItemCount);
        Assert.Equal(Now, service.LastRefreshedAt);
    }

    /// <summary>Đây là lý do bài được giữ theo từng nguồn thay vì gộp một rổ.</summary>
    [Fact]
    public async Task ASourceThatBreaks_LosesOnlyItsOwnStories()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("A1");
        client.Responses["https://b.example/feed"] = Feed("B1");

        var clock = new FixedClock(Now);
        var service = new NewsService(client, clock, new SilentLogger());
        var sources = new[] { Source("a"), Source("b") };

        await service.RefreshAsync(sources);

        // b hỏng ở lần sau, a có bài mới.
        client.Responses["https://a.example/feed"] = Feed("A1", "A2");
        client.Responses["https://b.example/feed"] = null;
        clock.Advance(TimeSpan.FromHours(1));

        var result = await service.RefreshAsync(sources);

        Assert.Equal(1, result.FailedCount);
        Assert.Contains(service.Items, i => i.Title == "A2");
        Assert.Contains(service.Items, i => i.Title == "B1");
        Assert.Equal("b", Assert.Single(service.FailedSources));
    }

    /// <summary>Tải được nhưng ra 0 bài (máy chủ trả trang lỗi HTML) cũng phải tính là hỏng.</summary>
    [Fact]
    public async Task AnHtmlErrorPage_CountsAsAFailedSource()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = "<html><body>404</body></html>";

        var service = new NewsService(client, new FixedClock(Now), new SilentLogger());

        var result = await service.RefreshAsync(new[] { Source("a") });

        Assert.Equal(1, result.FailedCount);
        Assert.True(result.AllFailed);
    }

    /// <summary>Bỏ tick một chủ đề xong, bài của nó phải biến mất chứ không nằm lại.</summary>
    [Fact]
    public async Task DroppingASource_AlsoDropsItsStories()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("A1");
        client.Responses["https://b.example/feed"] = Feed("B1");

        var service = new NewsService(client, new FixedClock(Now), new SilentLogger());

        await service.RefreshAsync(new[] { Source("a"), Source("b") });
        await service.RefreshAsync(new[] { Source("a") });

        Assert.Equal("A1", Assert.Single(service.Items).Title);
    }

    [Fact]
    public void IsStale_FollowsTheNowItIsGiven()
    {
        var service = new NewsService(new FakeFeedClient(), new FixedClock(Now), new SilentLogger());

        // Chưa lấy lần nào thì luôn là cũ.
        Assert.True(service.IsStale(Now, TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task IsStale_TurnsTrueOnceTheIntervalHasPassed()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("A1");

        var service = new NewsService(client, new FixedClock(Now), new SilentLogger());
        await service.RefreshAsync(new[] { Source("a") });

        Assert.False(service.IsStale(Now.AddMinutes(30), TimeSpan.FromHours(1)));
        Assert.True(service.IsStale(Now.AddMinutes(90), TimeSpan.FromHours(1)));
    }

    /// <summary>Mở app lên là có tin ngay, trước khi lần tải đầu tiên kịp xong.</summary>
    [Fact]
    public async Task CachedStories_SurviveARestart()
    {
        var cacheFile = Path.Combine(_folder, "news-cache.json");
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("A1");

        var first = new NewsService(client, new FixedClock(Now), new SilentLogger(), cacheFile);
        await first.RefreshAsync(new[] { Source("a") });

        var second = new NewsService(new FakeFeedClient(), new FixedClock(Now), new SilentLogger(), cacheFile);
        second.LoadCache();

        Assert.Equal("A1", Assert.Single(second.Items).Title);
        Assert.Equal(Now, second.LastRefreshedAt);
    }

    [Fact]
    public void ACorruptCacheFile_IsIgnoredInsteadOfThrowing()
    {
        var cacheFile = Path.Combine(_folder, "news-cache.json");
        File.WriteAllText(cacheFile, "{ đây không phải json");

        var logger = new SilentLogger();
        var service = new NewsService(new FakeFeedClient(), new FixedClock(Now), logger, cacheFile);

        service.LoadCache();

        Assert.Empty(service.Items);
        Assert.NotEmpty(logger.Messages);
    }

    [Fact]
    public async Task NoSources_IsNotAnError()
    {
        var service = new NewsService(new FakeFeedClient(), new FixedClock(Now), new SilentLogger());

        var result = await service.RefreshAsync(Array.Empty<NewsSource>());

        Assert.Equal(0, result.SourceCount);
        Assert.False(result.AllFailed);
    }

    /// <summary>Cả chuỗi từ feed thô tới bảng tin — nơi bộ đọc và bộ chọn gặp nhau.</summary>
    [Fact]
    public async Task FromFeedToDigest_TheWholeChainHolds()
    {
        var client = new FakeFeedClient();
        client.Responses["https://a.example/feed"] = Feed("Tin AI");
        client.Responses["https://v.example/feed"] = Feed("Tin trong nước");

        var service = new NewsService(client, new FixedClock(Now), new SilentLogger());
        var local = new NewsSource("v", "v", "https://v.example/feed", NewsRegion.Vietnam, new[] { NewsTopic.Ai });

        await service.RefreshAsync(new[] { Source("a"), local });

        var digest = NewsDigest.Build(
            service.Items,
            new NewsDigestOptions { Topics = new[] { NewsTopic.Ai }, MaxItems = 10, MaxAgeDays = 3 },
            Now);

        Assert.Equal(2, digest.Count);
        Assert.Contains(digest, i => i.Region == NewsRegion.Vietnam);
    }
}
