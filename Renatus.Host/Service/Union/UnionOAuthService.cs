using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Union;

public sealed class UnionOAuthService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    AppConfigurationService configurationService,
    UnionCenterClient centerClient,
    IConnectionMultiplexer connectionMultiplexer)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedContinuationFields = new(StringComparer.Ordinal)
    {
        "client_id", "redirect_uri", "response_type", "scope", "state", "code_challenge", "code_challenge_method", "backend"
    };
    private readonly IDatabase _redis = connectionMultiplexer.GetDatabase();

    /// <summary>Returns only the stable public half of the server-held Union OAuth signing key.</summary>
    public async Task<UnionOAuthPublicKeyResponse> GetPublicKeyAsync(CancellationToken cancellationToken)
    {
        var (_, privateKeyPem) = await GetOrCreateSigningKeyAsync(cancellationToken);
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
            return new UnionOAuthPublicKeyResponse(rsa.ExportSubjectPublicKeyInfoPem());
        }
        catch (CryptographicException exception)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable,
                "Union OAuth signing key is invalid.", exception);
        }
    }

    /// <summary>Stores allowlisted legacy grant parameters server-side and returns a local consent URL containing only an opaque interaction ID.</summary>
    public async Task<string> CreateInteractionAsync(
        IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> query,
        CancellationToken cancellationToken)
    {
        var (options, _) = await GetOrCreateSigningKeyAsync(cancellationToken);
        var continuation = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query)
        {
            if (!AllowedContinuationFields.Contains(pair.Key)) continue;
            if (pair.Value.Count != 1 || pair.Value[0] is not { Length: <= 2048 } value || value.Any(char.IsControl))
            {
                throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth continuation is invalid.");
            }
            continuation[pair.Key] = value;
        }
        if (continuation.Sum(pair => pair.Key.Length + pair.Value.Length) > 8192)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth continuation is too large.");
        }

        ValidateConsentPath(options.OAuth2ConsentPath);
        var ttl = TimeSpan.FromSeconds(Math.Clamp(options.OAuth2InteractionTtlSeconds, 60, 1800));
        var interactionId = Guid.NewGuid();
        var interaction = new UnionOAuthInteraction(DateTimeOffset.UtcNow.Add(ttl), continuation);
        await _redis.StringSetAsync(InteractionKey(interactionId), JsonSerializer.Serialize(interaction, JsonOptions), ttl);

        var site = await configurationService.GetSiteOptionsAsync(cancellationToken);
        var baseUri = new Uri(site.PublicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var consentUri = new Uri(baseUri, options.OAuth2ConsentPath.TrimStart('/'));
        return AppendQuery(consentUri, [new("interaction", interactionId.ToString("N"))]);
    }

    /// <summary>Returns non-sensitive interaction context for a logged-in frontend without consuming the one-time grant.</summary>
    public async Task<UnionOAuthInteractionResponse> GetInteractionAsync(Guid interactionId, CancellationToken cancellationToken)
    {
        await EnsureEnabledAsync(cancellationToken);
        var json = await _redis.StringGetAsync(InteractionKey(interactionId));
        var interaction = DeserializeInteraction(json);
        return new UnionOAuthInteractionResponse(interactionId, interaction.ExpiresAt, interaction.Continuation);
    }

    /// <summary>Atomically consumes consent and creates the exact PHP-compatible signed and center-encrypted continuation token on approval.</summary>
    public async Task<UnionOAuthCompleteResponse> CompleteInteractionAsync(
        Guid userId,
        Guid interactionId,
        UnionOAuthCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var json = await _redis.StringGetDeleteAsync(InteractionKey(interactionId));
        var interaction = DeserializeInteraction(json);
        var (options, privateKeyPem) = await GetOrCreateSigningKeyAsync(cancellationToken);
        if (!request.Approve)
        {
            return new UnionOAuthCompleteResponse(BuildCenterContinuation(options,
                interaction.Continuation, [new("error", "access_denied")]));
        }

        if (string.IsNullOrWhiteSpace(options.MemberKey) || options.MemberKey.Length > 1024
            || options.MemberKey.Any(char.IsControl))
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union member key is not configured.");
        }
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.AsNoTracking()
            .Where(value => value.Id == userId && !value.IsDisabled)
            .Select(value => new { value.UnionUserId, value.NickName, value.Email })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnionException(StatusCodes.Status403Forbidden, "Account is unavailable.");

        var userInfoJson = JsonSerializer.Serialize(new UnionOAuthUserInfo(
            user.UnionUserId, user.NickName, user.Email, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()), JsonOptions);
        var userInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(userInfoJson));
        var mac = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.MemberKey), Encoding.UTF8.GetBytes(userInfo))).ToLowerInvariant();

        byte[] signature;
        using (var signingKey = RSA.Create())
        {
            try
            {
                signingKey.ImportFromPem(privateKeyPem);
                signature = signingKey.SignData(Encoding.UTF8.GetBytes($"{userInfo}.{mac}"),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException exception)
            {
                throw new UnionException(StatusCodes.Status503ServiceUnavailable,
                    "Union OAuth signing key is invalid.", exception);
            }
        }

        var tokenJson = JsonSerializer.Serialize(new UnionOAuthToken(
            userInfo, mac, Convert.ToBase64String(signature)), JsonOptions);
        var backendKey = await centerClient.GetOAuthBackendKeyAsync(options, cancellationToken);
        var encrypted = EncryptInPkcs1Chunks(tokenJson, backendKey.PublicKey);
        return new UnionOAuthCompleteResponse(BuildCenterContinuation(options,
            interaction.Continuation, [new("userInfoToken", encrypted)]));
    }

    private async Task<(UnionOptions Options, string PrivateKeyPem)> GetOrCreateSigningKeyAsync(CancellationToken cancellationToken)
    {
        var current = await configurationService.GetUnionOptionsAsync(cancellationToken);
        if (!current.Enabled || !current.OAuth2Enabled)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union OAuth is disabled.");
        }
        if (!string.IsNullOrWhiteSpace(current.OAuth2SigningPrivateKeyPem))
        {
            return (current, current.OAuth2SigningPrivateKeyPem);
        }

        var privateKeyPem = await configurationService.MutateUnionOptionsAsync(options =>
        {
            if (!options.Enabled || !options.OAuth2Enabled)
            {
                throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union OAuth is disabled.");
            }
            if (string.IsNullOrWhiteSpace(options.OAuth2SigningPrivateKeyPem))
            {
                using var rsa = RSA.Create(2048);
                options.OAuth2SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
            }
            return options.OAuth2SigningPrivateKeyPem;
        }, cancellationToken);
        return (await configurationService.GetUnionOptionsAsync(cancellationToken), privateKeyPem);
    }

    private async Task EnsureEnabledAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        if (!options.Enabled || !options.OAuth2Enabled)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union OAuth is disabled.");
        }
    }

    private static UnionOAuthInteraction DeserializeInteraction(RedisValue json)
    {
        if (!json.HasValue)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth interaction is expired or already used.");
        }
        try
        {
            var interaction = JsonSerializer.Deserialize<UnionOAuthInteraction>(json.ToString(), JsonOptions)
                ?? throw new JsonException();
            if (interaction.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth interaction is expired or already used.");
            }
            return interaction;
        }
        catch (JsonException exception)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth interaction is invalid.", exception);
        }
    }

    private string BuildCenterContinuation(
        UnionOptions options,
        IReadOnlyDictionary<string, string> continuation,
        IEnumerable<KeyValuePair<string, string>> generated)
    {
        var root = centerClient.ValidateApiRoot(options);
        var target = new Uri(root, "oauth2/continue");
        return AppendQuery(target, generated.Concat(continuation));
    }

    private static string EncryptInPkcs1Chunks(string plaintext, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
            var source = Encoding.UTF8.GetBytes(plaintext);
            var chunkSize = rsa.KeySize / 8 - 11;
            using var encrypted = new MemoryStream();
            for (var offset = 0; offset < source.Length; offset += chunkSize)
            {
                var length = Math.Min(chunkSize, source.Length - offset);
                encrypted.Write(rsa.Encrypt(source.AsSpan(offset, length).ToArray(), RSAEncryptionPadding.Pkcs1));
            }
            return Convert.ToBase64String(encrypted.ToArray());
        }
        catch (CryptographicException exception)
        {
            throw new UnionException(StatusCodes.Status502BadGateway,
                "Union center returned an invalid OAuth public key.", exception);
        }
    }

    private static string AppendQuery(Uri uri, IEnumerable<KeyValuePair<string, string>> values)
    {
        var query = string.Join('&', values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var builder = new UriBuilder(uri) { Query = query };
        return builder.Uri.AbsoluteUri;
    }

    private static void ValidateConsentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 256 || !path.StartsWith('/')
            || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#'))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union OAuth consent path is invalid.");
        }
    }

    private static string InteractionKey(Guid interactionId) => $"union:oauth-interaction:{interactionId:N}";
}
