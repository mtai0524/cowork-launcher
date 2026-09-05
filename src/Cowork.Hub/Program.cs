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
var agents = builder.Configuration.GetSection("Agents").Get<List<AgentOptions>>() ?? new List<AgentOptions>();
HubOptions.Validate(web, agents);

builder.Services.AddSingleton(web);
builder.Services.AddSingleton(new AgentDirectory(agents.Select(a => new AgentCredential(a.Name, a.Token))));
builder.Services.AddSingleton<IClock>(Cowork.Core.Services.SystemClock.Instance);
builder.Services.AddSingleton<IAgentCommandSender, HubCommandSender>();
builder.Services.AddSingleton(sp => new MachineRegistry(
    sp.GetRequiredService<IAgentCommandSender>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<AgentDirectory>().Names));

// ---------- Web ----------
builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<UiLanguage>();

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
