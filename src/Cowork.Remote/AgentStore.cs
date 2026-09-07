using System.Text.Json;
using Cowork.Core.Services;

namespace Cowork.Remote;

/// <summary>
/// Nơi giữ danh sách máy giữa hai lần chạy hub. Tách khỏi <see cref="AgentDirectory"/>
/// để test danh sách máy không cần chạm đĩa.
/// </summary>
public interface IAgentStore
{
    /// <summary>Trả về <c>null</c> khi chưa có gì được lưu — người gọi khi đó lấy giống trong appsettings.</summary>
    IReadOnlyList<AgentCredential>? Load();

    void Save(IReadOnlyList<AgentCredential> agents);
}

/// <summary>
/// Lưu danh sách máy thành một file JSON riêng, không phải appsettings.json.
///
/// Hai lý do không ghi thẳng vào appsettings.json: ghi vào đó làm ASP.NET nạp lại cấu hình
/// giữa chừng, và mỗi lần deploy bằng WebDeploy sẽ đồng bộ đè file ấy — sửa trên web
/// sẽ lặng lẽ mất. File riêng nằm ngoài phạm vi đồng bộ thì sống qua được deploy.
/// </summary>
public sealed class JsonAgentStore : IAgentStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    public JsonAgentStore(string path, ICoworkLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _logger = logger;
    }

    public IReadOnlyList<AgentCredential>? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return null;

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<AgentCredential>>(json, SerializerOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Đọc hụt thì lùi về giống trong appsettings thay vì để hub không khởi động được:
                // mất danh sách máy còn cứu được, hub chết thì không ai vào sửa được nữa.
                _logger.Error($"Không đọc được {_path}, dùng danh sách máy trong appsettings.", ex);
                return null;
            }
        }
    }

    public void Save(IReadOnlyList<AgentCredential> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(agents, SerializerOptions);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(_path))
                File.Replace(temp, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, _path);
        }
    }
}
