using System.Net;
using System.Net.Http;
using System.Text;

namespace Cowork.Core.News;

/// <summary>
/// Tải nội dung thô của một feed. Là interface để test bộ đọc tin không cần ra mạng —
/// và để bảng tin kiểm được cả trường hợp một nguồn chết.
/// </summary>
public interface INewsFeedClient
{
    /// <summary>Nội dung feed, hoặc null nếu tải hỏng. Không ném cho lỗi mạng thông thường.</summary>
    Task<string?> DownloadAsync(string url, CancellationToken cancellationToken);
}

/// <summary>Bản dùng thật: HTTP, có nén, có hạn dung lượng và hạn thời gian.</summary>
public sealed class HttpNewsFeedClient : INewsFeedClient
{
    /// <summary>Feed lớn nhất trong danh sách dựng sẵn chưa tới 1 MB; 8 MB là mức chặn nguồn bất thường.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Một HttpClient dùng chung cho mọi nguồn; tạo mới mỗi lần lấy tin sẽ cạn socket.
    /// User-Agent là bắt buộc trên thực tế: khá nhiều báo trả 403 cho client không khai tên.
    /// </summary>
    private static readonly HttpClient Http = CreateClient();

    private readonly HttpClient _client;

    public HttpNewsFeedClient(HttpClient? client = null) => _client = client ?? Http;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Cowork/1.0 (+https://github.com/; feed reader)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml;q=0.9, */*;q=0.5");

        return client;
    }

    public async Task<string?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            if (response.Content.Headers.ContentLength > MaxBytes)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await ReadCappedAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            // Người gọi huỷ thì để nó nổi lên; còn lại là nguồn hỏng, không phải lỗi của Cowork.
            if (cancellationToken.IsCancellationRequested)
                throw;

            return null;
        }
    }

    /// <summary>
    /// Đọc tối đa <see cref="MaxBytes"/>. Một số máy chủ không khai Content-Length, nên chặn
    /// theo header thôi là chưa đủ — phải đếm trong lúc đọc.
    /// </summary>
    private static async Task<string?> ReadCappedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        using var memory = new MemoryStream();

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (memory.Length + read > MaxBytes)
                return null;

            memory.Write(buffer, 0, read);
        }

        // Feed tiếng Việt thỉnh thoảng còn ra UTF-8 có BOM; DetectEncodingFromByteOrderMarks lo phần đó.
        memory.Position = 0;
        using var reader = new StreamReader(memory, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
