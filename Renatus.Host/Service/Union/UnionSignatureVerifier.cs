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
    private const string PublicKeyCacheKey = "union:center-signature-public-key";
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

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

        var signedData = BuildSignedData(body.Span, timestampValue, nonce);
        var publicKey = await GetPublicKeyAsync(options, false, cancellationToken);
        if (!Verify(publicKey, signedData, signature))
        {
            publicKey = await GetPublicKeyAsync(options, true, cancellationToken);
            if (!Verify(publicKey, signedData, signature))
            {
                throw new UnionException(StatusCodes.Status403Forbidden, "Union signature verification failed.");
            }
        }

        var nonceKey = $"union:signature-nonce:{nonce}";
        bool accepted;
        try
        {
            accepted = await _database.StringSetAsync(
                nonceKey, "1", TimeSpan.FromSeconds(60), When.NotExists);
        }
        catch (RedisException exception)
        {
            throw new UnionException(
                StatusCodes.Status503ServiceUnavailable,
                "Union replay protection is unavailable.",
                exception);
        }

        if (!accepted)
        {
            throw new UnionException(StatusCodes.Status409Conflict, "Union message nonce was already used.");
        }
    }

    private async Task<string> GetPublicKeyAsync(
        UnionOptions options,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (!refresh && memoryCache.TryGetValue<string>(PublicKeyCacheKey, out var cached))
        {
            return cached!;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && memoryCache.TryGetValue<string>(PublicKeyCacheKey, out cached))
            {
                return cached!;
            }

            if (refresh
                && DateTimeOffset.UtcNow - _lastRefreshAt < TimeSpan.FromSeconds(15)
                && memoryCache.TryGetValue<string>(PublicKeyCacheKey, out cached))
            {
                return cached!;
            }

            var metadata = await centerClient.GetHostMetadataAsync(options, cancellationToken);
            ValidatePublicKey(metadata.SignaturePublicKey);
            memoryCache.Set(PublicKeyCacheKey, metadata.SignaturePublicKey, TimeSpan.FromMinutes(5));
            _lastRefreshAt = DateTimeOffset.UtcNow;
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

    private static bool Verify(string publicKeyPem, byte[] data, byte[] signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static void ValidatePublicKey(string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize < 2048)
            {
                throw new CryptographicException("RSA key is too small.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new UnionException(
                StatusCodes.Status502BadGateway,
                "Union center returned an invalid signature key.",
                exception);
        }
    }
}
