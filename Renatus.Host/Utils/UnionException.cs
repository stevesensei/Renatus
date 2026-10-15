namespace Renatus.Host.Utils;

/// <summary>Safe Union business/protocol error. Message text must exclude keys and upstream bodies; member controllers use the HTTP code and Admin controllers use GlobalResponse.</summary>
public sealed class UnionException(int statusCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}

