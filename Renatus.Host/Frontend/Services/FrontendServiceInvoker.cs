using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Renatus.Host.Data;
using Renatus.Host.Utils;

namespace Renatus.Host.Frontend.Services;

public static class FrontendServiceInvoker
{
    public static async Task<T> InvokeAsync<T>(
        IServiceScopeFactory scopeFactory,
        Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            return await action(scope.ServiceProvider);
        }
        catch (AuthException exception)
        {
            throw ToApiException(exception.StatusCode, exception.Message);
        }
        catch (PlayerException exception)
        {
            throw ToApiException(exception.StatusCode, exception.Message);
        }
        catch (TextureException exception)
        {
            throw ToApiException(exception.StatusCode, exception.Message);
        }
    }

    private static ApiException ToApiException(int statusCode, string message)
    {
        var code = statusCode >= 500 ? ResponseCode.InternalError : ResponseCode.ResultError;
        var httpStatusCode = Enum.IsDefined(typeof(HttpStatusCode), statusCode)
            ? (HttpStatusCode)statusCode
            : (HttpStatusCode?)null;
        return httpStatusCode is null
            ? new ApiException(code, message)
            : new ApiException(code, message, httpStatusCode.Value);
    }
}
