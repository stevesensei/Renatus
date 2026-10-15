namespace Renatus.Host.Data.Config;

public sealed class YggdrasilConnectOptions
{
    public const string SectionName = "YggdrasilConnect";

    public bool Enabled { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string SharedClientId { get; set; } = string.Empty;
    public List<string> SharedClientRedirectUris { get; set; } = [];
    public bool AllowImplicitFlow { get; set; }
    public string SigningPrivateKeyPem { get; set; } = string.Empty;
    public string EncryptionPrivateKeyPem { get; set; } = string.Empty;
    public int AccessTokenTtlSeconds { get; set; } = 3600;
    public int RefreshTokenTtlSeconds { get; set; } = 604800;
    public int AuthorizationCodeTtlSeconds { get; set; } = 600;
    public int DeviceCodeTtlSeconds { get; set; } = 600;
}
