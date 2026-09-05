namespace Renatus.Host.Utils;

public sealed class AuthException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
