namespace Renatus.Host.Data.Config;

public sealed class UnionOptions
{
    public const string SectionName = "Union";

    public bool Enabled { get; set; }
    /// <summary>The trusted center origin/path; callback public keys are fetched and cached only under this normalized root.</summary>
    public string ApiRoot { get; set; } = "https://skin.mualliance.ltd/api/union";
    public string MemberCode { get; set; } = string.Empty;
    /// <summary>Outgoing center authentication secret; administrator response DTOs expose only whether it exists.</summary>
    public string MemberKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 5;
    public bool OAuth2Enabled { get; set; }
    public string OAuth2SigningPrivateKeyPem { get; set; } = string.Empty;
}

