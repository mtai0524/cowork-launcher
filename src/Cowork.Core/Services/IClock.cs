namespace Cowork.Core.Services;

/// <summary>Bọc đồng hồ hệ thống để unit-test lịch chạy không phụ thuộc giờ thật.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>Đồng hồ giả cho test: thời gian chỉ đổi khi được set.</summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public void Advance(TimeSpan by) => Now += by;
}
