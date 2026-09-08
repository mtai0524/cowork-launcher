using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cowork.Core.Services;

namespace Cowork.Core.News;

/// <summary>Kết quả một lần lấy tin, đủ để giao diện nói cho người dùng biết chuyện gì đã xảy ra.</summary>
/// <param name="Skipped">Có lần lấy khác đang chạy nên lần này không làm gì.</param>
public sealed record NewsRefreshResult(
    DateTimeOffset At,
    int SourceCount,
    int FailedCount,
    int ItemCount,
    bool Skipped = false)
{
    public bool AllFailed => !Skipped && SourceCount > 0 && FailedCount == SourceCount;
}

/// <summary>
/// Giữ tin đã tải về và biết khi nào phải tải lại.
///
/// Bài được giữ theo từng nguồn chứ không gộp thành một rổ: một nguồn hỏng thì chỉ phần
/// của nó là cũ, phần còn lại vẫn tươi. Nếu gộp, một lần mất mạng sẽ xoá trắng bảng tin.
///
/// Không có gì ở đây đụng tới giao diện, nên cùng một lớp phục vụ cả app trên máy lẫn hub.
/// Chỉ khác chỗ đưa vào <c>cacheFilePath</c>: app lưu xuống đĩa để mở lên là có tin ngay,
/// hub chạy liên tục nên giữ trong bộ nhớ là đủ.
/// </summary>
public sealed class NewsService
{
    /// <summary>Tải song song vừa phải: đủ nhanh, không biến một lần làm tươi thành cơn bão kết nối.</summary>
    private const int MaxParallelFetches = 6;

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly INewsFeedClient _client;
    private readonly IClock _clock;
    private readonly ICoworkLogger _logger;
    private readonly string? _cacheFilePath;

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<NewsItem>> _bySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshing = new(1, 1);

    private DateTimeOffset? _refreshedAt;
    private IReadOnlyList<string> _failedSources = Array.Empty<string>();

    public NewsService(INewsFeedClient client, IClock clock, ICoworkLogger logger, string? cacheFilePath = null)
    {
        _client = client;
        _clock = clock;
        _logger = logger;
        _cacheFilePath = cacheFilePath;
    }

    /// <summary>Bắn ra sau mỗi lần lấy tin xong. Chạy trên luồng nền — bên giao diện phải tự đưa về luồng của mình.</summary>
    public event EventHandler<NewsRefreshResult>? Refreshed;

    /// <summary>Mọi bài đang giữ, chưa lọc theo chủ đề. Việc lọc là của <see cref="NewsDigest"/>.</summary>
    public IReadOnlyList<NewsItem> Items
    {
        get
        {
            lock (_gate)
                return _bySource.Values.SelectMany(list => list).ToList();
        }
    }

    public DateTimeOffset? LastRefreshedAt
    {
        get
        {
            lock (_gate)
                return _refreshedAt;
        }
    }

    /// <summary>Tên những nguồn hỏng ở lần lấy gần nhất — để giao diện nói rõ thiếu tin của ai.</summary>
    public IReadOnlyList<string> FailedSources
    {
        get
        {
            lock (_gate)
                return _failedSources;
        }
    }

    /// <summary>Đã tới lúc tải lại chưa. Quyết định về thời gian nên <c>now</c> đi vào từ ngoài.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan maxAge)
    {
        lock (_gate)
            return _refreshedAt is not { } last || now - last >= maxAge;
    }

    /// <summary>
    /// Tải lại toàn bộ nguồn được giao. Nguồn nào hỏng thì giữ nguyên bài của lần trước;
    /// nguồn nào không còn trong danh sách thì bỏ hẳn, nếu không tắt một chủ đề xong
    /// bài của nó vẫn nằm lại trên bảng.
    /// </summary>
    public async Task<NewsRefreshResult> RefreshAsync(
        IReadOnlyList<NewsSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Không xếp hàng chờ: hai lần bấm liên tiếp thì lần sau không có gì mới để làm.
        if (!await _refreshing.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            lock (_gate)
                return new NewsRefreshResult(_refreshedAt ?? _clock.Now, sources.Count, 0, 0, Skipped: true);
        }

        try
        {
            var now = _clock.Now;
            var fetched = new Dictionary<string, IReadOnlyList<NewsItem>>(StringComparer.OrdinalIgnoreCase);
            var failed = new List<string>();
            var writeGate = new object();

            await Parallel.ForEachAsync(
                sources,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelFetches,
                    CancellationToken = cancellationToken,
                },
                async (source, token) =>
                {
                    var xml = await _client.DownloadAsync(source.FeedUrl, token).ConfigureAwait(false);
                    var items = FeedParser.Parse(xml, source, now);

                    lock (writeGate)
                    {
                        // Tải được nhưng không đọc ra bài nào cũng là hỏng: hoặc feed đổi địa chỉ,
                        // hoặc máy chủ trả về trang lỗi dạng HTML.
                        if (items.Count == 0)
                            failed.Add(source.Name);
                        else
                            fetched[source.Id] = items;
                    }
                }).ConfigureAwait(false);

            int total;
            lock (_gate)
            {
                var keep = sources.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var stale in _bySource.Keys.Where(id => !keep.Contains(id)).ToList())
                    _bySource.Remove(stale);

                foreach (var pair in fetched)
                    _bySource[pair.Key] = pair.Value;

                _refreshedAt = now;
                _failedSources = failed;
                total = _bySource.Values.Sum(list => list.Count);
            }

            if (failed.Count > 0)
                _logger.Warning($"Bảng tin: {failed.Count}/{sources.Count} nguồn không lấy được ({string.Join(", ", failed)}).");

            SaveCache();

            var result = new NewsRefreshResult(now, sources.Count, failed.Count, total);
            Refreshed?.Invoke(this, result);

            return result;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    /// <summary>
    /// Nạp lại bài của lần chạy trước. Gọi lúc khởi động để bảng tin có nội dung ngay,
    /// trước khi lần tải đầu tiên kịp xong.
    /// </summary>
    public void LoadCache()
    {
        if (_cacheFilePath is null || !File.Exists(_cacheFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var cache = JsonSerializer.Deserialize<NewsCacheFile>(json, CacheJson);

            if (cache?.Sources is null)
                return;

            lock (_gate)
            {
                _bySource.Clear();
                foreach (var pair in cache.Sources.Where(p => p.Value is { Count: > 0 }))
                    _bySource[pair.Key] = pair.Value;

                _refreshedAt = cache.RefreshedAt;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Bộ nhớ đệm hỏng chỉ có nghĩa là phải tải lại, không đáng làm phiền người dùng.
            _logger.Warning("Không đọc được bộ nhớ đệm bảng tin: " + ex.Message);
        }
    }

    private void SaveCache()
    {
        if (_cacheFilePath is null)
            return;

        try
        {
            NewsCacheFile snapshot;
            lock (_gate)
                snapshot = new NewsCacheFile(_refreshedAt ?? _clock.Now, new Dictionary<string, IReadOnlyList<NewsItem>>(_bySource, StringComparer.OrdinalIgnoreCase));

            var json = JsonSerializer.Serialize(snapshot, CacheJson);
            var temp = _cacheFilePath + ".tmp";

            File.WriteAllText(temp, json);

            if (File.Exists(_cacheFilePath))
                File.Replace(temp, _cacheFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, _cacheFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Không ghi được bộ nhớ đệm bảng tin: " + ex.Message);
        }
    }

    private sealed record NewsCacheFile(
        DateTimeOffset RefreshedAt,
        Dictionary<string, IReadOnlyList<NewsItem>> Sources);
}
