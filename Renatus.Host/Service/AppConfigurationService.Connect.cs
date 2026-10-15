using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed partial class AppConfigurationService
{
    public Task<YggdrasilConnectOptions> GetConnectOptionsAsync(CancellationToken cancellationToken)
        => GetAsync<YggdrasilConnectOptions>(AppConfigurationKeys.YggdrasilConnect, cancellationToken);

    /// <summary>Validates public issuer, redirect and lifetime settings while preserving database-held private keys.</summary>
    public async Task<ConnectConfigurationDto> UpdateConnectOptionsAsync(
        UpdateConnectConfigurationRequest request,
        CancellationToken cancellationToken,
        bool allowDevelopmentLoopback = false)
    {
        var current = await GetConnectOptionsAsync(cancellationToken);
        var next = new YggdrasilConnectOptions
        {
            Enabled = request.Enabled,
            Issuer = request.Issuer.Trim().TrimEnd('/'),
            SharedClientId = request.SharedClientId.Trim(),
            SharedClientRedirectUris = request.SharedClientRedirectUris.Select(value => value.Trim()).Distinct().ToList(),
            AllowImplicitFlow = request.AllowImplicitFlow,
            SigningPrivateKeyPem = request.ReplaceSigningKey ? CreateConnectPrivateKey() : current.SigningPrivateKeyPem,
            EncryptionPrivateKeyPem = request.ReplaceEncryptionKey ? CreateConnectPrivateKey() : current.EncryptionPrivateKeyPem,
            AccessTokenTtlSeconds = request.AccessTokenTtlSeconds,
            RefreshTokenTtlSeconds = request.RefreshTokenTtlSeconds,
            AuthorizationCodeTtlSeconds = request.AuthorizationCodeTtlSeconds,
            DeviceCodeTtlSeconds = request.DeviceCodeTtlSeconds
        };
        ValidateConnectOptions(next, allowDevelopmentLoopback);
        await UpdateAsync(AppConfigurationKeys.YggdrasilConnect, next, cancellationToken);
        return ToConnectConfiguration(next, true);
    }

    public static ConnectConfigurationDto ToConnectConfiguration(
        YggdrasilConnectOptions options,
        bool requiresRestart)
        => new(options.Enabled, options.Issuer, options.SharedClientId, options.SharedClientRedirectUris,
            options.AllowImplicitFlow, !string.IsNullOrWhiteSpace(options.SigningPrivateKeyPem),
            !string.IsNullOrWhiteSpace(options.EncryptionPrivateKeyPem), requiresRestart,
            requiresRestart ? "restart-required" : options.Enabled ? "enabled" : "disabled",
            options.AccessTokenTtlSeconds, options.RefreshTokenTtlSeconds,
            options.AuthorizationCodeTtlSeconds, options.DeviceCodeTtlSeconds);

    public static void ValidateConnectOptions(YggdrasilConnectOptions options, bool allowDevelopmentLoopback = false)
    {
        if (options.AccessTokenTtlSeconds is < 60 or > 86400
            || options.RefreshTokenTtlSeconds is < 300 or > 7776000
            || options.AuthorizationCodeTtlSeconds is < 60 or > 900
            || options.DeviceCodeTtlSeconds is < 60 or > 1800)
            throw new InvalidOperationException("Connect token lifetime is outside the supported range.");
        if (!options.Enabled) return;
        if (string.IsNullOrWhiteSpace(options.SharedClientId)
            || !Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            || (issuer.Scheme != Uri.UriSchemeHttps
                && !(allowDevelopmentLoopback && IsLoopbackHttpIssuer(options.Issuer))))
            throw new InvalidOperationException("Enabled Connect requires an HTTPS issuer and shared client ID.");
        if (options.SharedClientRedirectUris.Any(uri => !ConnectUriRules.IsAllowedRedirectUri(uri)))
            throw new InvalidOperationException("A shared-client redirect URI is invalid.");
    }

    public static bool IsLoopbackHttpIssuer(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && System.Net.IPAddress.TryParse(uri.Host, out var address)
            && System.Net.IPAddress.IsLoopback(address);

    private static string CreateConnectPrivateKey()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}

internal static class ConnectUriRules
{
    public static bool IsAllowedRedirectUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }
}
