using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Renatus.Front.Components;
using Renatus.Front.Models;
using Renatus.Front.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<LocalizationService>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PendingAuthStore>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection(ApiOptions.SectionName))
    .Validate(options => options.BaseUrl.IsAbsoluteUri, "Api:BaseUrl must be an absolute URI.")
    .ValidateOnStart();

builder.Services.AddOptions<YggdrasilOptions>()
    .Bind(builder.Configuration.GetSection(YggdrasilOptions.SectionName))
    .Validate(options => options.PublicBaseUrl.IsAbsoluteUri, "Yggdrasil:PublicBaseUrl must be an absolute URI.")
    .ValidateOnStart();

builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<ApiClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<ApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.AbsoluteUri.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped<FrontSession>();
builder.Services.AddScoped<FrontAuthService>();
builder.Services.AddScoped<PlayerApi>();
builder.Services.AddScoped<TextureApi>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(3);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/account/signin", async (PendingAuthStore store, HttpContext context, string? key, string? returnUrl) =>
{
    var response = store.Consume(key ?? string.Empty);
    if (response is null)
    {
        return Results.Redirect("/login");
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, response.User.Id.ToString()),
        new Claim(ClaimTypes.Name, response.User.Username),
        new Claim(ClaimTypes.GivenName, response.User.NickName),
        new Claim(ClaimTypes.Role, response.User.Role.ToString()),
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    // The backend JWT is stored only in the encrypted auth cookie, never in
    // browser storage or page markup. It is not added to the cookie claims.
    var properties = new AuthenticationProperties
    {
        IsPersistent = true,
        ExpiresUtc = response.ExpiresAt.UtcDateTime,
    };
    properties.StoreTokens(
    [
        new AuthenticationToken { Name = "access_token", Value = response.AccessToken }
    ]);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        properties);

    return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/");
});

app.MapGet("/account/signout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.Run();

static bool IsLocalUrl(string? url)
{
    return !string.IsNullOrEmpty(url)
        && url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\");
}
