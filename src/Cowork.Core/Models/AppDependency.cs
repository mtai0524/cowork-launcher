namespace Cowork.Core.Models;

/// <summary>Chờ app phụ thuộc tới mức nào thì coi là xong.</summary>
public enum DependencyWait
{
    /// <summary>Chờ nó chạy xong và thành công. Dành cho job: sao lưu xong mới nén, nén xong mới gửi.</summary>
    Completed = 0,

    /// <summary>Chỉ chờ nó lên. Dành cho dịch vụ: app giữ luôn chạy không bao giờ "xong việc".</summary>
    Running = 1,
}

/// <summary>Một app phải sẵn sàng trước khi app này được chạy trong lượt "Chạy tất cả".</summary>
public sealed class AppDependency
{
    /// <summary><see cref="ManagedApp.Id"/> của app phải chờ.</summary>
    public Guid AppId { get; set; }

    public DependencyWait Wait { get; set; } = DependencyWait.Completed;

    public AppDependency Clone() => new() { AppId = AppId, Wait = Wait };
}
