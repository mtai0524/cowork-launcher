using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cowork.Core.Localization;
using Cowork.Core.Models;
using Cowork.Core.News;
using Cowork.Core.Services;

namespace Cowork.App.ViewModels;

/// <summary>
/// Thẻ Tin tức: chọn chủ đề, lấy tin, và mở bài ở trình duyệt.
///
/// Toàn bộ phần khó — đọc feed, gộp trùng, cân tin trong nước với tin nước ngoài — nằm ở
/// <see cref="Cowork.Core.News"/>; chỗ này chỉ nối nó vào giao diện và đưa sự kiện từ luồng
/// nền về luồng WPF.
/// </summary>
public sealed partial class NewsViewModel : ObservableObject, IDisposable
{
    /// <summary>Nhịp kiểm tra: đủ dày để nhãn "20 phút trước" không đứng yên, đủ rẻ để chạy cả ngày.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly NewsSettings _settings;
    private readonly NewsService _service;
    private readonly IClock _clock;
    private readonly Dispatcher _dispatcher;
    private readonly ICoworkLogger _logger;
    private readonly Action _markDirty;
    private readonly DispatcherTimer _timer;

    private bool _loading;
    private bool _disposed;

    public NewsViewModel(
        NewsSettings settings,
        NewsService service,
        IClock clock,
        Dispatcher dispatcher,
        ICoworkLogger logger,
        Action markDirty)
    {
        _settings = settings;
        _service = service;
        _clock = clock;
        _dispatcher = dispatcher;
        _logger = logger;
        _markDirty = markDirty;

        _loading = true;

        _enabled = settings.Enabled;
        _includeVietnam = settings.IncludeVietnam;
        _vietnamPercent = settings.VietnamPercent;
        _maxItems = settings.MaxItems;
        _maxAgeDays = settings.MaxAgeDays;
        _maxPerSource = settings.MaxPerSource;
        _refreshMinutes = settings.RefreshMinutes;

        Topics = new ObservableCollection<NewsTopicViewModel>(
            Enum.GetValues<NewsTopic>().Select(topic =>
            {
                var vm = new NewsTopicViewModel(topic) { IsSelected = settings.Topics.Contains(topic) };
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(NewsTopicViewModel.IsSelected))
                        OnTopicsChanged();
                };
                return vm;
            }));

        Sources = new ObservableCollection<NewsSourceViewModel>();
        Stories = new ObservableCollection<NewsItemViewModel>();

        RegionOptions = Enum.GetValues<NewsRegion>()
            .Select(r => new ChoiceViewModel<NewsRegion>(r, x => Loc.T("Region." + x)))
            .ToList();
        TopicOptions = Enum.GetValues<NewsTopic>()
            .Select(t => new ChoiceViewModel<NewsTopic>(t, x => Loc.T("Topic." + x)))
            .ToList();

        _newSourceRegion = RegionOptions[0];
        _newSourceTopic = TopicOptions[0];

        RebuildSources();

        // Tin của lần chạy trước hiện ra ngay, trước khi lần tải đầu tiên kịp xong.
        _service.LoadCache();
        _service.Refreshed += OnServiceRefreshed;

        _loading = false;

        Rebuild();

        _timer = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => OnTick(), dispatcher);
        _timer.Start();
    }

    /// <summary>Chủ đề bày thành các ô tick trên thẻ Tin tức.</summary>
    public ObservableCollection<NewsTopicViewModel> Topics { get; }

    /// <summary>Nguồn bày thành danh sách bật/tắt trong thẻ Thiết lập.</summary>
    public ObservableCollection<NewsSourceViewModel> Sources { get; }

    /// <summary>Bảng tin đã chọn xong, sắp mới nhất trước.</summary>
    public ObservableCollection<NewsItemViewModel> Stories { get; }

    public IReadOnlyList<ChoiceViewModel<NewsRegion>> RegionOptions { get; }

    public IReadOnlyList<ChoiceViewModel<NewsTopic>> TopicOptions { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _includeVietnam;
    [ObservableProperty] private int _vietnamPercent;
    [ObservableProperty] private int _maxItems;
    [ObservableProperty] private int _maxAgeDays;
    [ObservableProperty] private int _maxPerSource;
    [ObservableProperty] private int _refreshMinutes;

    [ObservableProperty] private bool _isRefreshing;

    /// <summary>Một dòng nói lần lấy gần nhất là khi nào và có nguồn nào hỏng không.</summary>
    [ObservableProperty] private string _statusText = Loc.T("News.NeverUpdated");

    [ObservableProperty] private string _countText = string.Empty;

    /// <summary>Lời nhắn thay cho danh sách rỗng: chưa chọn chủ đề, hay chỉ là chưa tải.</summary>
    [ObservableProperty] private string _emptyText = Loc.T("News.Empty");

    // ---------- Thêm feed riêng ----------

    [ObservableProperty] private string _newSourceName = string.Empty;
    [ObservableProperty] private string _newSourceUrl = string.Empty;
    [ObservableProperty] private ChoiceViewModel<NewsRegion> _newSourceRegion;
    [ObservableProperty] private ChoiceViewModel<NewsTopic> _newSourceTopic;
    [ObservableProperty] private string _addSourceError = string.Empty;

    /// <summary>Lấy tin ngay khi mở app, nếu bảng tin đang bật và tin đã cũ.</summary>
    public void Start()
    {
        if (Enabled && IsStale())
            _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing || !Enabled)
            return;

        var sources = NewsCatalog.Resolve(_settings);
        if (sources.Count == 0)
        {
            Rebuild();
            return;
        }

        IsRefreshing = true;
        UpdateStatus();

        try
        {
            await _service.RefreshAsync(sources).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Lấy tin hỏng không được làm phiền người đang làm việc khác — ghi log rồi thôi.
            _logger.Error("Không lấy được bảng tin.", ex);
        }
        finally
        {
            IsRefreshing = false;
            Rebuild();
        }
    }

    /// <summary>Mở bài ở trình duyệt mặc định.</summary>
    [RelayCommand]
    private void Open(NewsItemViewModel? story)
    {
        if (story is null)
            return;

        // Bộ đọc feed đã loại mọi scheme khác http/https; kiểm lại ở đây vì đây là chỗ
        // chuỗi biến thành một lệnh chạy của Windows, và nó rẻ.
        if (!Uri.TryCreate(story.Link, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.Error("Không mở được bài trên trình duyệt.", ex);
        }
    }

    [RelayCommand]
    private void AddSource()
    {
        var candidate = new CustomNewsSource
        {
            Name = NewSourceName,
            FeedUrl = NewSourceUrl,
            Region = NewSourceRegion.Value,
            Topics = { NewSourceTopic.Value },
        };

        if (candidate.ToSource() is null)
        {
            AddSourceError = Loc.T("Msg.NewsSourceInvalid");
            return;
        }

        AddSourceError = string.Empty;
        _settings.CustomSources.Add(candidate);
        NewSourceName = string.Empty;
        NewSourceUrl = string.Empty;

        RebuildSources();
        _markDirty();

        _ = RefreshAsync();
    }

    [RelayCommand]
    private void RemoveSource(NewsSourceViewModel? source)
    {
        if (source is not { IsCustom: true })
            return;

        _settings.CustomSources.RemoveAll(c => c.ToSource()?.Id == source.Id);
        _settings.DisabledSourceIds.Remove(source.Id);

        RebuildSources();
        Rebuild();
        _markDirty();
    }

    /// <summary>Đổi ngôn ngữ xong, các nhãn đã sinh sẵn phải được dựng lại.</summary>
    public void RefreshLabels()
    {
        foreach (var topic in Topics)
            topic.RefreshLabel();

        foreach (var source in Sources)
            source.RefreshLabels();

        foreach (var option in RegionOptions)
            option.RefreshLabel();

        foreach (var option in TopicOptions)
            option.RefreshLabel();

        Rebuild();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _service.Refreshed -= OnServiceRefreshed;
    }

    // ---------- Thiết lập đổi ----------

    partial void OnEnabledChanged(bool value) => Persist(() =>
    {
        _settings.Enabled = value;

        if (value)
            Start();
        else
            Rebuild();
    });

    partial void OnIncludeVietnamChanged(bool value) => Persist(() =>
    {
        _settings.IncludeVietnam = value;
        Rebuild();

        // Bật lại tin trong nước thì phải đi lấy: những nguồn đó vừa nãy không được tải.
        if (value)
            _ = RefreshAsync();
    });

    partial void OnVietnamPercentChanged(int value) => Persist(() =>
    {
        _settings.VietnamPercent = Math.Clamp(value, 0, 100);
        Rebuild();
    });

    partial void OnMaxItemsChanged(int value) => Persist(() =>
    {
        _settings.MaxItems = Math.Clamp(value, 1, 500);
        Rebuild();
    });

    partial void OnMaxAgeDaysChanged(int value) => Persist(() =>
    {
        _settings.MaxAgeDays = Math.Clamp(value, 1, 90);
        Rebuild();
    });

    partial void OnMaxPerSourceChanged(int value) => Persist(() =>
    {
        _settings.MaxPerSource = Math.Clamp(value, 1, 100);
        Rebuild();
    });

    partial void OnRefreshMinutesChanged(int value) => Persist(() => _settings.RefreshMinutes = Math.Max(0, value));

    private void OnTopicsChanged() => Persist(() =>
    {
        _settings.Topics = Topics.Where(t => t.IsSelected).Select(t => t.Topic).ToList();
        RebuildSources();
        Rebuild();

        _ = RefreshAsync();
    });

    /// <summary>Bật/tắt một nguồn trong thẻ Thiết lập.</summary>
    private void OnSourceEnabledChanged(NewsSourceViewModel source) => Persist(() =>
    {
        if (source.IsEnabled)
            _settings.DisabledSourceIds.Remove(source.Id);
        else if (!_settings.DisabledSourceIds.Contains(source.Id))
            _settings.DisabledSourceIds.Add(source.Id);

        Rebuild();

        if (source.IsEnabled)
            _ = RefreshAsync();
    });

    /// <summary>
    /// Chạy một thay đổi thiết lập rồi đánh dấu workspace bẩn — trừ lúc đang dựng view-model,
    /// khi mọi thuộc tính vừa được gán từ chính file vừa đọc lên.
    /// </summary>
    private void Persist(Action change)
    {
        if (_loading)
            return;

        change();
        _markDirty();
    }

    // ---------- Dựng lại ----------

    private bool IsStale()
        => RefreshMinutes > 0
           && _service.IsStale(_clock.Now, TimeSpan.FromMinutes(RefreshMinutes));

    private void OnTick()
    {
        var now = _clock.Now;

        foreach (var story in Stories)
            story.UpdateWhen(now);

        UpdateStatus();

        if (Enabled && !IsRefreshing && IsStale())
            _ = RefreshAsync();
    }

    /// <summary><see cref="NewsService"/> bắn sự kiện từ luồng tải — mọi thứ chạm view-model phải về luồng giao diện.</summary>
    private void OnServiceRefreshed(object? sender, NewsRefreshResult e)
        => _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                Rebuild();
        });

    private void Rebuild()
    {
        var now = _clock.Now;

        Stories.Clear();

        if (Enabled)
        {
            foreach (var item in NewsDigest.Build(_service.Items, _settings.ToDigestOptions(), now))
                Stories.Add(new NewsItemViewModel(item, now));
        }

        CountText = Loc.T("News.CountSuffix", Stories.Count);
        EmptyText = !Enabled
            ? Loc.T("News.Off")
            : Topics.Any(t => t.IsSelected) ? Loc.T("News.Empty") : Loc.T("News.NoTopic");

        UpdateStatus();
    }

    private void RebuildSources()
    {
        var disabled = _settings.DisabledSourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtIn = NewsCatalog.BuiltIn.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Sources.Clear();

        foreach (var source in NewsCatalog.All(_settings))
        {
            var vm = new NewsSourceViewModel(source, isCustom: !builtIn.Contains(source.Id))
            {
                IsEnabled = !disabled.Contains(source.Id),
            };

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NewsSourceViewModel.IsEnabled) && s is NewsSourceViewModel changed)
                    OnSourceEnabledChanged(changed);
            };

            Sources.Add(vm);
        }
    }

    private void UpdateStatus()
    {
        if (IsRefreshing)
        {
            StatusText = Loc.T("News.Refreshing");
            return;
        }

        var text = _service.LastRefreshedAt is { } at
            ? Loc.T("News.UpdatedAt", NewsTime.Describe(at, _clock.Now))
            : Loc.T("News.NeverUpdated");

        var failed = _service.FailedSources;
        if (failed.Count > 0)
        {
            text += "  •  " + (Stories.Count == 0 && _service.Items.Count == 0
                ? Loc.T("News.AllSourcesFailed")
                : Loc.T("News.SomeSourcesFailed", failed.Count, string.Join(", ", failed)));
        }

        StatusText = text;
    }
}

/// <summary>Một ô tick chủ đề.</summary>
public sealed partial class NewsTopicViewModel : ObservableObject
{
    public NewsTopicViewModel(NewsTopic topic) => Topic = topic;

    public NewsTopic Topic { get; }

    public string Label => Loc.T("Topic." + Topic);

    [ObservableProperty] private bool _isSelected;

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>Một dòng trong danh sách nguồn của thẻ Thiết lập.</summary>
public sealed partial class NewsSourceViewModel : ObservableObject
{
    private readonly NewsSource _source;

    public NewsSourceViewModel(NewsSource source, bool isCustom)
    {
        _source = source;
        IsCustom = isCustom;
    }

    public string Id => _source.Id;

    public string Name => _source.Name;

    public string FeedUrl => _source.FeedUrl;

    public bool IsCustom { get; }

    public string TopicsLabel => string.Join(", ", _source.Topics.Select(t => Loc.T("Topic." + t)));

    public string RegionLabel => Loc.T("Region." + _source.Region);

    [ObservableProperty] private bool _isEnabled = true;

    public void RefreshLabels()
    {
        OnPropertyChanged(nameof(TopicsLabel));
        OnPropertyChanged(nameof(RegionLabel));
    }
}

/// <summary>Một bài trên bảng tin.</summary>
public sealed partial class NewsItemViewModel : ObservableObject
{
    private readonly NewsItem _item;

    public NewsItemViewModel(NewsItem item, DateTimeOffset now)
    {
        _item = item;
        _when = NewsTime.Describe(item.PublishedAt, now);
    }

    public string Title => _item.Title;

    public string Summary => _item.Summary;

    public string Link => _item.Link;

    public string SourceName => _item.SourceName;

    public bool HasSummary => !string.IsNullOrWhiteSpace(_item.Summary);

    public string TopicsLabel => string.Join(" · ", _item.Topics.Select(t => Loc.T("Topic." + t)));

    public bool IsLocal => _item.Region == NewsRegion.Vietnam;

    public string RegionLabel => Loc.T("Region." + _item.Region);

    /// <summary>"20 phút trước" — cập nhật theo nhịp đồng hồ chứ không đứng yên từ lúc tải về.</summary>
    [ObservableProperty] private string _when;

    public void UpdateWhen(DateTimeOffset now) => When = NewsTime.Describe(_item.PublishedAt, now);
}
