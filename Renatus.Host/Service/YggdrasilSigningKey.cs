using System.Security.Cryptography;
using System.Text;

namespace Renatus.Host.Service;

public sealed class YggdrasilSigningKey : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private RSA _jwtRsa = RSA.Create(2048);
    private RSA _textureRsa = RSA.Create(2048);
    private bool _jwtInitialized;

    /// <summary>Initializes the immutable local JWT signer once at startup; Union texture rotation never calls or replaces it.</summary>
    public void Initialize(string? signingPrivateKeyPem)
    {
        var rsa = RSA.Create(2048);
        if (!string.IsNullOrWhiteSpace(signingPrivateKeyPem))
        {
            rsa.ImportFromPem(signingPrivateKeyPem);
        }

        _lock.EnterWriteLock();
        try
        {
            if (_jwtInitialized)
            {
                rsa.Dispose();
                throw new InvalidOperationException("The local JWT signing key is already initialized.");
            }
            _jwtRsa.Dispose();
            _jwtRsa = rsa;
            _jwtInitialized = true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Atomically replaces only the texture signer after private-key validation; readers retain a complete old or new RSA instance.</summary>
    public void InitializeTexture(string signingPrivateKeyPem)
    {
        var rsa = RSA.Create(2048);
        rsa.ImportFromPem(signingPrivateKeyPem);
        _ = rsa.ExportPkcs8PrivateKey();
        Replace(ref _textureRsa, rsa);
    }

    /// <summary>Exports the immutable local JWT verifier; this is never the texture key published by protocol metadata.</summary>
    public string PublicKeyPem => ExportPublicKeyPem(false);

    /// <summary>Exports the active local or Union texture verifier used by protocol metadata.</summary>
    public string TexturePublicKeyPem => ExportPublicKeyPem(true);

    /// <summary>Signs local client tokens with RSA SHA-256 independently of the mutable texture key.</summary>
    public byte[] SignJwtInput(byte[] input)
    {
        _lock.EnterReadLock();
        try
        {
            return _jwtRsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Accepts only signatures made by the immutable local JWT key, so frontend HMAC and Union texture keys cannot authorize a client.</summary>
    public bool VerifyJwtInput(byte[] input, byte[] signature)
    {
        _lock.EnterReadLock();
        try
        {
            return _jwtRsa.VerifyData(input, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Signs the UTF-8 property value with the currently selected texture RSA using protocol SHA-1/PKCS#1, under the reader lock.</summary>
    public string SignTextureValue(string value)
    {
        _lock.EnterReadLock();
        try
        {
            var signature = _textureRsa.SignData(
                Encoding.UTF8.GetBytes(value), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signature);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Waits for in-flight signers before releasing both independent RSA key instances.</summary>
    public void Dispose()
    {
        _lock.EnterWriteLock();
        try
        {
            _jwtRsa.Dispose();
            _textureRsa.Dispose();
        }
        finally
        {
            _lock.ExitWriteLock();
            _lock.Dispose();
        }
    }

    /// <summary>Selects JWT versus texture verification key under the reader lock so concurrent Union rotation cannot export a disposed RSA.</summary>
    private string ExportPublicKeyPem(bool textureKey)
    {
        _lock.EnterReadLock();
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("-----BEGIN PUBLIC KEY-----");
            builder.AppendLine(Convert.ToBase64String(
                (textureKey ? _textureRsa : _jwtRsa).ExportSubjectPublicKeyInfo(),
                Base64FormattingOptions.InsertLineBreaks));
            builder.AppendLine("-----END PUBLIC KEY-----");
            return builder.ToString();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Swaps a texture RSA under the writer lock and disposes the old key after existing signers finish.</summary>
    private void Replace(ref RSA target, RSA replacement)
    {
        _lock.EnterWriteLock();
        try
        {
            var previous = target;
            target = replacement;
            previous.Dispose();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
