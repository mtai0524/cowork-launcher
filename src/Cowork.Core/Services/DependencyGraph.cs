using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.Validation;

namespace Cowork.Core.Services;

/// <summary>
/// Đồ thị phụ thuộc giữa các app, toàn hàm thuần: sắp thứ tự chạy, dò vòng lặp, và kiểm tra những
/// khai báo không bao giờ thoả được. Tách khỏi <see cref="RunQueue"/> để phần dễ sai nhất — thứ tự
/// và vòng lặp — kiểm thử được mà không cần tiến trình nào.
/// </summary>
public static class DependencyGraph
{
    /// <summary>Các phụ thuộc thật sự dùng được của một app: bỏ tự trỏ và trùng lặp.</summary>
    public static IReadOnlyList<AppDependency> EdgesOf(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var seen = new HashSet<Guid>();
        var edges = new List<AppDependency>();

        foreach (var edge in app.DependsOn)
        {
            if (edge is null || edge.AppId == Guid.Empty || edge.AppId == app.Id)
                continue;
            if (!seen.Add(edge.AppId))
                continue;

            edges.Add(edge);
        }

        return edges;
    }

    /// <summary>
    /// Thứ tự chạy: app nào cũng đứng sau mọi app nó phụ thuộc. Các app nằm trong vòng lặp bị bỏ ra
    /// (trả về ở <paramref name="cyclic"/>) vì không có thứ tự nào đúng cho chúng.
    /// Giữ nguyên thứ tự người dùng sắp cho những app không ràng buộc nhau.
    /// </summary>
    public static IReadOnlyList<ManagedApp> TopologicalOrder(
        IReadOnlyList<ManagedApp> apps, out IReadOnlyList<ManagedApp> cyclic)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var byId = apps.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var pending = apps.ToList();
        var ordered = new List<ManagedApp>(apps.Count);
        var placed = new HashSet<Guid>();

        // Kahn: mỗi vòng lấy ra mọi app đã đủ phụ thuộc, theo đúng thứ tự danh sách gốc. Vòng nào
        // không lấy được gì nữa nghĩa là phần còn lại đang chờ nhau.
        while (pending.Count > 0)
        {
            var ready = pending
                .Where(app => EdgesOf(app).All(e => !byId.ContainsKey(e.AppId) || placed.Contains(e.AppId)))
                .ToList();

            if (ready.Count == 0)
                break;

            foreach (var app in ready)
            {
                ordered.Add(app);
                placed.Add(app.Id);
                pending.Remove(app);
            }
        }

        cyclic = pending;
        return ordered;
    }

    /// <summary>App nào phụ thuộc (trực tiếp) vào <paramref name="appId"/>.</summary>
    public static IReadOnlyList<ManagedApp> DependentsOf(IReadOnlyList<ManagedApp> apps, Guid appId)
    {
        ArgumentNullException.ThrowIfNull(apps);
        return apps.Where(a => EdgesOf(a).Any(e => e.AppId == appId)).ToList();
    }

    /// <summary>Có nằm trong một vòng lặp phụ thuộc nào không.</summary>
    public static bool IsInCycle(IReadOnlyList<ManagedApp> apps, Guid appId)
    {
        TopologicalOrder(apps, out var cyclic);
        return cyclic.Any(a => a.Id == appId);
    }

    /// <summary>
    /// Kiểm tra toàn bộ khai báo phụ thuộc của một workspace. Cần cả danh sách vì một app không tự
    /// biết app nó trỏ tới có tồn tại hay không — <see cref="AppValidator"/> chỉ nhìn được một app.
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<ManagedApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var issues = new List<ValidationIssue>();
        var byId = apps.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        const string field = nameof(ManagedApp.DependsOn);

        foreach (var app in apps)
        {
            foreach (var edge in EdgesOf(app))
            {
                if (!byId.TryGetValue(edge.AppId, out var target))
                {
                    issues.Add(new ValidationIssue(field, Loc.T("Val.DependencyUnknown", app.Name), true));
                    continue;
                }

                if (!target.Enabled)
                {
                    issues.Add(new ValidationIssue(field,
                        Loc.T("Val.DependencyDisabled", app.Name, target.Name), false));
                }

                // App giữ luôn chạy không bao giờ "xong việc": chờ nó kết thúc là chờ mãi.
                if (edge.Wait == DependencyWait.Completed && target.KeepAlive)
                {
                    issues.Add(new ValidationIssue(field,
                        Loc.T("Val.DependencyKeepAliveCompleted", app.Name, target.Name), true));
                }
            }
        }

        TopologicalOrder(apps, out var cyclic);
        if (cyclic.Count > 0)
        {
            issues.Add(new ValidationIssue(field,
                Loc.T("Val.DependencyCycle", string.Join(" → ", cyclic.Select(a => a.Name))), true));
        }

        return issues;
    }
}
