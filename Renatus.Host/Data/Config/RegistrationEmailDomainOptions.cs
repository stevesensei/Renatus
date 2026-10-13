namespace Renatus.Host.Data.Config;

public sealed class RegistrationEmailDomainOptions
{
    public bool Enabled { get; set; }
    public EmailDomainListMode Mode { get; set; } = EmailDomainListMode.Whitelist;
    public string[] Domains { get; set; } = [];
}

public enum EmailDomainListMode
{
    Whitelist,
    Blacklist
}
