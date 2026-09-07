using System.Security.Claims;
using Cowork.Core.Services;
using Cowork.Hub;
using Cowork.Hub.Components;
using Cowork.Remote;
using Cowork.Remote.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---------- Cấu hình ----------
var web = builder.Configuration.GetSection("Web").Get<WebOptions>() ?? new WebOptions();
var seed = builder.Configuration.GetSection("Agents").Get<List<AgentOptions>>() ?? new List<AgentOptions>();

// Danh sách máy sửa được trên web nên nó phải sống ngoài appsettings.json — file đó bị
// WebDeploy đồng bộ đè mỗi lần deploy. App_Data nằm ngoài phạm vi đồng bộ, sống qua được.
//
// Dựng sớm chứ không qua factory của DI: cấu hình sai phải làm hub chết ngay lúc khởi động,
// chứ không phải im lặng tới khi có người mở trang đầu tiên.
using var startupLogging = LoggerFactory.Create(logging => logging
    .AddConfiguration(builder.Configuration.GetSection("Logging"))
    .AddConsole());

// Đường dẫn đổi được để test chỉ vào thư mục tạm — nếu không, test sẽ đọc phải file
// mà một lần chạy hub ở local vô tình để lại trong thư mục dự án.
var storePath = builder.Configuration["AgentStorePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "agents.json");

var store = new JsonAgentStore(storePath, new HubLogger(startupLogging.CreateLogger("Cowork.Hub.Agents")));

// appsettings.json chỉ là giống ban đầu: dùng cho lần chạy đầu, sau đó file đã lưu thắng.
var effective = store.Load() ?? seed.Select(a => new AgentCredential(a.Name, a.Token)).ToList();
HubOptions.Validate(web, effective);

builder.Services.AddSingleton<IAgentStore>(store);
builder.Services.AddSingleton(new AgentDirectory(effective));
builder.Services.AddSingleton(web);
builder.Services.AddSingleton<IClock>(Cowork.Core.Services.SystemClock.Instance);
builder.Services.AddSingleton<IAgentCommandSender, HubCommandSender>();
builder.Services.AddSingleton(sp => new MachineRegistry(
    sp.GetRequiredService<IAgentCommandSender>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<AgentDirectory>().Names));
builder.Services.AddSingleton<AgentAdmin>();

// ---------- Web ----------
builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<StaticAssets>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UiLanguage>();
builder.Services.AddScoped<UiTheme>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "cowork.hub";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Đăng nhập là một form POST thường: component Blazor không tự đặt cookie được.
// Đặt dưới /auth/ vì component /login của Blazor cũng nhận POST — trùng đường dẫn là mơ hồ.
app.MapPost("/auth/login", async (HttpContext http, WebOptions options) =>
{
    var form = await http.Request.ReadFormAsync();
    if (!HubOptions.PasswordMatches(options, form["password"]))
        return Results.Redirect("/login?failed=1");

    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, "admin") },
        CookieAuthenticationDefaults.AuthenticationScheme);

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/");
}).DisableAntiforgery();

// Ngôn ngữ và phong cách cũng là form POST vì cùng lý do: đặt cookie xong nạp lại trang,
// nhờ vậy thẻ <html> mang sẵn data-theme đúng và không có cú nháy màu.
app.MapPost("/ui/theme", async (HttpContext http) =>
{
    var form = await http.Request.ReadFormAsync();
    var theme = UiPreferences.ParseTheme(form["theme"]);

    http.Response.Cookies.Append(UiPreferences.ThemeCookie, theme.ToString(), PreferenceCookie(http));
    return Results.Redirect(UiPreferences.SafeReturnUrl(form["returnUrl"]));
}).DisableAntiforgery();

app.MapPost("/ui/language", async (HttpContext http) =>
{
    var form = await http.Request.ReadFormAsync();
    var language = UiPreferences.ParseLanguage(form["language"]);

    http.Response.Cookies.Append(UiPreferences.LanguageCookie, language.ToString(), PreferenceCookie(http));
    return Results.Redirect(UiPreferences.SafeReturnUrl(form["returnUrl"]));
}).DisableAntiforgery();

static CookieOptions PreferenceCookie(HttpContext http) => new()
{
    MaxAge = UiPreferences.CookieLifetime,
    HttpOnly = true,
    SameSite = SameSiteMode.Lax,
    Secure = http.Request.IsHttps,
    IsEssential = true,
};

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapHub<AgentHub>(HubMethods.AgentPath);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>Để test tích hợp dựng được hub trong tiến trình bằng WebApplicationFactory.</summary>
public partial class Program
{
}
