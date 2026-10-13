namespace Renatus.Host.Data.Config;

public sealed class UnionOptions
{
    public const string SectionName = "Union";

    public bool Enabled { get; set; }
    public string ApiRoot { get; set; } = "https://skin.mualliance.ltd/api/union";
    public string MemberCode { get; set; } = string.Empty;
    public string MemberKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public bool OAuth2Enabled { get; set; }
    public string OAuth2SigningPrivateKeyPem { get; set; } = string.Empty;
}
