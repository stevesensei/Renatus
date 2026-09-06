using Renatus.Host.Frontend.Models;
using Renatus.Host.Data;
using System.Net;

namespace Renatus.Host.Frontend.Services;

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
