using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed class YggdrasilSigningKey : IDisposable
{
    private readonly RSA _rsa;

    public YggdrasilSigningKey(IOptions<YggdrasilOptions> options)
    {
        _rsa = RSA.Create(2048);
        if (!string.IsNullOrWhiteSpace(options.Value.SigningPrivateKeyPem))
        {
            _rsa.ImportFromPem(options.Value.SigningPrivateKeyPem);
        }
    }

    public string PublicKeyPem => ExportPublicKeyPem();

    public byte[] SignJwtInput(byte[] input)
    {
        return _rsa.SignData(input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public bool VerifyJwtInput(byte[] input, byte[] signature)
    {
        return _rsa.VerifyData(input, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public string SignTextureValue(string value)
    {
        var signature = _rsa.SignData(Encoding.UTF8.GetBytes(value), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private string ExportPublicKeyPem()
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----BEGIN PUBLIC KEY-----");
        builder.AppendLine(Convert.ToBase64String(_rsa.ExportSubjectPublicKeyInfo(), Base64FormattingOptions.InsertLineBreaks));
        builder.AppendLine("-----END PUBLIC KEY-----");
        return builder.ToString();
    }
}
