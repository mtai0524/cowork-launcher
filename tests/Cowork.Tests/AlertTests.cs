using System.Text.Json;
using Cowork.Core.Models;
using Cowork.Core.Services;
using Xunit;

namespace Cowork.Tests;

/// <summary>Quyết định gửi hay không là hàm thuần — kiểm khoảng lặng mà không phải chờ phút thật.</summary>
public class AlertPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 2, 0, 0, TimeSpan.FromHours(7));

    private static NotificationSettings Settings(bool enabled = true, int dedupeMinutes = 5)
        => new() { Enabled = enabled, DedupeMinutes = dedupeMinutes };

    [Fact]
    public void SendsTheFirstAlert()
        => Assert.True(AlertPolicy.ShouldSend(Settings(), lastSentAt: null, Now));

    [Fact]
    public void TheSwitchOff_StopsEverything()
        => Assert.False(AlertPolicy.ShouldSend(Settings(enabled: false), null, Now));

    [Fact]
    public void ARepeatWithinTheQuietPeriod_IsDropped()
        => Assert.False(AlertPolicy.ShouldSend(Settings(), Now.AddMinutes(-4), Now));

    [Fact]
    public void TheSameAlertAgainLater_GoesOut()
        => Assert.True(AlertPolicy.ShouldSend(Settings(), Now.AddMinutes(-5), Now));

    [Fact]
    public void ZeroQuietPeriod_SendsEveryTime()
        => Assert.True(AlertPolicy.ShouldSend(Settings(dedupeMinutes: 0), Now.AddSeconds(-1), Now));

    [Fact]
    public void DedupeKey_SeparatesAppsAndTitles()
    {
        var a = new Alert("App chạy lỗi", "chi tiết", "backup", Now);
        var b = new Alert("App chạy lỗi", "chi tiết khác", "backup", Now);
        var c = new Alert("App chạy lỗi", "chi tiết", "bao cao", Now);
        var d = new Alert("Treo", "chi tiết", "backup", Now);

        // Cùng app, cùng loại cảnh báo là một — dù câu chữ khác nhau.
        Assert.Equal(a.DedupeKey, b.DedupeKey);
        Assert.NotEqual(a.DedupeKey, c.DedupeKey);
        Assert.NotEqual(a.DedupeKey, d.DedupeKey);
    }

    [Fact]
    public void AnyChannelConfigured_LooksAtEveryChannel()
    {
        var channels = new IAlertChannel[] { new WebhookAlertChannel(), new TelegramAlertChannel() };

        Assert.False(AlertPolicy.AnyChannelConfigured(new NotificationSettings(), channels));
        Assert.True(AlertPolicy.AnyChannelConfigured(
            new NotificationSettings { WebhookUrl = "https://example.com/hook" }, channels));
    }
}

public class AlertChannelTests
{
    private static readonly Alert Sample =
        new("App chạy lỗi", "“backup” kết thúc với mã thoát 1.", "backup", new DateTimeOffset(2026, 9, 5, 2, 0, 0, TimeSpan.FromHours(7)));

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("khong-phai-url", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://hooks.slack.com/services/x", true)]
    [InlineData("http://localhost:9000/hook", true)]
    public void Webhook_NeedsAnHttpUrl(string url, bool configured)
        => Assert.Equal(configured, new WebhookAlertChannel().IsConfigured(new NotificationSettings { WebhookUrl = url }));

    [Fact]
    public void Webhook_PayloadCarriesTextAndTheDetails()
    {
        using var document = JsonDocument.Parse(WebhookAlertChannel.BuildPayload(Sample));
        var root = document.RootElement;

        // "text" là trường Slack/Discord/Teams đọc được ngay.
        Assert.Contains("App chạy lỗi", root.GetProperty("text").GetString());
        Assert.Equal("App chạy lỗi", root.GetProperty("title").GetString());
        Assert.Equal("backup", root.GetProperty("app").GetString());
        Assert.Equal(Environment.MachineName, root.GetProperty("machine").GetString());
    }

    [Fact]
    public void Telegram_NeedsBothTokenAndChat()
    {
        var channel = new TelegramAlertChannel();

        Assert.False(channel.IsConfigured(new NotificationSettings { TelegramBotToken = "abc" }));
        Assert.False(channel.IsConfigured(new NotificationSettings { TelegramChatId = "123" }));
        Assert.True(channel.IsConfigured(new NotificationSettings { TelegramBotToken = "abc", TelegramChatId = "123" }));
    }

    [Fact]
    public void Telegram_UrlUsesTheToken()
        => Assert.Equal("https://api.telegram.org/bot123:ABC/sendMessage",
            TelegramAlertChannel.BuildUrl(new NotificationSettings { TelegramBotToken = " 123:ABC " }));

    [Fact]
    public void Telegram_EscapesHtml_SoAnAppNamedWithBracketsDoesNotBreakTheMessage()
    {
        var alert = new Alert("Lỗi <b>", "app & <script>", "x", DateTimeOffset.Now);

        var text = TelegramAlertChannel.BuildText(alert);

        Assert.Contains("&lt;b&gt;", text);
        Assert.Contains("app &amp; &lt;script&gt;", text);
    }

    [Fact]
    public void Email_NeedsHostFromAndAtLeastOneRecipient()
    {
        var channel = new EmailAlertChannel();
        var settings = new NotificationSettings { SmtpHost = "smtp.example.com", EmailFrom = "a@example.com" };

        Assert.False(channel.IsConfigured(settings));

        settings.EmailTo = "b@example.com";
        Assert.True(channel.IsConfigured(settings));
    }

    [Theory]
    [InlineData("a@x.com, b@x.com", 2)]
    [InlineData("a@x.com;b@x.com; ", 2)]
    [InlineData("  ", 0)]
    [InlineData(null, 0)]
    public void Email_SplitsRecipients(string? value, int count)
        => Assert.Equal(count, EmailAlertChannel.ParseRecipients(value).Count);

    [Fact]
    public void Email_SubjectNamesTheMachine_SoSeveralMachinesAreTellableApart()
    {
        Assert.Contains(Environment.MachineName, EmailAlertChannel.BuildSubject(Sample));
        Assert.Contains("App chạy lỗi", EmailAlertChannel.BuildSubject(Sample));
    }

    [Fact]
    public void Email_BodyRepeatsTheContext()
    {
        var body = EmailAlertChannel.BuildBody(Sample);

        Assert.Contains("mã thoát 1", body);
        Assert.Contains("backup", body);
        Assert.Contains("2026-09-05 02:00:00", body);
    }
}

public class AlertDispatcherTests
{
    private sealed class FakeChannel : IAlertChannel
    {
        public FakeChannel(string name, bool configured = true, bool throws = false)
        {
            Name = name;
            Configured = configured;
            Throws = throws;
        }

        public string Name { get; }
        public bool Configured { get; set; }
        public bool Throws { get; set; }
        public List<Alert> Sent { get; } = new();

        public bool IsConfigured(NotificationSettings settings) => Configured;

        public Task SendAsync(NotificationSettings settings, Alert alert, CancellationToken cancellationToken)
        {
            lock (Sent)
                Sent.Add(alert);

            return Throws ? Task.FromException(new InvalidOperationException("hong")) : Task.CompletedTask;
        }
    }

    private static Alert Alert(string title = "App chạy lỗi", string? app = "backup")
        => new(title, "chi tiết", app, DateTimeOffset.Now);

    private static (AlertDispatcher Dispatcher, NotificationSettings Settings, FixedClock Clock) Build(
        params IAlertChannel[] channels)
    {
        var settings = new NotificationSettings { Enabled = true, DedupeMinutes = 5 };
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 5, 2, 0, 0, TimeSpan.FromHours(7)));

        return (new AlertDispatcher(() => settings, channels, clock, NullLogger.Instance), settings, clock);
    }

    private static async Task<AlertSentEventArgs> WaitForSendAsync(AlertDispatcher dispatcher, Action send)
    {
        var completion = new TaskCompletionSource<AlertSentEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, AlertSentEventArgs e) => completion.TrySetResult(e);

        dispatcher.Sent += Handler;
        try
        {
            send();
            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(completion.Task, finished);
            return await completion.Task;
        }
        finally
        {
            dispatcher.Sent -= Handler;
        }
    }

    [Fact]
    public async Task SendsToEveryConfiguredChannel()
    {
        var a = new FakeChannel("a");
        var b = new FakeChannel("b");
        var off = new FakeChannel("off", configured: false);
        var (dispatcher, _, _) = Build(a, b, off);
        using (dispatcher)
        {
            var result = await WaitForSendAsync(dispatcher, () => Assert.True(dispatcher.Send(Alert())));

            Assert.Equal(new[] { "a", "b" }, result.Delivered.OrderBy(x => x));
            Assert.Empty(result.Failed);
            Assert.Single(a.Sent);
            Assert.Empty(off.Sent);
        }
    }

    [Fact]
    public async Task AFailingChannel_DoesNotStopTheOthers()
    {
        var good = new FakeChannel("good");
        var bad = new FakeChannel("bad", throws: true);
        var (dispatcher, _, _) = Build(good, bad);
        using (dispatcher)
        {
            var result = await WaitForSendAsync(dispatcher, () => dispatcher.Send(Alert()));

            Assert.Equal(new[] { "good" }, result.Delivered);
            Assert.Equal(new[] { "bad" }, result.Failed);
        }
    }

    [Fact]
    public void TheSwitchOff_SendsNothing()
    {
        var channel = new FakeChannel("a");
        var (dispatcher, settings, _) = Build(channel);
        using (dispatcher)
        {
            settings.Enabled = false;

            Assert.False(dispatcher.Send(Alert()));
            Assert.Empty(channel.Sent);
        }
    }

    [Fact]
    public void NoChannelConfigured_SendsNothing()
    {
        var channel = new FakeChannel("a", configured: false);
        var (dispatcher, _, _) = Build(channel);
        using (dispatcher)
        {
            Assert.False(dispatcher.Send(Alert()));
            Assert.False(dispatcher.AnyConfigured());
        }
    }

    [Fact]
    public async Task RepeatsOfTheSameAlert_AreDroppedUntilTheQuietPeriodPasses()
    {
        var channel = new FakeChannel("a");
        var (dispatcher, _, clock) = Build(channel);
        using (dispatcher)
        {
            await WaitForSendAsync(dispatcher, () => dispatcher.Send(Alert()));

            // App hỏng hẳn lỗi liên tục: không để nó rung điện thoại cả đêm.
            Assert.False(dispatcher.Send(Alert()));
            Assert.False(dispatcher.Send(Alert()));

            clock.Advance(TimeSpan.FromMinutes(5));
            await WaitForSendAsync(dispatcher, () => Assert.True(dispatcher.Send(Alert())));

            lock (channel.Sent)
                Assert.Equal(2, channel.Sent.Count);
        }
    }

    [Fact]
    public async Task ADifferentApp_IsNotHeldBackByAnotherAppsQuietPeriod()
    {
        var channel = new FakeChannel("a");
        var (dispatcher, _, _) = Build(channel);
        using (dispatcher)
        {
            await WaitForSendAsync(dispatcher, () => dispatcher.Send(Alert(app: "backup")));
            await WaitForSendAsync(dispatcher, () => Assert.True(dispatcher.Send(Alert(app: "bao cao"))));

            lock (channel.Sent)
                Assert.Equal(2, channel.Sent.Count);
        }
    }

    [Fact]
    public async Task SendTest_IgnoresTheQuietPeriodAndReportsBothSides()
    {
        var good = new FakeChannel("good");
        var bad = new FakeChannel("bad", throws: true);
        var (dispatcher, _, _) = Build(good, bad);
        using (dispatcher)
        {
            await dispatcher.SendTestAsync(Alert());
            var result = await dispatcher.SendTestAsync(Alert());

            Assert.Equal(new[] { "good" }, result.Delivered);
            Assert.Equal(new[] { "bad" }, result.Failed);
        }
    }
}

/// <summary>Thiết lập cảnh báo phải đi hết vòng lưu → nạp.</summary>
public class AlertSettingsTests
{
    [Fact]
    public void Notifications_RoundTripThroughWorkspaceJson()
    {
        using var temp = new TempDirectory();
        var store = new JsonWorkspaceStore(new CoworkPaths(temp.Path), NullLogger.Instance);

        var workspace = new CoworkWorkspace();
        workspace.Settings.Notifications = new NotificationSettings
        {
            Enabled = true,
            WebhookUrl = "https://example.com/hook",
            TelegramBotToken = "123:ABC",
            TelegramChatId = "42",
            SmtpHost = "smtp.example.com",
            SmtpPort = 465,
            SmtpUseSsl = true,
            SmtpUser = "user",
            SmtpPassword = "secret",
            EmailFrom = "cowork@example.com",
            EmailTo = "me@example.com",
            DedupeMinutes = 10,
        };

        store.Save(workspace);
        var loaded = store.Load().Settings.Notifications;

        Assert.True(loaded.Enabled);
        Assert.Equal("https://example.com/hook", loaded.WebhookUrl);
        Assert.Equal("123:ABC", loaded.TelegramBotToken);
        Assert.Equal("42", loaded.TelegramChatId);
        Assert.Equal(465, loaded.SmtpPort);
        Assert.Equal("secret", loaded.SmtpPassword);
        Assert.Equal("me@example.com", loaded.EmailTo);
        Assert.Equal(10, loaded.DedupeMinutes);
    }

    [Fact]
    public void OlderWorkspace_WithoutNotifications_GetsSafeDefaults()
    {
        using var temp = new TempDirectory();
        var paths = new CoworkPaths(temp.Path);
        File.WriteAllText(paths.WorkspaceFile, """
            { "Version": 1, "Apps": [], "Settings": { } }
            """);

        var loaded = new JsonWorkspaceStore(paths, NullLogger.Instance).Load().Settings.Notifications;

        Assert.False(loaded.Enabled);
        Assert.Equal(string.Empty, loaded.WebhookUrl);
        Assert.Equal(587, loaded.SmtpPort);
        Assert.True(loaded.SmtpUseSsl);
        Assert.Equal(5, loaded.DedupeMinutes);
    }

    [Fact]
    public void Clone_DoesNotShareStateWithTheOriginal()
    {
        var original = new NotificationSettings { WebhookUrl = "https://a", Enabled = true };

        var copy = original.Clone();
        copy.WebhookUrl = "https://b";

        Assert.Equal("https://a", original.WebhookUrl);
        Assert.True(copy.Enabled);
    }
}
