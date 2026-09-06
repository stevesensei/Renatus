namespace Renatus.Host.Utils;

public sealed class TextureException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
