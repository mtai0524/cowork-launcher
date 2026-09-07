namespace Cowork.Core.Models;

/// <summary>
/// Sự kiện của máy mà Cowork có thể lấy làm mốc chạy app. Cố ý chỉ nhận ba thứ chắc chắn quan sát
/// được trên Windows mà không cần quyền admin.
/// </summary>
public enum SystemEventKind
{
    /// <summary>Máy vừa thức dậy sau khi ngủ hoặc ngủ đông.</summary>
    Resume = 0,

    /// <summary>Người dùng vừa mở khoá màn hình.</summary>
    SessionUnlock = 1,

    /// <summary>Máy vừa có mạng trở lại.</summary>
    NetworkAvailable = 2,
}
