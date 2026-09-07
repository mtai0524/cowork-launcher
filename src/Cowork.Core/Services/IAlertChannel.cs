using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Cowork.Core.Models;

namespace Cowork.Core.Services;

/// <summary>Một đường đưa cảnh báo ra ngoài máy: webhook, Telegram, email…</summary>
public interface IAlertChannel
{
    /// <summary>Tên ngắn để ghi log, không hiển thị cho người dùng.</summary>
    string Name { get; }

    /// <summary>Thiết lập đã đủ để gửi chưa.</summary>
    bool IsConfigured(NotificationSettings settings);

    Task SendAsync(NotificationSettings settings, Alert alert, CancellationToken cancellationToken);
}

/// <summary>Một HttpClient dùng chung cho mọi kênh HTTP; tạo mới mỗi lần gửi sẽ cạn socket.</summary>
internal static class AlertHttp
{
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
}

/// <summary>
/// POST một JSON gọn tới địa chỉ người dùng khai. Cố ý gửi kèm cả <c>text</c> lẫn các trường rời:
/// Slack, Discord và Teams đều đọc được <c>text</c>, còn thứ tự xử lý riêng thì dùng các trường kia.
/// </summary>
public sealed class WebhookAlertChannel : IAlertChannel
{
    public string Name => "webhook";

    public bool IsConfigured(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Uri.TryCreate(settings.WebhookUrl?.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Dựng thân yêu cầu. Tách riêng để kiểm thử được mà không cần gọi mạng.</summary>
    public static string BuildPayload(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return JsonSerializer.Serialize(new
        {
            text = $"{alert.Title}: {alert.Body}",
            title = alert.Title,
            body = alert.Body,
            app = alert.AppName,
            machine = Environment.MachineName,
            at = alert.At,
        });
    }

    public async Task SendAsync(NotificationSettings settings, Alert alert, CancellationToken cancellationToken)
    {
        using var content = new StringContent(BuildPayload(alert), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await AlertHttp.Client
            .PostAsync(settings.WebhookUrl.Trim(), content, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Gửi tin qua Bot API của Telegram.</summary>
public sealed class TelegramAlertChannel : IAlertChannel
{
    public string Name => "telegram";

    public bool IsConfigured(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !string.IsNullOrWhiteSpace(settings.TelegramBotToken)
               && !string.IsNullOrWhiteSpace(settings.TelegramChatId);
    }

    public static string BuildUrl(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return $"https://api.telegram.org/bot{settings.TelegramBotToken.Trim()}/sendMessage";
    }

    /// <summary>Tiêu đề in đậm rồi tới nội dung; thoát ký tự để HTML của Telegram không vỡ.</summary>
    public static string BuildText(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return $"<b>{Escape(alert.Title)}</b>\n{Escape(alert.Body)}\n<i>{Escape(Environment.MachineName)}</i>";
    }

    private static string Escape(string? text)
        => (text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public async Task SendAsync(NotificationSettings settings, Alert alert, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            chat_id = settings.TelegramChatId.Trim(),
            text = BuildText(alert),
            parse_mode = "HTML",
            disable_web_page_preview = true,
        });

        using var content = new StringContent(payload, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await AlertHttp.Client
            .PostAsync(BuildUrl(settings), content, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Gửi email qua SMTP.</summary>
public sealed class EmailAlertChannel : IAlertChannel
{
    public string Name => "email";

    public bool IsConfigured(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !string.IsNullOrWhiteSpace(settings.SmtpHost)
               && !string.IsNullOrWhiteSpace(settings.EmailFrom)
               && ParseRecipients(settings.EmailTo).Count > 0;
    }

    /// <summary>Tách danh sách người nhận, bỏ khoảng trắng và phần tử rỗng.</summary>
    public static IReadOnlyList<string> ParseRecipients(string? value)
        => (value ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

    public static string BuildSubject(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        return $"[Cowork · {Environment.MachineName}] {alert.Title}";
    }

    public static string BuildBody(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var builder = new StringBuilder()
            .AppendLine(alert.Body)
            .AppendLine()
            .Append("Máy: ").AppendLine(Environment.MachineName)
            .Append("Lúc: ").AppendLine(alert.At.ToString("yyyy-MM-dd HH:mm:ss"));

        if (!string.IsNullOrWhiteSpace(alert.AppName))
            builder.Append("App: ").AppendLine(alert.AppName);

        return builder.ToString();
    }

    public async Task SendAsync(NotificationSettings settings, Alert alert, CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(settings.EmailFrom.Trim()),
            Subject = BuildSubject(alert),
            Body = BuildBody(alert),
        };

        foreach (var recipient in ParseRecipients(settings.EmailTo))
            message.To.Add(recipient);

        using var client = new SmtpClient(settings.SmtpHost.Trim(), settings.SmtpPort)
        {
            EnableSsl = settings.SmtpUseSsl,
        };

        if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new System.Net.NetworkCredential(settings.SmtpUser.Trim(), settings.SmtpPassword);
        }

        // SmtpClient không nhận CancellationToken; huỷ bằng cách bảo nó thôi gửi.
        using var registration = cancellationToken.Register(client.SendAsyncCancel);
        await client.SendMailAsync(message).ConfigureAwait(false);
    }
}
