using System.Security.Claims;
using System.Text;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service;
using Renatus.Host.Service.Cache;
using Renatus.Host.Service.Union;
using Renatus.Host.Service.Connect;
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

builder.Services.AddMemoryCache();
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")).UseOpenIddict());
var openIddict = builder.Services.AddOpenIddict().AddCore(options =>
    options.UseEntityFrameworkCore().UseDbContext<AppDbContext>());

var startupConnectOptions = await LoadConnectOptionsAsync(builder.Configuration);
builder.Services.AddSingleton(startupConnectOptions);
if (startupConnectOptions.Enabled)
{
    AppConfigurationService.ValidateConnectOptions(startupConnectOptions, builder.Environment.IsDevelopment());
    var signingKey = RSA.Create();
    signingKey.ImportFromPem(startupConnectOptions.SigningPrivateKeyPem);
    var encryptionKey = RSA.Create();
    encryptionKey.ImportFromPem(startupConnectOptions.EncryptionPrivateKeyPem);
    openIddict.AddServer(options =>
    {
        options.SetIssuer(new Uri(startupConnectOptions.Issuer));
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetDeviceAuthorizationEndpointUris("connect/device")
            .SetEndUserVerificationEndpointUris("connect/verify")
            .SetUserInfoEndpointUris("connect/userinfo");
        options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().AllowDeviceAuthorizationFlow();
        if (startupConnectOptions.AllowImplicitFlow) options.AllowImplicitFlow();
        options.RequireProofKeyForCodeExchange();
        options.RegisterScopes(OpenIddict.Abstractions.OpenIddictConstants.Scopes.OpenId,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile,
            OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email,
            ConnectConstants.ProfileReadScope, ConnectConstants.ProfileSelectScope, ConnectConstants.ServerJoinScope);
        options.SetAccessTokenLifetime(TimeSpan.FromSeconds(startupConnectOptions.AccessTokenTtlSeconds))
            .SetRefreshTokenLifetime(TimeSpan.FromSeconds(startupConnectOptions.RefreshTokenTtlSeconds))
            .SetAuthorizationCodeLifetime(TimeSpan.FromSeconds(startupConnectOptions.AuthorizationCodeTtlSeconds))
            .SetDeviceCodeLifetime(TimeSpan.FromSeconds(startupConnectOptions.DeviceCodeTtlSeconds));
        options.AddSigningKey(new RsaSecurityKey(signingKey));
        options.AddEncryptionKey(new RsaSecurityKey(encryptionKey));
        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableEndUserVerificationEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough();
        if (builder.Environment.IsDevelopment()
            && AppConfigurationService.IsLoopbackHttpIssuer(startupConnectOptions.Issuer))
            aspNetCore.DisableTransportSecurityRequirement();
    });
    openIddict.AddValidation(options =>
    {
        options.UseLocalServer();
        options.EnableAuthorizationEntryValidation();
        options.UseAspNetCore();
    });
}
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();
builder.Services.AddSingleton<AppConfigurationService>();
builder.Services.AddSingleton<YggdrasilSigningKey>();
builder.Services.AddScoped<YggdrasilService>();
builder.Services.AddScoped<YggdrasilLifecycleService>();
builder.Services.AddScoped<ConnectInteractionStore>();
builder.Services.AddScoped<ConnectManagementService>();
builder.Services.AddScoped<ConnectProtocolService>();
builder.Services.AddScoped<ConnectSessionService>();
builder.Services.AddScoped<ConnectBootstrapper>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<TextureService>();
builder.Services.AddScoped<TextureLibraryService>();
builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<MailService>();
builder.Services.AddScoped<RegistrationVerificationService>();
builder.Services.AddUnionMemberServices();
builder.Services.AddAutoMapper(_ => { }, typeof(Program).Assembly);
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.JwtSigningKey))
{
    throw new InvalidOperationException("Auth:JwtSigningKey must be configured.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    if (startupConnectOptions.Enabled)
        await scope.ServiceProvider.GetRequiredService<ConnectBootstrapper>()
            .InitializeAsync(startupConnectOptions, CancellationToken.None);
}

var appConfigurationService = app.Services.GetRequiredService<AppConfigurationService>();
var yggdrasilOptions = await appConfigurationService.GetYggdrasilOptionsAsync(CancellationToken.None);
var unionOptions = await appConfigurationService.GetUnionOptionsAsync(CancellationToken.None);
var unionState = await appConfigurationService.GetUnionStateAsync(CancellationToken.None);
var yggdrasilSigningKey = app.Services.GetRequiredService<YggdrasilSigningKey>();
yggdrasilSigningKey.Initialize(yggdrasilOptions.SigningPrivateKeyPem);
yggdrasilSigningKey.InitializeTexture(
    unionOptions.Enabled && !string.IsNullOrWhiteSpace(unionState.TextureSigningPrivateKeyPem)
        ? unionState.TextureSigningPrivateKeyPem
        : yggdrasilOptions.TextureSigningPrivateKeyPem);

//app.UseMiddleware<RequestResponseLoggingMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers.AccessControlAllowOrigin = "*";
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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");


app.Run();

static async Task<YggdrasilConnectOptions> LoadConnectOptionsAsync(IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default must be configured.");
    var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).UseOpenIddict().Options;
    await using var context = new AppDbContext(options);
    await DbSeed.InitializeAsync(context, configuration);
    var json = await context.AppConfigurations.AsNoTracking()
        .Where(entity => entity.Key == AppConfigurationKeys.YggdrasilConnect)
        .Select(entity => entity.Value).SingleAsync();
    return System.Text.Json.JsonSerializer.Deserialize<YggdrasilConnectOptions>(json,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
        ?? new YggdrasilConnectOptions();
}
