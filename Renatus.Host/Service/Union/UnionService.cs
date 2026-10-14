using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Union;

public sealed partial class UnionService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    AppConfigurationService configurationService,
    UnionCenterClient centerClient,
    ICacheService cacheService,
    IConnectionMultiplexer connectionMultiplexer,
    YggdrasilSigningKey signingKey,
    ILogger<UnionService> logger)
{
    private static readonly SemaphoreSlim TextureKeyUpdateLock = new(1, 1);
    private static readonly SemaphoreSlim ProfileSyncLock = new(1, 1);
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();

    /// <summary>Publishes PHP-compatible hello fields. unionBlacklist denotes signed center email lookup; emailVerification matches Union-enabled Yggdrasil authentication policy, and OAuth is not advertised.</summary>
    public async Task<UnionMemberInfoResponse> GetMemberInfoAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var state = await configurationService.GetUnionStateAsync(cancellationToken);
        var features = options.Enabled ? new[] { "unionBlacklist", "emailVerification" } : [];
        var version = typeof(UnionService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        return new UnionMemberInfoResponse(version, state.ServerListVersion, state.PrivateKeyVersion, features);
    }

    /// <summary>Returns recovery state only; directory payloads, member secrets and private signing keys never enter admin status.</summary>
    public async Task<UnionAdminStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var state = await configurationService.GetUnionStateAsync(cancellationToken);
        return new UnionAdminStatusResponse(options.Enabled, state.ServerListVersion, state.PrivateKeyVersion,
            state.NeedsProfileSync, state.LastProfileSyncAt, state.LastProfileSyncError);
    }

    /// <summary>Returns a write-only secret indicator instead of the member key or any private key.</summary>
    public async Task<UnionAdminSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return ToSettingsResponse(await configurationService.GetUnionOptionsAsync(cancellationToken));
    }

    /// <summary>Validates settings before persistence, retains an omitted member key, and serializes changes with sync. Disabling Union restores the local texture key without changing JWT keys.</summary>
    public async Task<UnionAdminSettingsResponse> UpdateSettingsAsync(UnionAdminUpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.RequestTimeoutSeconds is < 1 or > 30)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Request timeout must be between 1 and 30 seconds.");
        }

        var apiRoot = request.ApiRoot?.Trim() ?? string.Empty;
        var memberCode = request.MemberCode?.Trim() ?? string.Empty;
        if (memberCode.Length > 128 || request.MemberKey?.Length > 1024
            || request.MemberKey?.Any(char.IsControl) == true)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union member settings are invalid.");
        }

        centerClient.ValidateApiRoot(new UnionOptions { ApiRoot = apiRoot });
        await ProfileSyncLock.WaitAsync(cancellationToken);
        try
        {
            await TextureKeyUpdateLock.WaitAsync(cancellationToken);
            try
            {
                var prior = await configurationService.GetUnionOptionsAsync(cancellationToken);
                var rootChanged = centerClient.ValidateApiRoot(prior).AbsoluteUri != centerClient.ValidateApiRoot(new UnionOptions { ApiRoot = apiRoot }).AbsoluteUri;
                var textureKey = await SelectActiveTextureKeyAsync(request.Enabled && !rootChanged, cancellationToken);
                ValidatePrivateKey(textureKey);
                var options = await configurationService.MutateUnionSettingsAsync((current, state) =>
                {
                    var changed = current.Enabled != request.Enabled || current.ApiRoot != apiRoot
                        || (request.MemberKey is not null && current.MemberKey != request.MemberKey.Trim());
                    current.Enabled = request.Enabled;
                    current.ApiRoot = apiRoot;
                    current.MemberCode = memberCode;
                    current.RequestTimeoutSeconds = request.RequestTimeoutSeconds;
                    if (request.MemberKey is not null)
                    {
                        current.MemberKey = request.MemberKey.Trim();
                    }
                    if (rootChanged)
                    {
                        state.Servers = [];
                        state.ServerListVersion = 0;
                        state.PrivateKeyVersion = 0;
                        state.TextureSigningPrivateKeyPem = string.Empty;
                        state.LastProfileChecksum = string.Empty;
                    }
                    if (changed)
                    {
                        state.NeedsProfileSync = true;
                        state.ProfileSyncRevision = checked(state.ProfileSyncRevision + 1);
                    }
                    return current;
                }, cancellationToken);
                signingKey.InitializeTexture(textureKey);
                return ToSettingsResponse(options);
            }
            finally
            {
                TextureKeyUpdateLock.Release();
            }
        }
        finally
        {
            ProfileSyncLock.Release();
        }
    }

    /// <summary>Stores a signed callback's new outgoing member secret; rejects header control characters and never returns or logs the value.</summary>
    public async Task UpdateMemberKeyAsync(string? key, CancellationToken cancellationToken)
    {
        key = key?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 1024 || key.Any(char.IsControl))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union member key is invalid.");
        }
        await configurationService.MutateUnionOptionsAsync(options =>
        {
            options.MemberKey = key;
            return true;
        }, cancellationToken);
        await MarkProfilesDirtyAsync(cancellationToken);
    }

    /// <summary>Validates the complete directory before replacing it, rejects version rollback, and preserves concurrent sync/private-key state.</summary>
    public async Task UpdateServerListAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var response = await centerClient.GetServerListAsync(options, cancellationToken);
        if (response.Version < 0 || response.Servers is null || response.Servers.Count > 10_000)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid server list.");
        }
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in response.Servers)
        {
            if (server is null || string.IsNullOrWhiteSpace(server.Code) || server.Code.Length > 128
                || !codes.Add(server.Code) || server.BsRoot?.Length > 2048
                || !Uri.TryCreate(server.BsRoot, UriKind.Absolute, out var root)
                || !string.IsNullOrEmpty(root.UserInfo) || !string.IsNullOrEmpty(root.Query)
                || !string.IsNullOrEmpty(root.Fragment)
                || (root.Scheme != Uri.UriSchemeHttps && root.Scheme != Uri.UriSchemeHttp))
            {
                throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid server list.");
            }
        }
        await TextureKeyUpdateLock.WaitAsync(cancellationToken);
        try
        {
            var current = await configurationService.GetUnionOptionsAsync(cancellationToken);
            if (centerClient.ValidateApiRoot(current) != centerClient.ValidateApiRoot(options))
            {
                throw new UnionException(StatusCodes.Status409Conflict, "Union center changed during the directory update; retry the request.");
            }
            await configurationService.MutateUnionStateAsync(state =>
            {
                if (response.Version < state.ServerListVersion)
                {
                    throw new UnionException(StatusCodes.Status409Conflict, "Union directory version cannot move backwards.");
                }
                state.Servers = response.Servers;
                state.ServerListVersion = response.Version;
                return true;
            }, cancellationToken);
        }
        finally
        {
            TextureKeyUpdateLock.Release();
        }
    }
    /// <summary>Validates and persists the shared texture key under the runtime key lock; only texture signatures change, and lower key versions are rejected.</summary>
    public async Task UpdatePrivateKeyAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var response = await centerClient.GetPrivateKeyAsync(options, cancellationToken);
        if (response.PrivateKeyVersion < 0)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid private key version.");
        }
        ValidatePrivateKey(response.PrivateKey);
        await TextureKeyUpdateLock.WaitAsync(cancellationToken);
        try
        {
            var currentOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
            if (centerClient.ValidateApiRoot(currentOptions) != centerClient.ValidateApiRoot(options))
            {
                throw new UnionException(StatusCodes.Status409Conflict, "Union center changed during the key update; retry the request.");
            }
            await configurationService.MutateUnionStateAsync(state =>
            {
                if (response.PrivateKeyVersion < state.PrivateKeyVersion)
                {
                    throw new UnionException(StatusCodes.Status409Conflict, "Union texture key version cannot move backwards.");
                }
                state.TextureSigningPrivateKeyPem = response.PrivateKey;
                state.PrivateKeyVersion = response.PrivateKeyVersion;
                return true;
            }, cancellationToken);
            if (currentOptions.Enabled)
            {
                signingKey.InitializeTexture(response.PrivateKey);
            }
        }
        finally
        {
            TextureKeyUpdateLock.Release();
        }
    }

    /// <summary>Performs bounded center diagnostics while returning only safe operation status to administrators.</summary>
    public async Task<UnionAdminOperationResponse> DiagnoseCenterAsync(CancellationToken cancellationToken)
    {
        return await centerClient.DiagnoseAsync(await configurationService.GetUnionOptionsAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Selects persisted Union texture material only while enabled; the local key remains available for disabling and restarting.</summary>
    private async Task<string> SelectActiveTextureKeyAsync(bool unionEnabled, CancellationToken cancellationToken)
    {
        if (unionEnabled)
        {
            var state = await configurationService.GetUnionStateAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(state.TextureSigningPrivateKeyPem))
            {
                return state.TextureSigningPrivateKeyPem;
            }
        }
        return (await configurationService.GetYggdrasilOptionsAsync(cancellationToken)).TextureSigningPrivateKeyPem;
    }

    private static UnionAdminSettingsResponse ToSettingsResponse(UnionOptions options)
    {
        return new UnionAdminSettingsResponse(options.Enabled, options.ApiRoot, options.MemberCode,
            !string.IsNullOrWhiteSpace(options.MemberKey), options.RequestTimeoutSeconds);
    }

    /// <summary>Rejects public-only, malformed, oversized, or unsupported RSA private material without including PEM contents in an error.</summary>
    private static void ValidatePrivateKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || pem.Length > 16 * 1024)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union texture signing key is invalid.");
        }
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            _ = rsa.ExportParameters(true);
            if (rsa.KeySize is < 2048 or > 8192)
            {
                throw new CryptographicException("RSA key size is outside the supported range.");
            }
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union texture signing key is invalid.", exception);
        }
    }
}



