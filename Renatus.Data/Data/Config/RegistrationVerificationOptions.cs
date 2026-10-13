namespace Renatus.Host.Data.Config;

public sealed class RegistrationVerificationOptions
{
    public int CodeTtlMinutes { get; set; } = 5;
    public int EmailCooldownSeconds { get; set; } = 60;
    public int IpWindowSeconds { get; set; } = 3600;
    public int IpMaxRequests { get; set; } = 10;
    public int MaxVerificationAttempts { get; set; } = 5;
}
