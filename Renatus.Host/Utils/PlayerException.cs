namespace Renatus.Host.Utils;

public sealed class PlayerException : Exception
{
    public PlayerException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public PlayerException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
