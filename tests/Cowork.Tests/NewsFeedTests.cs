using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.News;

namespace Cowork.Tests;

/// <summary>
/// Đọc feed. Ba định dạng đang lưu hành đặt tên thẻ khác nhau, và cái nào cũng có thể
/// gửi về HTML trong phần tóm tắt hay một địa chỉ không dùng được — bộ đọc phải chịu hết.
/// </summary>
public class FeedParserTests
{
    private static readonly NewsSource Source =
        new("nguon", "Nguồn thử", "https://vi.du/feed", NewsRegion.Global, new[] { NewsTopic.Ai });

    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 8, 17, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Rss20_IsRead()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0">
              <channel>
                <title>Kênh</title>
                <item>
                  <title>Bài thứ nhất</title>
                  <link>https://vi.du/bai-1</link>
                  <description>Tóm tắt một</description>
                  <pubDate>Tue, 08 Sep 2026 03:00:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal("Bài thứ nhất", item.Title);
        Assert.Equal("https://vi.du/bai-1", item.Link);
        Assert.Equal("Tóm tắt một", item.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero), item.PublishedAt);
        Assert.False(item.DateEstimated);

        // Chủ đề và vùng đến từ khai báo nguồn, không phải từ nội dung feed.
        Assert.Equal(NewsTopic.Ai, Assert.Single(item.Topics));
        Assert.Equal("Nguồn thử", item.SourceName);
    }

    [Fact]
    public void Atom_ReadsHrefAndPrefersAlternateLink()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <title>An entry</title>
                <link rel="replies" href="https://vi.du/binh-luan" />
                <link rel="alternate" href="https://vi.du/bai-atom" />
                <summary>Đoạn tóm tắt</summary>
                <published>2026-09-08T05:30:00+07:00</published>
              </entry>
            </feed>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal("https://vi.du/bai-atom", item.Link);
        Assert.Equal(new DateTimeOffset(2026, 9, 8, 5, 30, 0, TimeSpan.FromHours(7)), item.PublishedAt);
    }

    /// <summary>arXiv vẫn phát RSS 1.0/RDF, ngày nằm ở thẻ dc:date.</summary>
    [Fact]
    public void Rdf_IsRead()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                     xmlns="http://purl.org/rss/1.0/"
                     xmlns:dc="http://purl.org/dc/elements/1.1/">
              <item>
                <title>Một bài báo</title>
                <link>https://arxiv.org/abs/2609.00001</link>
                <description>Abstract ở đây</description>
                <dc:date>2026-09-07T00:00:00Z</dc:date>
              </item>
            </rdf:RDF>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal("https://arxiv.org/abs/2609.00001", item.Link);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Fact]
    public void HtmlInSummary_IsStrippedAndTruncated()
    {
        var body = "<p>Xin ch&#224;o <b>bạn</b></p><script>alert(1)</script>" + new string('x', 400);

        var xml = $"""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>T</title>
              <link>https://vi.du/a</link>
              <description><![CDATA[{body}]]></description>
            </item></channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.DoesNotContain("<", item.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", item.Summary, StringComparison.Ordinal);
        Assert.StartsWith("Xin chào bạn", item.Summary, StringComparison.Ordinal);
        Assert.True(item.Summary.Length <= FeedParser.MaxSummaryLength + 1, item.Summary.Length.ToString());
        Assert.EndsWith("…", item.Summary, StringComparison.Ordinal);
    }

    /// <summary>Tiêu đề trở thành liên kết bấm được, nên một scheme lạ lọt qua là lỗ hổng chứ không phải bài lỗi.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/system.ini")]
    [InlineData("/chi-co-duong-dan")]
    [InlineData("")]
    public void NonHttpLink_IsDropped(string link)
    {
        var xml = $"""
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>T</title>
              <link>{link}</link>
            </item></channel></rss>
            """;

        Assert.Empty(FeedParser.Parse(xml, Source, FetchedAt));
    }

    [Fact]
    public void MissingDate_FallsBackToFetchTimeAndSaysSo()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>Không có ngày</title>
              <link>https://vi.du/a</link>
            </item></channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal(FetchedAt, item.PublishedAt);
        Assert.True(item.DateEstimated);
    }

    /// <summary>
    /// .NET từ chối cả chuỗi RFC 1123 khi thứ trong tuần không khớp ngày, mà đó là lỗi
    /// quen thuộc của trình sinh feed. Bỏ nguyên ngày vì lý do đó thì bài mới nhất
    /// bỗng mang giờ lấy về và trôi lên đầu bảng.
    /// </summary>
    [Fact]
    public void AWrongWeekdayName_DoesNotThrowAwayTheDate()
    {
        // 08/09/2026 là thứ Ba, feed ghi Mon.
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>T</title>
              <link>https://vi.du/a</link>
              <pubDate>Mon, 08 Sep 2026 03:00:00 GMT</pubDate>
            </item></channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero), item.PublishedAt);
        Assert.False(item.DateEstimated);
    }

    /// <summary>
    /// GitHub Trending là ảnh chụp bảng xếp hạng của một ngày: cả feed có một ngày, từng repo
    /// trong đó thì không. Lùi thẳng về giờ tải sẽ làm cả feed mang nhãn "vừa xong" và leo lên
    /// đầu bảng tin sau mỗi lần làm tươi.
    /// </summary>
    [Fact]
    public void WhenOnlyTheFeedCarriesADate_ItemsBorrowIt()
    {
        // 07/09/2026 là thứ Hai.
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel>
              <title>Trending</title>
              <pubDate>Mon, 07 Sep 2026 05:27:27 GMT</pubDate>
              <item><title>ai/repo</title><link>https://github.com/ai/repo</link></item>
            </channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal(new DateTimeOffset(2026, 9, 7, 5, 27, 27, TimeSpan.Zero), item.PublishedAt);

        // Vẫn là phỏng đoán: đó là ngày của feed, không phải của bài.
        Assert.True(item.DateEstimated);
    }

    [Fact]
    public void AnItemsOwnDate_BeatsTheFeedDate()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel>
              <pubDate>Mon, 07 Sep 2026 05:00:00 GMT</pubDate>
              <item>
                <title>T</title><link>https://vi.du/a</link>
                <pubDate>Tue, 08 Sep 2026 03:00:00 GMT</pubDate>
              </item>
            </channel></rss>
            """;

        var item = Assert.Single(FeedParser.Parse(xml, Source, FetchedAt));

        Assert.Equal(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero), item.PublishedAt);
        Assert.False(item.DateEstimated);
    }

    /// <summary>Trong Atom, <c>updated</c> của entry không được nhầm thành <c>updated</c> của feed.</summary>
    [Fact]
    public void InAtom_TheFeedDateDoesNotOverrideEntryDates()
    {
        const string xml = """
            <?xml version="1.0"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <updated>2026-09-01T00:00:00Z</updated>
              <entry>
                <title>A</title>
                <link rel="alternate" href="https://vi.du/a" />
                <updated>2026-09-08T03:00:00Z</updated>
              </entry>
              <entry>
                <title>B</title>
                <link rel="alternate" href="https://vi.du/b" />
              </entry>
            </feed>
            """;

        var items = FeedParser.Parse(xml, Source, FetchedAt);

        var a = items.Single(i => i.Title == "A");
        var b = items.Single(i => i.Title == "B");

        Assert.Equal(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero), a.PublishedAt);
        Assert.False(a.DateEstimated);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), b.PublishedAt);
        Assert.True(b.DateEstimated);
    }

    [Fact]
    public void GuidPermalink_StandsInForAMissingLink()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>T</title>
              <guid isPermaLink="true">https://vi.du/qua-guid</guid>
            </item></channel></rss>
            """;

        Assert.Equal("https://vi.du/qua-guid", Assert.Single(FeedParser.Parse(xml, Source, FetchedAt)).Link);
    }

    [Fact]
    public void GuidThatIsNotAPermalink_IsIgnored()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><item>
              <title>T</title>
              <guid isPermaLink="false">https://vi.du/khong-phai-lien-ket</guid>
            </item></channel></rss>
            """;

        Assert.Empty(FeedParser.Parse(xml, Source, FetchedAt));
    }

    /// <summary>Một nguồn trả rác không được làm sập cả bảng tin.</summary>
    [Theory]
    [InlineData("<rss><channel><item>chưa đóng")]
    [InlineData("<html><body>403 Forbidden</body></html>")]
    [InlineData("")]
    [InlineData(null)]
    public void BrokenContent_YieldsNothingInsteadOfThrowing(string? xml)
        => Assert.Empty(FeedParser.Parse(xml, Source, FetchedAt));

    /// <summary>
    /// Feed là dữ liệu của người lạ. Một DTD với thực thể ngoài có thể sai Cowork đi đọc
    /// file trên máy; bộ đọc phải từ chối cả tài liệu thay vì đọc ra một bài có nội dung đó.
    /// </summary>
    [Fact]
    public void ExternalEntity_IsRefused()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <rss version="2.0"><channel><item>
              <title>&xxe;</title>
              <link>https://vi.du/a</link>
            </item></channel></rss>
            """;

        Assert.Empty(FeedParser.Parse(xml, Source, FetchedAt));
    }
}

/// <summary>Cân đối bảng tin: đủ mới, đúng chủ đề, không trùng, và nghiêng về nguồn nước ngoài.</summary>
public class NewsDigestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(7));

    private static NewsItem Item(
        string id,
        NewsRegion region = NewsRegion.Global,
        int minutesAgo = 10,
        string source = "s",
        NewsTopic topic = NewsTopic.Ai)
        => new()
        {
            Title = "Bài " + id,
            Link = "https://" + source + ".example/" + id,
            PublishedAt = Now.AddMinutes(-minutesAgo),
            SourceId = source,
            SourceName = source,
            Region = region,
            Topics = new[] { topic },
        };

    private static NewsDigestOptions Options(int maxItems = 10, int vietnamPercent = 25) => new()
    {
        Topics = new[] { NewsTopic.Ai, NewsTopic.Agents },
        IncludeVietnam = true,
        VietnamPercent = vietnamPercent,
        MaxItems = maxItems,
        MaxAgeDays = 3,
        MaxPerSource = 100,
    };

    [Fact]
    public void OnlySelectedTopics_GetThrough()
    {
        var items = new[]
        {
            Item("a", topic: NewsTopic.Ai),
            Item("b", topic: NewsTopic.Security),
            Item("c", topic: NewsTopic.Agents),
        };

        var digest = NewsDigest.Build(items, Options(), Now);

        Assert.Equal(new[] { "Bài a", "Bài c" }, digest.Select(i => i.Title).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void NoTopicSelected_YieldsNothingRatherThanEverything()
        => Assert.Empty(NewsDigest.Build(new[] { Item("a") }, Options() with { Topics = Array.Empty<NewsTopic>() }, Now));

    [Fact]
    public void ItemsOlderThanTheWindow_AreDropped()
    {
        var items = new[]
        {
            Item("moi", minutesAgo: 30),
            Item("cu", minutesAgo: (int)TimeSpan.FromDays(4).TotalMinutes),
        };

        Assert.Equal("Bài moi", Assert.Single(NewsDigest.Build(items, Options(), Now)).Title);
    }

    /// <summary>Feed ghi sai múi giờ hay đặt lịch trước: nếu tin, bài đó đứng đầu bảng cho tới khi hết hạn.</summary>
    [Fact]
    public void ItemsFarInTheFuture_AreDropped()
    {
        var items = new[]
        {
            Item("that", minutesAgo: 5),
            Item("tuong-lai", minutesAgo: -(int)TimeSpan.FromDays(2).TotalMinutes),
        };

        Assert.Equal("Bài that", Assert.Single(NewsDigest.Build(items, Options(), Now)).Title);
    }

    /// <summary>Cùng một bài đi qua hai nguồn với đuôi theo dõi khác nhau vẫn là một bài.</summary>
    [Fact]
    public void SameArticleFromTwoSources_AppearsOnce()
    {
        var items = new[]
        {
            Item("x") with { Link = "https://bao.example/tin-abc", SourceId = "mot", SourceName = "mot" },
            Item("x") with { Link = "https://www.bao.example/tin-abc/?utm_source=rss", SourceId = "hai", SourceName = "hai" },
        };

        Assert.Single(NewsDigest.Build(items, Options(), Now));
    }

    [Fact]
    public void OneProlificSource_CannotFillThePage()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Item("bai" + i, source: "on-ao", minutesAgo: i))
            .Concat(Enumerable.Range(0, 3).Select(i => Item("khac" + i, source: "yen", minutesAgo: 100 + i)));

        var digest = NewsDigest.Build(items, Options(maxItems: 10) with { MaxPerSource = 4 }, Now);

        Assert.Equal(4, digest.Count(i => i.SourceId == "on-ao"));
        Assert.Equal(3, digest.Count(i => i.SourceId == "yen"));
    }

    /// <summary>Đây là điều người dùng yêu cầu: đọc chủ yếu tin nước ngoài, vẫn còn một phần tin trong nước.</summary>
    [Fact]
    public void ForeignSourcesGetMostOfThePage_LocalOnesKeepTheirShare()
    {
        var items = Enumerable.Range(0, 30).Select(i => Item("g" + i, source: "g" + i, minutesAgo: 200 + i))
            .Concat(Enumerable.Range(0, 30).Select(i => Item("v" + i, NewsRegion.Vietnam, source: "v" + i, minutesAgo: i)));

        var digest = NewsDigest.Build(items, Options(maxItems: 20, vietnamPercent: 25), Now);

        Assert.Equal(20, digest.Count);
        Assert.Equal(5, digest.Count(i => i.Region == NewsRegion.Vietnam));

        // Tin trong nước mới hơn hẳn mà vẫn không chiếm chỗ — hạn mức thắng thứ tự thời gian.
        Assert.Equal(15, digest.Count(i => i.Region == NewsRegion.Global));
    }

    /// <summary>Ngày các báo trong nước im ắng thì bảng tin không được ngắn đi.</summary>
    [Fact]
    public void WhenLocalSourcesAreQuiet_ForeignStoriesFillTheGap()
    {
        var items = Enumerable.Range(0, 30).Select(i => Item("g" + i, source: "g" + i, minutesAgo: i))
            .Concat(new[] { Item("v0", NewsRegion.Vietnam, source: "v") });

        var digest = NewsDigest.Build(items, Options(maxItems: 20), Now);

        Assert.Equal(20, digest.Count);
        Assert.Equal(1, digest.Count(i => i.Region == NewsRegion.Vietnam));
    }

    /// <summary>Và ngược lại: hôm nào nguồn nước ngoài trục trặc thì tin trong nước lấp chỗ.</summary>
    [Fact]
    public void WhenForeignSourcesAreMissing_LocalStoriesFillTheGap()
    {
        var items = Enumerable.Range(0, 30).Select(i => Item("v" + i, NewsRegion.Vietnam, source: "v" + i, minutesAgo: i))
            .Concat(new[] { Item("g0", source: "g") });

        var digest = NewsDigest.Build(items, Options(maxItems: 20), Now);

        Assert.Equal(20, digest.Count);
        Assert.Equal(19, digest.Count(i => i.Region == NewsRegion.Vietnam));
    }

    [Fact]
    public void VietnamTurnedOff_MeansNoLocalStoriesAtAll()
    {
        var items = new[] { Item("g"), Item("v", NewsRegion.Vietnam) };

        var digest = NewsDigest.Build(items, Options() with { IncludeVietnam = false }, Now);

        Assert.Equal(NewsRegion.Global, Assert.Single(digest).Region);
    }

    /// <summary>Đã tick "kèm tin trong nước" mà làm tròn ra 0 chỗ thì vẫn phải còn một chỗ.</summary>
    [Fact]
    public void ASmallShare_StillLeavesRoomForOneLocalStory()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item("g" + i, source: "g" + i, minutesAgo: 100 + i))
            .Concat(new[] { Item("v0", NewsRegion.Vietnam, source: "v") });

        var digest = NewsDigest.Build(items, Options(maxItems: 5, vietnamPercent: 1), Now);

        Assert.Equal(5, digest.Count);
        Assert.Equal(1, digest.Count(i => i.Region == NewsRegion.Vietnam));
    }

    [Fact]
    public void Result_IsSortedNewestFirst()
    {
        var items = new[] { Item("cu", minutesAgo: 300), Item("moi", minutesAgo: 5), Item("giua", minutesAgo: 60) };

        var digest = NewsDigest.Build(items, Options(), Now);

        Assert.Equal(new[] { "Bài moi", "Bài giua", "Bài cu" }, digest.Select(i => i.Title));
    }

    /// <summary>
    /// Hạn mức đúng trên cả bảng vẫn chưa đủ: nếu tin trong nước dồn hết lên đầu thì màn hình
    /// đầu tiên — chỗ người ta thực sự đọc — vẫn toàn tin trong nước.
    /// </summary>
    [Fact]
    public void LocalStories_AreSpreadOutInsteadOfClumpingAtTheTop()
    {
        // Báo trong nước vừa đăng dày, vừa mới hơn hẳn — trường hợp xấu nhất cho việc sắp theo giờ.
        var items = Enumerable.Range(0, 40).Select(i => Item("g" + i, source: "g" + i, minutesAgo: 300 + i))
            .Concat(Enumerable.Range(0, 40).Select(i => Item("v" + i, NewsRegion.Vietnam, source: "v" + i, minutesAgo: i)));

        var digest = NewsDigest.Build(items, Options(maxItems: 20, vietnamPercent: 25), Now);

        Assert.Equal(5, digest.Count(i => i.Region == NewsRegion.Vietnam));

        // Trong tám bài đầu, tin trong nước không được quá hai — tức là vẫn quanh mức 25%.
        Assert.InRange(digest.Take(8).Count(i => i.Region == NewsRegion.Vietnam), 1, 2);
    }

    /// <summary>Rải đều không được làm đảo thứ tự thời gian bên trong từng bên.</summary>
    [Fact]
    public void WithinEachSide_TheOrderStaysNewestFirst()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item("g" + i, source: "g" + i, minutesAgo: i * 10))
            .Concat(Enumerable.Range(0, 10).Select(i => Item("v" + i, NewsRegion.Vietnam, source: "v" + i, minutesAgo: i * 7)));

        var digest = NewsDigest.Build(items, Options(maxItems: 20), Now);

        foreach (var region in new[] { NewsRegion.Global, NewsRegion.Vietnam })
        {
            var side = digest.Where(i => i.Region == region).Select(i => i.PublishedAt).ToList();
            Assert.Equal(side.OrderByDescending(t => t), side);
        }
    }

    /// <summary>Không có tin trong nước thì bảng vẫn thuần thời gian, không có gì để trộn.</summary>
    [Fact]
    public void WithoutLocalStories_TheOrderIsPurelyChronological()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item("g" + i, source: "g" + i, minutesAgo: i * 10));

        var digest = NewsDigest.Build(items, Options(maxItems: 20), Now);

        Assert.Equal(digest.Select(i => i.PublishedAt).OrderByDescending(t => t), digest.Select(i => i.PublishedAt));
    }

    /// <summary>"Còn mới không" là quyết định về thời gian, nên nó phải theo <c>now</c> được truyền vào.</summary>
    [Fact]
    public void TheSameItems_AgeOutAsNowMovesForward()
    {
        var items = new[] { Item("a", minutesAgo: 10) };
        var options = Options();

        Assert.Single(NewsDigest.Build(items, options, Now));
        Assert.Empty(NewsDigest.Build(items, options, Now.AddDays(4)));
    }
}

/// <summary>Danh mục nguồn dựng sẵn: sai một địa chỉ ở đây là mất hẳn một nguồn mà không có gì báo.</summary>
public class NewsCatalogTests
{
    [Fact]
    public void EverySource_IsWellFormed()
    {
        foreach (var source in NewsCatalog.BuiltIn)
        {
            Assert.False(string.IsNullOrWhiteSpace(source.Name), source.Id);
            Assert.NotEmpty(source.Topics);

            Assert.True(
                Uri.TryCreate(source.FeedUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
                $"{source.Id}: {source.FeedUrl}");
        }
    }

    [Fact]
    public void SourceIds_AreUnique()
    {
        var duplicates = NewsCatalog.BuiltIn
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>Một chủ đề không có nguồn nào là một ô tick chọn xong không ra bài nào.</summary>
    [Fact]
    public void EveryTopic_HasAtLeastOneForeignSource()
    {
        foreach (var topic in Enum.GetValues<NewsTopic>())
        {
            Assert.Contains(
                NewsCatalog.BuiltIn,
                s => s.Region == NewsRegion.Global && s.Topics.Contains(topic));
        }
    }

    [Fact]
    public void ForeignSources_OutnumberLocalOnes()
        => Assert.True(
            NewsCatalog.BuiltIn.Count(s => s.Region == NewsRegion.Global)
            > NewsCatalog.BuiltIn.Count(s => s.Region == NewsRegion.Vietnam));

    [Fact]
    public void Resolve_SkipsSourcesThatServeNoSelectedTopic()
    {
        var settings = new NewsSettings { Topics = new() { NewsTopic.Security } };

        var resolved = NewsCatalog.Resolve(settings);

        Assert.NotEmpty(resolved);
        Assert.All(resolved, s => Assert.Contains(NewsTopic.Security, s.Topics));
    }

    [Fact]
    public void Resolve_SkipsDisabledSources()
    {
        var settings = new NewsSettings();
        settings.DisabledSourceIds.Add("hacker-news");

        Assert.DoesNotContain(NewsCatalog.Resolve(settings), s => s.Id == "hacker-news");
    }

    [Fact]
    public void Resolve_DropsLocalSourcesWhenVietnamIsOff()
    {
        var settings = new NewsSettings { IncludeVietnam = false };

        Assert.DoesNotContain(NewsCatalog.Resolve(settings), s => s.Region == NewsRegion.Vietnam);
    }

    [Fact]
    public void CustomFeed_JoinsTheList()
    {
        var settings = new NewsSettings();
        settings.CustomSources.Add(new CustomNewsSource
        {
            Name = "Blog riêng",
            FeedUrl = "https://vi.du/feed.xml",
            Topics = { NewsTopic.Ai },
        });

        Assert.Contains(NewsCatalog.Resolve(settings), s => s.Name == "Blog riêng");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("khong-phai-dia-chi")]
    [InlineData("")]
    public void CustomFeedWithABadAddress_IsIgnored(string url)
    {
        var settings = new NewsSettings();
        settings.CustomSources.Add(new CustomNewsSource { Name = "Xấu", FeedUrl = url, Topics = { NewsTopic.Ai } });

        Assert.DoesNotContain(NewsCatalog.All(settings), s => s.Name == "Xấu");
    }

    [Fact]
    public void CustomFeedWithoutATopic_IsIgnored()
    {
        var settings = new NewsSettings();
        settings.CustomSources.Add(new CustomNewsSource { Name = "Không chủ đề", FeedUrl = "https://vi.du/feed" });

        Assert.DoesNotContain(NewsCatalog.All(settings), s => s.Name == "Không chủ đề");
    }
}

/// <summary>Nhãn "cách đây bao lâu" — theo <c>now</c> truyền vào, và theo ngôn ngữ đang bật.</summary>
public class NewsTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void UnderAMinute_ReadsAsJustNow()
        => Assert.Equal("vừa xong", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddSeconds(-20), Now));

    [Fact]
    public void MinutesHoursAndDays_EachGetTheirOwnWording()
    {
        Assert.Equal("20 phút trước", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddMinutes(-20), Now));
        Assert.Equal("3 giờ trước", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddHours(-3), Now));
        Assert.Equal("2 ngày trước", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddDays(-2), Now));
    }

    [Fact]
    public void EnglishAndVietnamese_AreBothAvailableAtOnce()
    {
        Assert.Equal("3 h ago", NewsTime.Describe(AppLanguage.English, Now.AddHours(-3), Now));
        Assert.Equal("3 giờ trước", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddHours(-3), Now));
    }

    /// <summary>Ngày lệch về phía trước không được ra "âm 3 phút trước".</summary>
    [Fact]
    public void AFutureTimestamp_StillReadsAsJustNow()
        => Assert.Equal("vừa xong", NewsTime.Describe(AppLanguage.Vietnamese, Now.AddMinutes(5), Now));
}
