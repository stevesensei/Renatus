namespace Renatus.Host.Utils;

public sealed class UnionException(int statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
