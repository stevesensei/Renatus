using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Renatus.Host.Service;
using Renatus.Host.Utils;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;
using Renatus.Host.Service.Connect;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/api/user/profile")]
[YggdrasilTextureInput]
public sealed class YggdrasilProfileTextureController(
    YggdrasilService yggdrasilService,
    ConnectSessionService connectSessionService,
    IAuthenticationSchemeProvider schemeProvider) : YggdrasilControllerBase
{
    /// <summary>Parses the protocol multipart upload and delegates local Yggdrasil token verification without using frontend JWT authorization.</summary>
    [HttpPut("{uuid}/{type}")]
    [RequestSizeLimit(YggdrasilTextureInputAttribute.MaxRequestBodySize)]
    public async Task<IActionResult> Upload(string uuid, string type, [FromForm] IFormFile? file,
        [FromForm] string? model, CancellationToken cancellationToken)
    {
        try
        {
            if (file is null) throw new YggdrasilException(400, "IllegalArgumentException", "Texture file is required.");
            var result = await AuthenticateConnectAsync();
            if (result.Succeeded && result.Principal is not null)
                await connectSessionService.UploadTextureAsync(result.Principal, uuid, type, model, file, cancellationToken);
            else await yggdrasilService.UploadProfileTextureAsync(BearerToken(), uuid, type, model, file, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception) { return YggdrasilError(exception); }
    }

    /// <summary>Resets the selected profile's skin or cape with the Yggdrasil token boundary and Mojang error shape.</summary>
    [HttpDelete("{uuid}/{type}")]
    public async Task<IActionResult> Reset(string uuid, string type, CancellationToken cancellationToken)
    {
        try
        {
            var result = await AuthenticateConnectAsync();
            if (result.Succeeded && result.Principal is not null)
                await connectSessionService.ResetTextureAsync(result.Principal, uuid, type, cancellationToken);
            else await yggdrasilService.ResetProfileTextureAsync(BearerToken(), uuid, type, cancellationToken);
            return NoContent();
        }
        catch (YggdrasilException exception) { return YggdrasilError(exception); }
    }

    /// <summary>Requires exactly one Bearer credential; its signature and client binding are checked by the Yggdrasil service.</summary>
    private string BearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || authorization.Length <= 7 || authorization.Contains(','))
            throw YggdrasilException.InvalidToken();
        return authorization[7..].Trim();
    }

    /// <summary>Keeps traditional texture authentication available when the optional Connect validation handler is disabled at startup.</summary>
    private async Task<AuthenticateResult> AuthenticateConnectAsync()
        => await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is null
            ? AuthenticateResult.NoResult()
            : await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
}

public sealed class YggdrasilTextureInputAttribute : ActionFilterAttribute, IAsyncResourceFilter
{
    public const long MaxRequestBodySize = 11 * 1024 * 1024;

    public YggdrasilTextureInputAttribute() => Order = -3000;

    /// <summary>Reads/caches the multipart form before model binding can turn Kestrel's chunked-body 413 exception into a plain model-error string; other malformed forms retain Mojang 400.</summary>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength > MaxRequestBodySize)
        {
            context.Result = UploadError(StatusCodes.Status413PayloadTooLarge);
            return;
        }
        if (HttpMethods.IsPut(request.Method) && request.HasFormContentType)
        {
            try
            {
                await request.ReadFormAsync(context.HttpContext.RequestAborted);
            }
            catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException)
            {
                context.Result = UploadError(IsRequestTooLarge(exception)
                    ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest);
                return;
            }
        }
        await next();
    }

    /// <summary>Preserves transport size-limit 413 (including chunked-body exception chains) and maps other binding failures to Mojang 400 before ApiController's filter; service file-size validation remains 400.</summary>
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        var tooLarge = context.HttpContext.Request.ContentLength > MaxRequestBodySize
            || context.ModelState.Values.SelectMany(value => value.Errors)
                .Any(error => IsRequestTooLarge(error.Exception));
        if (tooLarge || !context.ModelState.IsValid)
        {
            var statusCode = tooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest;
            context.Result = UploadError(statusCode);
        }
    }

    private static ObjectResult UploadError(int statusCode) => new(
        new YggdrasilException(statusCode, "IllegalArgumentException", "Invalid texture upload request.").ToResponse())
        { StatusCode = statusCode };

    /// <summary>Unwraps form-binding exceptions without relying on localized exception messages to identify Kestrel's request-size rejection.</summary>
    private static bool IsRequestTooLarge(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
                return true;
            exception = exception.InnerException;
        }
        return false;
    }
}
