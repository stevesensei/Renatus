using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class ApiException : Exception
{
    public ResponseCode Code { get; }

    public ApiException(ResponseCode code, string message) : base(message)
    {
        Code = code;
    }
}
