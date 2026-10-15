using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Union;

public sealed class UnionSignatureVerifier(
    AppConfigurationService configurationService,
    UnionCenterClient centerClient,
    IMemoryCache memoryCache,
    IConnectionMultiplexer connectionMultiplexer)
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    /// <summary>Verifies RSA-SHA256 over raw body + timestamp + nonce, then atomically reserves the nonce for 60 seconds. Invalid signatures never consume nonces.</summary>
    public async Task VerifyAsync(
        ReadOnlyMemory<byte> body,
        string signatureValue,
        string timestampValue,
        string nonce,
        CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        if (!options.Enabled)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union integration is disabled.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureValue);
        }
        catch (FormatException exception)
        {
            throw new UnionException(StatusCodes.Status403Forbidden, "Union signature verification failed.", exception);
        }

        var root = centerClient.ValidateApiRoot(options).AbsoluteUri;
        var signedData = BuildSignedData(body.Span, timestampValue, nonce);
        var publicKey = await GetPublicKeyAsync(options, root, false, cancellationToken);
        if (!Verify(publicKey, signedData, signature))
        {
            publicKey = await GetPublicKeyAsync(options, root, true, cancellationToken);
            if (!Verify(publicKey, signedData, signature))
            {
                throw new UnionException(StatusCodes.Status403Forbidden, "Union signature verification failed.");
            }
        }

        var rootHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
        bool accepted;
        try
        {
            accepted = await _database.StringSetAsync(
                $"union:signature-nonce:{rootHash}:{nonce}", "1", TimeSpan.FromSeconds(60), When.NotExists);
        }
        catch (RedisException exception)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union replay protection is unavailable.", exception);
        }

        if (!accepted)
        {
            throw new UnionException(StatusCodes.Status409Conflict, "Union message nonce was already used.");
        }
    }

    /// <summary>Caches only the configured root's validated public key. Serialized, throttled refresh permits rotation without fetching on every bad signature.</summary>
    private async Task<string> GetPublicKeyAsync(
        UnionOptions options, string root, bool refresh, CancellationToken cancellationToken)
    {
        var cacheKey = $"union:center-signature-public-key:{root}";
        if (!refresh && memoryCache.TryGetValue<TrustedKey>(cacheKey, out var cached))
        {
            return cached!.Pem;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (memoryCache.TryGetValue<TrustedKey>(cacheKey, out cached)
                && (!refresh || DateTimeOffset.UtcNow - cached!.FetchedAt < TimeSpan.FromSeconds(15)))
            {
                return cached!.Pem;
            }

            var metadata = await centerClient.GetHostMetadataAsync(options, cancellationToken);
            ValidatePublicKey(metadata.SignaturePublicKey);
            memoryCache.Set(cacheKey, new TrustedKey(metadata.SignaturePublicKey, DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
            return metadata.SignaturePublicKey;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private static byte[] BuildSignedData(ReadOnlySpan<byte> body, string timestamp, string nonce)
    {
        var suffix = Encoding.UTF8.GetBytes(timestamp + nonce);
        var value = new byte[body.Length + suffix.Length];
        body.CopyTo(value);
        suffix.CopyTo(value.AsSpan(body.Length));
        return value;
    }

    /// <summary>Malformed signatures and PEMs fail closed; public-key parsing errors cannot escape as internal exceptions.</summary>
    private static bool Verify(string publicKeyPem, byte[] data, byte[] signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Rejects missing/oversized PEMs and RSA keys outside 2048-8192 bits before storing trusted callback metadata.</summary>
    private static void ValidatePublicKey(string? publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Length > 16 * 1024)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid signature key.");
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize is < 2048 or > 8192)
            {
                throw new CryptographicException("RSA key size is outside the supported range.");
            }
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid signature key.", exception);
        }
    }

    private sealed record TrustedKey(string Pem, DateTimeOffset FetchedAt);
}
