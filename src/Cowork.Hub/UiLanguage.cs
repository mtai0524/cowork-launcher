using Cowork.Core.Localization;

namespace Cowork.Hub;

/// <summary>
/// Ngôn ngữ của một phiên trình duyệt. Desktop dùng <see cref="Loc.Current"/> tĩnh vì chỉ
/// có một người dùng; server phục vụ nhiều người nên mỗi phiên (scoped) giữ lựa chọn riêng
/// và tra bảng bằng bản nhận ngôn ngữ tường minh.
/// </summary>
public sealed class UiLanguage
{
    public AppLanguage Current { get; private set; } = AppLanguage.Vietnamese;

    public event Action? Changed;

    public string T(string key) => Loc.T(Current, key);

    public string T(string key, params object?[] args) => Loc.T(Current, key, args);

    public void Toggle()
    {
        Current = Current == AppLanguage.Vietnamese ? AppLanguage.English : AppLanguage.Vietnamese;
        Changed?.Invoke();
    }
}
