using Cowork.Core.Localization;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Vì sao một app trong lượt chạy bị bỏ qua.</summary>
public enum QueueSkipReason
{
    /// <summary>App nó phụ thuộc đã lỗi hoặc cũng bị bỏ qua.</summary>
    DependencyFailed,

    /// <summary>App nó phụ thuộc không nằm trong lượt chạy và cũng không đang chạy sẵn.</summary>
    DependencyMissing,

    /// <summary>Nằm trong một vòng lặp phụ thuộc — không có thứ tự nào chạy được.</summary>
    DependencyCycle,

    /// <summary>Chờ "xong việc" ở một app giữ luôn chạy: nó không bao giờ xong.</summary>
    DependencyNeverCompletes,

    /// <summary>Không khởi chạy được (cấu hình lỗi, Windows từ chối…).</summary>
    CannotStart,
}

public sealed record QueueSkippedEventArgs(ManagedApp App, QueueSkipReason Reason, string Message);

public sealed record QueueFinishedEventArgs(int Started, int Succeeded, int Failed, int Skipped);

/// <summary>
/// Chạy một lượt nhiều app theo đúng thứ tự phụ thuộc: app chỉ được khởi chạy khi mọi app nó phụ
/// thuộc đã sẵn sàng — chạy xong và thành công (<see cref="DependencyWait.Completed"/>), hoặc đã
/// lên (<see cref="DependencyWait.Running"/>). Phụ thuộc lỗi thì các app phía sau bị bỏ qua chứ
/// không chạy vào khoảng không.
///
/// Khác các supervisor khác, hàng đợi cần <em>biết kết quả</em> của lệnh khởi chạy nên nhận một
/// <c>runner</c> thay vì phát sự kiện: tầng ứng dụng vẫn là nơi quyết định chạy thế nào, hàng đợi
/// chỉ hỏi "khởi chạy được không". Toàn bộ thứ tự nằm ở <see cref="DependencyGraph"/>, hàm thuần.
/// </summary>
public sealed class RunQueue : IDisposable
{
    private readonly IProcessManager _processManager;
    private readonly Func<ManagedApp, bool> _runner;
    private readonly ICoworkLogger _logger;
    private readonly object _gate = new();

    private readonly Dictionary<Guid, Entry> _entries = new();
    private List<Entry> _order = new();
    private bool _pumping;
    private bool _pumpAgain;
    private bool _disposed;

    /// <param name="runner">Khởi chạy một app, trả về false nếu không chạy được. Được gọi từ luồng của sự kiện tiến trình.</param>
    public RunQueue(IProcessManager processManager, Func<ManagedApp, bool> runner, ICoworkLogger logger)
    {
        _processManager = processManager;
        _runner = runner;
        _logger = logger;

        _processManager.StatusChanged += OnStatusChanged;
        _processManager.RunCompleted += OnRunCompleted;
    }

    /// <summary>Một app bị bỏ qua, kèm lý do đọc được.</summary>
    public event EventHandler<QueueSkippedEventArgs>? Skipped;

    /// <summary>Cả lượt đã xong: không còn app nào đang chờ hay đang chạy.</summary>
    public event EventHandler<QueueFinishedEventArgs>? Finished;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _order.Any(e => e.State is EntryState.Waiting or EntryState.Started);
        }
    }

    /// <summary>Các app còn đang chờ phụ thuộc.</summary>
    public IReadOnlyList<Guid> Waiting
    {
        get
        {
            lock (_gate)
                return _order.Where(e => e.State == EntryState.Waiting).Select(e => e.App.Id).ToList();
        }
    }

    /// <summary>
    /// Bắt đầu một lượt chạy. Lượt trước (nếu còn) bị bỏ. <paramref name="batch"/> theo thứ tự người
    /// dùng sắp; hàng đợi sắp lại theo phụ thuộc và giữ nguyên thứ tự cũ cho những app không ràng buộc.
    /// </summary>
    public void Start(IReadOnlyList<ManagedApp> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var skips = new List<QueueSkippedEventArgs>();

        lock (_gate)
        {
            if (_disposed)
                return;

            _entries.Clear();
            _order = new List<Entry>();

            var ordered = DependencyGraph.TopologicalOrder(batch, out var cyclic);

            foreach (var app in ordered.Concat(cyclic))
            {
                var entry = new Entry(app);
                _entries[app.Id] = entry;
                _order.Add(entry);
            }

            // Vòng lặp phụ thuộc: không có thứ tự nào đúng, bỏ qua ngay thay vì treo mãi.
            foreach (var app in cyclic)
            {
                skips.Add(Refuse(_entries[app.Id], QueueSkipReason.DependencyCycle,
                    Loc.T("Queue.SkipCycle", app.Name)));
            }
        }

        foreach (var skip in skips)
            RaiseSkipped(skip);

        Pump();
    }

    /// <summary>Bỏ lượt đang chạy (người dùng bấm Dừng tất cả). Không đụng tới tiến trình đang chạy.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order = new List<Entry>();
        }
    }

    private void OnStatusChanged(object? sender, AppStatusChanged e)
    {
        if (e.State != AppRuntimeState.Running)
            return;

        lock (_gate)
        {
            if (!_entries.TryGetValue(e.AppId, out var entry) || entry.State != EntryState.Started)
                return;

            entry.Live = true;
        }

        Pump();
    }

    private void OnRunCompleted(object? sender, AppRunRecord record)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(record.AppId, out var entry) || entry.State != EntryState.Started)
                return;

            if (record.Trigger == RunTrigger.Retry)
                entry.RetriesSeen++;

            var app = entry.App;
            var succeeded = record.Outcome == RunOutcome.Succeeded;

            // Bộ thử lại sẽ chạy lại app này, nên chưa kết luận: chờ tới lần cuối của chuỗi.
            if (!succeeded
                && RetryPolicy.AppliesTo(app)
                && RetryPolicy.IsRetryable(record.Outcome)
                && entry.RetriesSeen < app.RetryCount)
            {
                return;
            }

            entry.State = succeeded ? EntryState.Succeeded : EntryState.Failed;
        }

        Pump();
    }

    /// <summary>
    /// Xét lại toàn bộ hàng đợi: khởi chạy những app đã đủ phụ thuộc, bỏ qua những app không bao giờ
    /// đủ. Chạy ngoài khoá vì <c>runner</c> có thể quay lại đây ngay (ProcessManager phát sự kiện
    /// "đang chạy" ngay trong lệnh khởi chạy).
    /// </summary>
    private void Pump()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            if (_pumping)
            {
                _pumpAgain = true;
                return;
            }

            _pumping = true;
        }

        try
        {
            while (true)
            {
                var skips = new List<QueueSkippedEventArgs>();
                var toStart = new List<Entry>();
                QueueFinishedEventArgs? finished = null;

                lock (_gate)
                {
                    _pumpAgain = false;

                    foreach (var entry in _order.Where(e => e.State == EntryState.Waiting))
                    {
                        var readiness = Evaluate(entry);
                        if (readiness.Skip is { } skip)
                            skips.Add(Refuse(entry, skip.Reason, skip.Message));
                        else if (readiness.Ready)
                            toStart.Add(entry);
                    }

                    foreach (var entry in toStart)
                        entry.State = EntryState.Started;

                    if (toStart.Count == 0 && skips.Count == 0 && _order.Count > 0
                        && _order.All(e => e.State is not (EntryState.Waiting or EntryState.Started)))
                    {
                        finished = Summarize();
                        _entries.Clear();
                        _order = new List<Entry>();
                    }
                }

                foreach (var skip in skips)
                    RaiseSkipped(skip);

                foreach (var entry in toStart)
                    StartEntry(entry);

                if (finished is not null)
                    Finished?.Invoke(this, finished);

                lock (_gate)
                {
                    // Một app bị bỏ qua kéo theo những app chờ nó, và chỉ khi cả lượt đứng yên mới
                    // tổng kết được — nên vòng nào có động tĩnh thì phải xét lại thêm một vòng nữa.
                    if (skips.Count == 0 && toStart.Count == 0 && !_pumpAgain)
                    {
                        _pumping = false;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
                _pumping = false;

            // Chạy trên luồng của sự kiện tiến trình; lỗi lọt ra ngoài sẽ làm sập cả Cowork.
            _logger.Error("Lỗi trong hàng đợi chạy theo phụ thuộc.", ex);
        }
    }

    private void StartEntry(Entry entry)
    {
        // Instance cũ còn sống (thường do "không chạy chồng"): coi như đã lên, chờ nó kết thúc.
        if (_processManager.IsRunning(entry.App.Id))
        {
            lock (_gate)
                entry.Live = true;
            return;
        }

        bool started;
        try
        {
            started = _runner(entry.App);
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi khởi chạy '{entry.App.Name}' từ hàng đợi.", ex);
            started = false;
        }

        if (started)
            return;

        // Không chạy được thì cũng không có RunCompleted nào tới: tự khép lại, nếu không các app
        // phía sau sẽ chờ mãi.
        QueueSkippedEventArgs skip;
        lock (_gate)
        {
            if (entry.State != EntryState.Started)
                return;

            skip = Refuse(entry, QueueSkipReason.CannotStart, Loc.T("Queue.SkipCannotStart", entry.App.Name));
        }

        RaiseSkipped(skip);
    }

    /// <summary>Xét một app đang chờ: đã đủ phụ thuộc chưa, hay đã chắc chắn không bao giờ đủ.</summary>
    private Readiness Evaluate(Entry entry)
    {
        foreach (var edge in DependencyGraph.EdgesOf(entry.App))
        {
            if (!_entries.TryGetValue(edge.AppId, out var target))
            {
                // Ngoài lượt chạy: chỉ chấp nhận khi nó đã sống sẵn và ta chỉ cần nó "đang chạy".
                if (edge.Wait == DependencyWait.Running && _processManager.IsRunning(edge.AppId))
                    continue;

                return Readiness.Refuse(QueueSkipReason.DependencyMissing,
                    Loc.T("Queue.SkipMissing", entry.App.Name));
            }

            if (edge.Wait == DependencyWait.Completed && target.App.KeepAlive)
            {
                return Readiness.Refuse(QueueSkipReason.DependencyNeverCompletes,
                    Loc.T("Queue.SkipNeverCompletes", entry.App.Name, target.App.Name));
            }

            switch (target.State)
            {
                case EntryState.Failed or EntryState.Skipped:
                    return Readiness.Refuse(QueueSkipReason.DependencyFailed,
                        Loc.T("Queue.SkipDependencyFailed", entry.App.Name, target.App.Name));

                case EntryState.Succeeded:
                    continue;

                // Đã lên rồi thì phụ thuộc kiểu "chỉ cần nó chạy" coi như xong.
                case EntryState.Started when edge.Wait == DependencyWait.Running && target.Live:
                    continue;

                default:
                    return Readiness.Wait;
            }
        }

        return Readiness.Go;
    }

    private QueueSkippedEventArgs Refuse(Entry entry, QueueSkipReason reason, string message)
    {
        entry.State = EntryState.Skipped;
        return new QueueSkippedEventArgs(entry.App, reason, message);
    }

    private void RaiseSkipped(QueueSkippedEventArgs args)
    {
        _logger.Warning($"Bỏ qua '{args.App.Name}' trong lượt chạy: {args.Message}");
        Skipped?.Invoke(this, args);
    }

    private QueueFinishedEventArgs Summarize()
    {
        var succeeded = _order.Count(e => e.State == EntryState.Succeeded);
        var failed = _order.Count(e => e.State == EntryState.Failed);
        var skipped = _order.Count(e => e.State == EntryState.Skipped);

        return new QueueFinishedEventArgs(succeeded + failed, succeeded, failed, skipped);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;

            _entries.Clear();
            _order = new List<Entry>();
        }

        _processManager.StatusChanged -= OnStatusChanged;
        _processManager.RunCompleted -= OnRunCompleted;
    }

    private enum EntryState
    {
        Waiting,
        Started,
        Succeeded,
        Failed,
        Skipped,
    }

    private sealed record Readiness(bool Ready, (QueueSkipReason Reason, string Message)? Skip)
    {
        public static readonly Readiness Go = new(true, null);
        public static readonly Readiness Wait = new(false, null);

        public static Readiness Refuse(QueueSkipReason reason, string message) => new(false, (reason, message));
    }

    private sealed class Entry
    {
        public Entry(ManagedApp app) => App = app;

        public ManagedApp App { get; }
        public EntryState State { get; set; } = EntryState.Waiting;

        /// <summary>Đã thấy tiến trình lên thật (không chỉ là đã ra lệnh chạy).</summary>
        public bool Live { get; set; }

        /// <summary>Số lần chạy lại của bộ thử lại đã thấy trong lượt này.</summary>
        public int RetriesSeen { get; set; }
    }
}
