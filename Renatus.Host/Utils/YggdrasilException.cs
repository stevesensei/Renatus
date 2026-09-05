using Renatus.Host.Data;
using Renatus.Host.Service;

namespace Renatus.Host.Utils;

public sealed class YggdrasilException : Exception
{
    public YggdrasilException(int statusCode, string error, string errorMessage, string? cause = null)
        : base(errorMessage)
    {
        StatusCode = statusCode;
        Error = error;
        ErrorMessage = errorMessage;
        Cause = cause;
    }

    public int StatusCode { get; }
    public string Error { get; }
    public string ErrorMessage { get; }
    public string? Cause { get; }

    public YggdrasilErrorResponse ToResponse()
    {
        return new YggdrasilErrorResponse(Error, ErrorMessage, Cause);
    }

    public static YggdrasilException InvalidToken()
    {
        return new YggdrasilException(StatusCodes.Status403Forbidden, "ForbiddenOperationException", "Invalid token.");
    }

    public static YggdrasilException InvalidCredentials()
    {
        return new YggdrasilException(StatusCodes.Status403Forbidden, "ForbiddenOperationException", "Invalid credentials. Invalid username or password.");
    }
}
