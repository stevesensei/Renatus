using Renatus.Front.Models;
using System.Net;

namespace Renatus.Front.Services;

public sealed class ApiException : Exception
{
    public ResponseCode Code { get; }
    public HttpStatusCode? StatusCode { get; }

    public ApiException(ResponseCode code, string message) : base(message)
    {
        Code = code;
    }

    public ApiException(ResponseCode code, string message, HttpStatusCode statusCode) : this(code, message)
    {
        StatusCode = statusCode;
    }
}
