using Cowork.Core.Models;
using Cowork.Core.Services;
using Cowork.Remote.Contracts;

namespace Cowork.Remote;

/// <summary>Dựng ảnh chụp từ model + trạng thái lúc chạy. Hàm thuần, nhận <c>now</c> để tính mốc kế tiếp.</summary>
public static class SnapshotBuilder
{
    public static AppSnapshot Build(
        ManagedApp app, AppRuntimeState state, int processId, string? lastError, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new AppSnapshot(
            app.Id,
            app.Name,
            app.Group,
            app.Enabled,
            app.KeepAlive,
            state,
            processId,
            app.LastRunAt,
            app.LastExitCode,
            ScheduleEvaluator.NextRun(app, now),
            app.Schedule.Describe(),
            lastError);
    }
}
