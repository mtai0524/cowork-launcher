using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

public interface IWorkspaceStore
{
    CoworkWorkspace Load();
    void Save(CoworkWorkspace workspace);
    string FilePath { get; }
}

/// <summary>Lưu workspace ra JSON, ghi nguyên tử qua file tạm để không mất dữ liệu khi ghi dở.</summary>
public sealed class JsonWorkspaceStore : IWorkspaceStore
{
    private readonly CoworkPaths _paths;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(),
            new TimeSpanJsonConverter(),
            new NullableTimeSpanJsonConverter(),
        },
    };

    public JsonWorkspaceStore(CoworkPaths paths, ICoworkLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string FilePath => _paths.WorkspaceFile;

    public CoworkWorkspace Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_paths.WorkspaceFile))
                return new CoworkWorkspace();

            try
            {
                var json = File.ReadAllText(_paths.WorkspaceFile);
                var workspace = JsonSerializer.Deserialize<CoworkWorkspace>(json, SerializerOptions);
                return Normalize(workspace ?? new CoworkWorkspace());
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // File hỏng: đổi tên để giữ bằng chứng rồi bắt đầu lại từ workspace rỗng.
                _logger.Error("Không đọc được workspace.json, tạo mới.", ex);
                TryQuarantine(_paths.WorkspaceFile);
                return new CoworkWorkspace();
            }
        }
    }

    public void Save(CoworkWorkspace workspace)
    {
        lock (_gate)
        {
            SnapshotBeforeSave();

            var json = JsonSerializer.Serialize(Normalize(workspace), SerializerOptions);
            var temp = _paths.WorkspaceFile + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(_paths.WorkspaceFile))
                File.Replace(temp, _paths.WorkspaceFile, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temp, _paths.WorkspaceFile);
        }
    }

    /// <summary>
    /// Chụp lại workspace một lần mỗi ngày trước khi ghi đè.
    /// Đây là lưới an toàn cho trường hợp file bị hỏng hoặc bị ghi đè bởi thứ khác:
    /// bản chụp giữ nguyên trạng thái đầu ngày, chưa dính thay đổi của hôm nay.
    /// </summary>
    private void SnapshotBeforeSave()
    {
        if (!File.Exists(_paths.WorkspaceFile))
            return;

        var snapshot = _paths.WorkspaceBackupFile(DateTime.Now);
        if (File.Exists(snapshot))
            return;

        try
        {
            Directory.CreateDirectory(_paths.BackupDirectory);
            File.Copy(_paths.WorkspaceFile, snapshot);
            PruneSnapshots();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Không sao lưu được thì cũng không được chặn việc lưu.
            _logger.Warning("Không tạo được bản sao workspace: " + ex.Message);
        }
    }

    /// <summary>Giữ lại <see cref="SnapshotsToKeep"/> bản chụp gần nhất.</summary>
    private void PruneSnapshots()
    {
        var snapshots = Directory.GetFiles(_paths.BackupDirectory, "workspace-*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(SnapshotsToKeep)
            .ToList();

        foreach (var stale in snapshots)
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
            }
        }
    }

    private const int SnapshotsToKeep = 10;

    /// <summary>Vá các trường null do file cũ hoặc do người dùng sửa tay.</summary>
    private static CoworkWorkspace Normalize(CoworkWorkspace workspace)
    {
        workspace.Apps ??= new List<ManagedApp>();
        workspace.Settings ??= new WorkspaceSettings();

        var order = 0;
        foreach (var app in workspace.Apps)
        {
            // Tên biến môi trường trên Windows không phân biệt hoa thường; dựng lại
            // dictionary vì bộ deserialize luôn trả về comparer mặc định.
            app.EnvironmentVariables = app.EnvironmentVariables is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(app.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
            app.ConfigFiles ??= new List<ConfigFileRef>();
            app.Schedule ??= new ScheduleRule();
            app.Schedule.Times ??= new List<TimeSpan>();
            app.Schedule.DaysOfWeek ??= new List<DayOfWeek>();
            app.HealthCheck ??= new HealthCheck();
            app.HealthCheck.Target ??= string.Empty;
            app.HealthCheck.FailurePatterns ??= new List<string>();

            // File cũ không có trường này, hoặc người dùng sửa tay thành mảng rỗng: quay về mặc định 0.
            app.SuccessExitCodes = ExitCodes.Normalize(app.SuccessExitCodes);

            if (app.Id == Guid.Empty)
                app.Id = Guid.NewGuid();

            app.Order = order++;
        }

        return workspace;
    }

    private void TryQuarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), overwrite: true);
        }
        catch (IOException)
        {
            // Bỏ qua: không đổi tên được thì cũng không nên chặn khởi động.
        }
    }
}
