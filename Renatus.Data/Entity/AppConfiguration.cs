namespace Renatus.Host.Entity;

public sealed class AppConfiguration
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}
