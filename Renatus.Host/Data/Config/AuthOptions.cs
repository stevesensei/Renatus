namespace Renatus.Host.Data.Config;

public sealed class AuthOptions
{
    public string JwtIssuer { get; set; } = "Renatus";
    public string JwtAudience { get; set; } = "Renatus.Frontend";
    public string JwtSigningKey { get; set; } = string.Empty;
    public int JwtExpireDays { get; set; } = 3;
    public int SessionKeyTtlSeconds { get; set; } = 120;
    public int TimestampSkewSeconds { get; set; } = 120;
}
