using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed class UnionCenterClient(HttpClient httpClient, IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<UnionServerListResponse> GetServerListAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionServerListResponse>(
            HttpMethod.Get, options, "serverlist", null, true, cancellationToken);
    }

    public Task<UnionPrivateKeyResponse> GetPrivateKeyAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionPrivateKeyResponse>(
            HttpMethod.Get, options, "privatekey", null, true, cancellationToken);
    }

    public async Task SyncProfilesAsync(
        UnionOptions options,
        Dictionary<string, string> profileList,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new UnionProfileSyncRequest(profileList), options: JsonOptions);
        using var response = await SendAsync(
            HttpMethod.Post, options, "sync", content, true, cancellationToken);
        EnsureSuccess(response);
    }

    public async Task<UnionAdminOperationResponse> DiagnoseAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new { }, options: JsonOptions);
        using var response = await SendAsync(
            HttpMethod.Post, options, "diagnose", content, true, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new UnionAdminOperationResponse("error", (int)response.StatusCode);
        }

        JsonElement? data = null;
        if (response.Content.Headers.ContentLength is > 0)
        {
            try
            {
                data = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        return new UnionAdminOperationResponse("ok", (int)response.StatusCode, data);
    }

    public Task<UnionHostMetadataResponse> GetHostMetadataAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionHostMetadataResponse>(
            HttpMethod.Get, options, string.Empty, null, false, cancellationToken);
    }

    public Uri ValidateApiRoot(UnionOptions options)
    {
        if (!Uri.TryCreate(options.ApiRoot.Trim(), UriKind.Absolute, out var root)
            || !string.IsNullOrEmpty(root.UserInfo)
            || !string.IsNullOrEmpty(root.Query)
            || !string.IsNullOrEmpty(root.Fragment))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Union API root is invalid.");
        }

        var secure = root.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var developmentLoopback = environment.IsDevelopment()
            && root.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && IPAddress.TryParse(root.Host, out var address)
            && IPAddress.IsLoopback(address);
        if (!secure && !developmentLoopback)
        {
            throw new UnionException(
                StatusCodes.Status400BadRequest,
                "Union API root must use HTTPS (development loopback HTTP is allowed).");
        }

        return new Uri(root.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        UnionOptions options,
        string relativePath,
        HttpContent? content,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            method, options, relativePath, content, authenticate, cancellationToken);
        EnsureSuccess(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new UnionException(
                StatusCodes.Status502BadGateway,
                "Union center returned an invalid response.",
                exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        UnionOptions options,
        string relativePath,
        HttpContent? content,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union integration is disabled.");
        }

        if (authenticate && string.IsNullOrWhiteSpace(options.MemberKey))
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union member key is not configured.");
        }

        var root = ValidateApiRoot(options);
        var target = string.IsNullOrEmpty(relativePath)
            ? new Uri(root.AbsoluteUri.TrimEnd('/'), UriKind.Absolute)
            : new Uri(root, relativePath.TrimStart('/'));
        using var request = new HttpRequestMessage(method, target) { Content = content };
        if (authenticate)
        {
            request.Headers.TryAddWithoutValidation("X-Union-Member-Key", options.MemberKey);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 1, 30)));
        try
        {
            return await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UnionException(StatusCodes.Status504GatewayTimeout, "Union center request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center is unavailable.", exception);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new UnionException(
                StatusCodes.Status502BadGateway,
                $"Union center returned HTTP {(int)response.StatusCode}.");
        }
    }
}
