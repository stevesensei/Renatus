namespace Renatus.Host.Data.Config;

public sealed class YggdrasilOptions
{
    public string PublicBaseUrl { get; set; } = "https://localhost";
    public int AccessTokenExpireHours { get; set; } = 24;
    public int JoinSessionTtlSeconds { get; set; } = 30;
    public int MaxProfileLookupCount { get; set; } = 100;
    public string ServerName { get; set; } = "Renatus";
    public string ImplementationName { get; set; } = "Renatus";
    public string ImplementationVersion { get; set; } = "0.1.0";
    public string HomepageUrl { get; set; } = "";
    public string RegisterUrl { get; set; } = "";
    public bool NonEmailLogin { get; set; } = true;
    public bool LegacySkinApi { get; set; }
    public bool NoMojangNamespace { get; set; }
    public bool EnableMojangAntiFeatures { get; set; }
    public bool EnableProfileKey { get; set; }
    public bool UsernameCheck { get; set; }
    public string SigningPrivateKeyPem { get; set; } = "";
    public string[] SkinDomains { get; set; } = [];
}
