using System.Text.Json;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

public interface IRunHistoryStore
{
    IReadOnlyList<AppRunRecord> All();
    IReadOnlyList<AppRunRecord> ForApp(Guid appId);
    void Add(AppRunRecord record);
    void Prune(int retentionDays);
    void Clear();
}

/// <summary>Lịch sử chạy lưu thành một mảng JSON, nạp một lần khi khởi động rồi giữ trong bộ nhớ.</summary>
public sealed class JsonRunHistoryStore : IRunHistoryStore
{
    private readonly CoworkPaths _paths;
    private readonly ICoworkLogger _logger;
    private readonly List<AppRunRecord> _records;
    private readonly object _gate = new();

    /// <summary>Trần cứng để file lịch sử không phình vô hạn dù người dùng đặt retention dài.</summary>
    private const int MaxRecords = 5000;

    public JsonRunHistoryStore(CoworkPaths paths, ICoworkLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _records = LoadFromDisk();
    }

    private List<AppRunRecord> LoadFromDisk()
    {
        if (!File.Exists(_paths.HistoryFile))
            return new List<AppRunRecord>();

        try
        {
            var json = File.ReadAllText(_paths.HistoryFile);
            return JsonSerializer.Deserialize<List<AppRunRecord>>(json, JsonWorkspaceStore.SerializerOptions)
                   ?? new List<AppRunRecord>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.Warning("Không đọc được history.json: " + ex.Message);
            return new List<AppRunRecord>();
        }
    }

    public IReadOnlyList<AppRunRecord> All()
    {
        lock (_gate)
            return _records.OrderByDescending(r => r.StartedAt).ToList();
    }

    public IReadOnlyList<AppRunRecord> ForApp(Guid appId)
    {
        lock (_gate)
            return _records.Where(r => r.AppId == appId).OrderByDescending(r => r.StartedAt).ToList();
    }

    public void Add(AppRunRecord record)
    {
        lock (_gate)
        {
            // Cùng Id nghĩa là bản ghi được cập nhật (bắt đầu -> kết thúc).
            var existing = _records.FindIndex(r => r.Id == record.Id);
            if (existing >= 0)
                _records[existing] = record;
            else
                _records.Add(record);

            TrimLocked();
            Persist();
        }
    }

    public void Prune(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        lock (_gate)
        {
            var cutoff = DateTimeOffset.Now.AddDays(-retentionDays);
            var removed = _records.RemoveAll(r => r.StartedAt < cutoff);
            if (removed > 0)
                Persist();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
            Persist();
        }
    }

    private void TrimLocked()
    {
        if (_records.Count <= MaxRecords)
            return;

        var keep = _records.OrderByDescending(r => r.StartedAt).Take(MaxRecords).ToList();
        _records.Clear();
        _records.AddRange(keep);
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_records, JsonWorkspaceStore.SerializerOptions);
            File.WriteAllText(_paths.HistoryFile, json);
        }
        catch (IOException ex)
        {
            _logger.Warning("Không ghi được history.json: " + ex.Message);
        }
    }
}
