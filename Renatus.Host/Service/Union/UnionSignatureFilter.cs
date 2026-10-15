using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed partial class UnionSignatureFilter(UnionSignatureVerifier signatureVerifier) : IAsyncResourceFilter
{
    private const int MaximumBodyBytes = 1024 * 1024;
    private const int MaximumSignatureLength = 4096;

    /// <summary>Checks single bounded signature headers and the past-10/future-30-second window, buffers at most 1 MiB of raw bytes, and authenticates before model binding.</summary>
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (request.ContentLength is > MaximumBodyBytes)
        {
            Reject(context, StatusCodes.Status413PayloadTooLarge, "Union request body is too large.");
            return;
        }

        if (!TryGetSingleHeader(request, "X-Message-Signature", MaximumSignatureLength, out var signature)
            || !TryGetSingleHeader(request, "X-Message-Timestamp", 20, out var timestamp)
            || !TryGetSingleHeader(request, "X-Message-Nonce", 128, out var nonce)
            || !NonceRegex().IsMatch(nonce))
        {
            Reject(context, StatusCodes.Status400BadRequest, "Union signature headers are invalid.");
            return;
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixTimestamp))
        {
            Reject(context, StatusCodes.Status400BadRequest, "Union message timestamp is invalid.");
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (unixTimestamp < now - 10 || unixTimestamp > now + 30)
        {
            Reject(context, StatusCodes.Status403Forbidden, "Union message timestamp is outside the allowed window.");
            return;
        }

        byte[] body;
        try
        {
            request.EnableBuffering(64 * 1024, MaximumBodyBytes);
            await using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, context.HttpContext.RequestAborted);
            body = buffer.ToArray();
            request.Body.Position = 0;
        }
        catch (IOException)
        {
            Reject(context, StatusCodes.Status413PayloadTooLarge, "Union request body is too large.");
            return;
        }

        try
        {
            await signatureVerifier.VerifyAsync(
                body, signature, timestamp, nonce, context.HttpContext.RequestAborted);
        }
        catch (UnionException exception)
        {
            Reject(context, exception.StatusCode, exception.Message);
            return;
        }

        await next();
    }

    private static bool TryGetSingleHeader(
        HttpRequest request,
        string name,
        int maximumLength,
        out string value)
    {
        var values = request.Headers[name];
        value = values.Count == 1 ? values[0] ?? string.Empty : string.Empty;
        return value.Length > 0 && value.Length <= maximumLength;
    }

    private static void Reject(ResourceExecutingContext context, int statusCode, string message)
    {
        context.Result = new ObjectResult(new { error = message }) { StatusCode = statusCode };
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex NonceRegex();
}

