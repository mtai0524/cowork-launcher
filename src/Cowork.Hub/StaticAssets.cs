using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Cowork.Hub;

/// <summary>
/// Gắn vân tay nội dung vào URL của tài nguyên tĩnh: <c>/app.css?v=9f2c…</c>.
///
/// Không có nó thì trình duyệt giữ bản CSS cũ sau khi deploy — đường dẫn không đổi nên
/// nó chẳng có lý do gì để tải lại. Đây là chuyện đã xảy ra thật: một bản deploy đổi
/// hoàn toàn phần định kiểu mà người dùng vẫn thấy giao diện cũ với form vỡ layout.
///
/// Vân tay tính một lần cho mỗi file rồi nhớ lại, vì file tĩnh không đổi giữa chừng
/// trong lúc chạy. Sửa CSS lúc đang phát triển thì cần khởi động lại để thấy.
/// </summary>
public sealed class StaticAssets
{
    private readonly IWebHostEnvironment _environment;
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.Ordinal);

    public StaticAssets(IWebHostEnvironment environment) => _environment = environment;

    public string Url(string file) => $"/{file}?v={_fingerprints.GetOrAdd(file, Fingerprint)}";

    private string Fingerprint(string file)
    {
        var root = _environment.WebRootPath;
        if (string.IsNullOrEmpty(root))
            return "0";

        var path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return "0";

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
    }
}
