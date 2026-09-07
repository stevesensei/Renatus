using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Middleware;

/// <summary>
/// 记录所有 HTTP 请求与响应的中间件,输出到 Serilog 文件 sink。
/// 默认只记录元数据(方法/URL/状态码/耗时等);请求体、响应体需在配置中开启,
/// 开启后仅记录文本类内容,并对敏感字段(密码/token)脱敏。
/// </summary>
public sealed partial class RequestResponseLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestResponseLoggingMiddleware> logger,
    IOptions<RequestResponseLoggingOptions> options)
{
    [GeneratedRegex("(\"(?:password|accessToken|clientToken)\"\\s*:\\s*)\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveJsonFieldRegex();

    [GeneratedRegex("((?:^|[?&])password=)[^&\\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveFormPasswordRegex();

    [GeneratedRegex("((?:^|[?&])key=)[^&\\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveQueryKeyRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var loggingOptions = options.Value;
        var stopwatch = Stopwatch.StartNew();

        var requestBody = loggingOptions.LogRequestBody
            ? await ReadRequestBodyAsync(request, loggingOptions.MaxBodyBytes)
            : null;

        // WebSocket 升级请求(SignalR)不能替换响应流,否则会破坏 Blazor 交互电路
        var shouldBufferResponse = loggingOptions.LogResponseBody
            && !context.WebSockets.IsWebSocketRequest
            && response.StatusCode != StatusCodes.Status101SwitchingProtocols;

        var originalBody = response.Body;
        MemoryStream? buffer = null;
        if (shouldBufferResponse)
        {
            buffer = new MemoryStream();
            response.Body = buffer;
        }

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            string? responseBody = null;
            if (shouldBufferResponse && buffer is not null)
            {
                responseBody = await ReadResponseBodyAsync(response, buffer, originalBody, loggingOptions.MaxBodyBytes);
                response.Body = originalBody;
            }

            var url = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";
            logger.LogInformation(
                "HTTP {Method} {Url} -> {StatusCode} {ContentType} in {ElapsedMs}ms from {ClientIp} ua=\"{UserAgent}\" requestBody={RequestBody} responseBody={ResponseBody}",
                request.Method,
                Redact(url),
                response.StatusCode,
                response.ContentType ?? string.Empty,
                stopwatch.ElapsedMilliseconds,
                context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                request.Headers.UserAgent.ToString(),
                requestBody ?? string.Empty,
                responseBody ?? string.Empty);
        }
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request, int maxBytes)
    {
        if (request.ContentLength is null or 0)
        {
            return null;
        }

        if (!IsTextContent(request.ContentType ?? string.Empty))
        {
            return null;
        }

        request.EnableBuffering();
        var (text, truncated) = await ReadCappedAsync(request.Body, maxBytes);
        request.Body.Position = 0;
        return truncated ? $"{Redact(text)}...[截断]" : Redact(text);
    }

    private static async Task<string?> ReadResponseBodyAsync(
        HttpResponse response,
        MemoryStream buffer,
        Stream originalBody,
        int maxBytes)
    {
        string? text = null;
        if (IsTextContent(response.ContentType ?? string.Empty))
        {
            buffer.Position = 0;
            var (body, truncated) = await ReadCappedAsync(buffer, maxBytes);
            text = truncated ? $"{Redact(body)}...[截断]" : Redact(body);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        await buffer.DisposeAsync();
        return text;
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(Stream stream, int maxBytes)
    {
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
               && (read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total))) > 0)
        {
            total += read;
        }

        var truncated = total > maxBytes;
        var length = Math.Min(total, maxBytes);
        return (Encoding.UTF8.GetString(buffer, 0, length), truncated);
    }

    private static bool IsTextContent(string contentType) =>
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("text", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("form", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);

    private static string Redact(string text)
    {
        text = SensitiveJsonFieldRegex().Replace(text, "$1\"***\"");
        text = SensitiveFormPasswordRegex().Replace(text, "$1***");
        text = SensitiveQueryKeyRegex().Replace(text, "$1***");
        return text.Replace("\r", " ").Replace("\n", " ");
    }
}
