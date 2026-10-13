
namespace Renatus.Host.Data.Config;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "smtp.qcloudmail.com";
    public int Port { get; set; } = 465;
    //public SecureSocketOptions Security { get; set; } = SecureSocketOptions.SslOnConnect;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Renatus";
}
