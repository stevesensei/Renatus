namespace Renatus.Host.Utils;

public sealed class MailException(int statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
