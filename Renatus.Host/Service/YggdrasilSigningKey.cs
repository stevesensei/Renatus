using System.Security.Cryptography;
using System.Text;

namespace Renatus.Host.Service;

public sealed class YggdrasilSigningKey : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new();
    private RSA _jwtRsa = RSA.Create(2048);
    private RSA _textureRsa = RSA.Create(2048);

    public void Initialize(string? signingPrivateKeyPem)
    {
        var rsa = RSA.Create(2048);
        if (!string.IsNullOrWhiteSpace(signingPrivateKeyPem))
        {
            rsa.ImportFromPem(signingPrivateKeyPem);
        }

        Replace(ref _jwtRsa, rsa);
    }

    public void InitializeTexture(string signingPrivateKeyPem)
    {
        var rsa = RSA.Create(2048);
        rsa.ImportFromPem(signingPrivateKeyPem);
        Replace(ref _textureRsa, rsa);
    }

    public string PublicKeyPem => ExportPublicKeyPem(false);

    public string TexturePublicKeyPem => ExportPublicKeyPem(true);

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
