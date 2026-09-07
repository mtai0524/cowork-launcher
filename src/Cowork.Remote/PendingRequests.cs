using System.Collections.Concurrent;

namespace Cowork.Remote;

/// <summary>Kết cục một lần hỏi agent: có trả lời, im lặng tới hết hạn, hay không gửi được.</summary>
internal sealed record PendingOutcome<T>(T? Result, Exception? SendError)
    where T : class
{
    public static readonly PendingOutcome<T> TimedOut = new(null, null);

    public static PendingOutcome<T> Answered(T result) => new(result, null);

    public static PendingOutcome<T> Failed(Exception error) => new(null, error);
}

/// <summary>
/// Các yêu cầu đã gửi xuống agent và đang chờ trả lời, ghép với nhau bằng mã yêu cầu.
///
/// Hub hỏi agent nhiều thứ — chạy lệnh, chụp màn hình, đọc log — và tất cả đều cùng một
/// hình dạng: gửi đi, chờ tối đa ngần này, hết giờ thì bỏ. Gom vào một chỗ để mỗi loại
/// yêu cầu mới không phải chép lại đoạn ghép mã và dọn dẹp, vốn là chỗ dễ rò rỉ mục chờ.
/// </summary>
internal sealed class PendingRequests<T>
    where T : class
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<T>> _pending = new();

    /// <summary>Agent đã trả lời. Trả lời cho yêu cầu không còn chờ (đã quá hạn) bị bỏ qua.</summary>
    public bool Complete(Guid requestId, T result)
        => _pending.TryGetValue(requestId, out var completion) && completion.TrySetResult(result);

    /// <summary>
    /// Gửi rồi chờ. Ba kết cục phải phân biệt được: agent trả lời, agent im lặng tới hết hạn,
    /// và không gửi đi được. Gộp hai cái sau lại thì web báo "máy không phản hồi" cho cả trường
    /// hợp mạng đứt ngay lúc gửi, khiến người dùng đi tìm sai chỗ.
    /// </summary>
    public async Task<PendingOutcome<T>> SendAndWaitAsync(
        Guid requestId, Func<Task> send, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        try
        {
            await send().ConfigureAwait(false);

            var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken))
                .ConfigureAwait(false);

            return finished == completion.Task
                ? PendingOutcome<T>.Answered(await completion.Task.ConfigureAwait(false))
                : PendingOutcome<T>.TimedOut;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PendingOutcome<T>.Failed(ex);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }
}
