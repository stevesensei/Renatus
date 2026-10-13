using System.Security.Claims;
using System.Text;
using System.Globalization;
using System.Text.Json.Serialization;
using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MudBlazor.Services;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Components;
using Renatus.Host.Frontend.Services;
using Renatus.Host.Middleware;
using Renatus.Host.Service;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;
using Scalar.AspNetCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
if (!File.Exists(appSettingsPath))
{
    throw new FileNotFoundException(
        $"Required configuration file '{appSettingsPath}' was not found. Create appsettings.json and restart the application.",
        appSettingsPath);
}

builder.WebHost.UseStaticWebAssets();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
//builder.Services.Configure<RequestResponseLoggingOptions>(builder.Configuration.GetSection("RequestResponseLogging"));
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddSingleton<LocalizationService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PendingAuthStore>();
builder.Services.AddScoped<FrontendUserContext>();
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddSingleton<AppConfigurationService>();
builder.Services.AddSingleton<YggdrasilSigningKey>();
builder.Services.AddScoped<YggdrasilService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<TextureService>();
builder.Services.AddScoped<TextureLibraryService>();
builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<MailService>();
builder.Services.AddAutoMapper(_ => { }, typeof(Program).Assembly);
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});;
builder.Services.AddEndpointsApiExplorer();

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.JwtSigningKey))
{
    throw new InvalidOperationException("Auth:JwtSigningKey must be configured.");
}

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(3);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(jwtOptions =>
    {
        jwtOptions.MapInboundClaims = false;
        jwtOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = authOptions.JwtIssuer,
            ValidAudience = authOptions.JwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(nameof(UserRole.Admin), policy => policy.RequireRole(nameof(UserRole.Admin)))
    .AddPolicy(nameof(UserRole.Player), policy => policy.RequireRole(nameof(UserRole.Player)));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Input JWT as: Bearer {token}"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document, null),
            []
        }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeed.InitializeAsync(dbContext, builder.Configuration);
}

var appConfigurationService = app.Services.GetRequiredService<AppConfigurationService>();
var yggdrasilOptions = await appConfigurationService.GetYggdrasilOptionsAsync(CancellationToken.None);
app.Services.GetRequiredService<YggdrasilSigningKey>().Initialize(yggdrasilOptions.SigningPrivateKeyPem);

//app.UseMiddleware<RequestResponseLoggingMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Authlib-Injector-API-Location"] = "/api/yggdrasil/";
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.MapScalarApiReference(options =>
    {
        options.AddDocument("v1", "Renatus API", "/swagger/v1/swagger.json");
        options.WithTitle("Renatus API");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
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
