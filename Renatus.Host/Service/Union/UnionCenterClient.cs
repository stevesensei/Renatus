using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed class UnionCenterClient(HttpClient httpClient, IHostEnvironment environment)
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Fetches the center directory using the member secret; callers validate its contents before persisting.</summary>
    public Task<UnionServerListResponse> GetServerListAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionServerListResponse>(
            HttpMethod.Get, options, "serverlist", null, true, cancellationToken);
    }

    /// <summary>Fetches the shared texture key only; this response must never replace a login JWT signing key.</summary>
    public Task<UnionPrivateKeyResponse> GetPrivateKeyAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionPrivateKeyResponse>(
            HttpMethod.Get, options, "privatekey", null, true, cancellationToken);
    }

    /// <summary>Sends the complete UUID-to-name snapshot; a failed center request leaves local player changes committed.</summary>
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

    /// <summary>Requests center diagnostics and returns only a status, preventing upstream bodies and headers from exposing secrets.</summary>
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

        return new UnionAdminOperationResponse("ok", (int)response.StatusCode);
    }

    /// <summary>Obtains the callback verification key from the configured trusted root, without forwarding the member secret.</summary>
    public Task<UnionHostMetadataResponse> GetHostMetadataAsync(
        UnionOptions options,
        CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionHostMetadataResponse>(
            HttpMethod.Get, options, string.Empty, null, false, cancellationToken);
    }

    /// <summary>Reads the center-owned profile document for an already ownership-checked local Minecraft UUID.</summary>
    public Task<JsonElement> GetProfileDetailAsync(UnionOptions options, Guid minecraftUuid, CancellationToken cancellationToken)
    {
        return SendJsonAsync<JsonElement>(HttpMethod.Get, options,
            $"profile/detail/{minecraftUuid:N}", null, true, cancellationToken);
    }

    /// <summary>Reads same-name unbound profiles while preserving the center's extensible wire fields.</summary>
    public Task<JsonElement> GetUnmappedProfilesByNameAsync(UnionOptions options, string name, CancellationToken cancellationToken)
    {
        return SendJsonAsync<JsonElement>(HttpMethod.Get, options,
            $"profile/unmapped/byname/{Uri.EscapeDataString(name)}", null, true, cancellationToken);
    }

    /// <summary>Creates a center binding token for an owned local Minecraft UUID.</summary>
    public Task<UnionBindTokenResponse> CreateBindTokenAsync(UnionOptions options, Guid minecraftUuid, CancellationToken cancellationToken)
    {
        return SendJsonBodyAsync<UnionBindTokenResponse>(HttpMethod.Post, options, "profile/bind",
            new { uuid = minecraftUuid.ToString("N") }, true, cancellationToken);
    }

    /// <summary>Submits a binding token for an owned local Minecraft UUID.</summary>
    public Task BindToAsync(UnionOptions options, Guid minecraftUuid, string token, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Post, options, "profile/bindto",
            new { uuid = minecraftUuid.ToString("N"), token }, true, cancellationToken);
    }

    /// <summary>Removes the center binding for an owned local Minecraft UUID.</summary>
    public Task UnbindAsync(UnionOptions options, Guid minecraftUuid, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Post, options, "profile/unbind",
            new { uuid = minecraftUuid.ToString("N") }, true, cancellationToken);
    }

    /// <summary>Requests a center UUID remap from an owned local UUID to a selected mapped UUID.</summary>
    public Task RequestRemapAsync(UnionOptions options, Guid minecraftUuid, Guid targetMinecraftUuid, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Post, options, "profile/remapuuid",
            new { me = minecraftUuid.ToString("N"), target = targetMinecraftUuid.ToString("N") }, true, cancellationToken);
    }

    /// <summary>Resolves the member code and then reads its public center security level.</summary>
    public async Task<int> GetSecurityLevelAsync(UnionOptions options, CancellationToken cancellationToken)
    {
        var codeResponse = await SendJsonBodyAsync<JsonElement>(HttpMethod.Post, options, "code",
            new { token = options.MemberKey }, false, cancellationToken);
        if (!codeResponse.TryGetProperty("code", out var codeElement)
            || codeElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(codeElement.GetString()))
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid member code.");
        }

        var levelResponse = await SendJsonAsync<JsonElement>(HttpMethod.Get, options,
            $"backend/{Uri.EscapeDataString(codeElement.GetString()!)}/security/level", null, false, cancellationToken);
        if (levelResponse.ValueKind == JsonValueKind.Number && levelResponse.TryGetInt32(out var level))
        {
            return level;
        }
        if (levelResponse.ValueKind == JsonValueKind.Object
            && levelResponse.TryGetProperty("level", out var nested)
            && nested.TryGetInt32(out level))
        {
            return level;
        }
        throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned an invalid security level.");
    }

    /// <summary>Returns the authoritative center blacklist page without inventing local pagination fields.</summary>
    public Task<JsonElement> QueryBlacklistAsync(UnionOptions options, string query, int page, CancellationToken cancellationToken)
    {
        var path = $"blacklist/query?q={Uri.EscapeDataString(query)}&page={page}";
        return SendJsonAsync<JsonElement>(HttpMethod.Get, options, path, null, true, cancellationToken);
    }

    /// <summary>Creates a remote blacklist row using the original center wire fields.</summary>
    public Task CreateBlacklistEntryAsync(UnionOptions options, UnionBlacklistCreateRequest request, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Post, options, "blacklist/restful",
            new { email = request.Email, reason = request.Reason }, true, cancellationToken);
    }

    /// <summary>Invalidates a positive remote blacklist row identifier.</summary>
    public Task InvalidateBlacklistEntryAsync(UnionOptions options, long id, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Put, options, $"blacklist/invalidate/{id}", null, true, cancellationToken);
    }

    /// <summary>Deletes a positive remote blacklist row identifier.</summary>
    public Task DeleteBlacklistEntryAsync(UnionOptions options, long id, CancellationToken cancellationToken)
    {
        return SendBodyAsync(HttpMethod.Delete, options, $"blacklist/restful/{id}", null, true, cancellationToken);
    }

    /// <summary>Fetches the Union web OAuth encryption key from the configured trusted center root without member credentials.</summary>
    public Task<UnionOAuthBackendKeyResponse> GetOAuthBackendKeyAsync(UnionOptions options, CancellationToken cancellationToken)
    {
        return SendJsonAsync<UnionOAuthBackendKeyResponse>(HttpMethod.Get, options,
            "oauth2/backend", null, false, cancellationToken);
    }

    /// <summary>Restricts center URLs to HTTPS, or numeric loopback HTTP during development; credentials and URL suffixes are rejected.</summary>
    public Uri ValidateApiRoot(UnionOptions options)
    {
        if (options.ApiRoot is null || options.ApiRoot.Length > 2048
            || !Uri.TryCreate(options.ApiRoot.Trim(), UriKind.Absolute, out var root)
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

    /// <summary>Parses a size-bounded buffered center response and converts malformed JSON into a safe protocol error.</summary>
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

    private Task<T> SendJsonBodyAsync<T>(
        HttpMethod method,
        UnionOptions options,
        string relativePath,
        object body,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(body, options: JsonOptions);
        return SendJsonOwnedContentAsync<T>(method, options, relativePath, content, authenticate, cancellationToken);
    }

    private async Task<T> SendJsonOwnedContentAsync<T>(
        HttpMethod method,
        UnionOptions options,
        string relativePath,
        HttpContent content,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        using (content)
        {
            return await SendJsonAsync<T>(method, options, relativePath, content, authenticate, cancellationToken);
        }
    }

    private async Task SendBodyAsync(
        HttpMethod method,
        UnionOptions options,
        string relativePath,
        object? body,
        bool authenticate,
        CancellationToken cancellationToken)
    {
        using var content = body is null ? null : JsonContent.Create(body, options: JsonOptions);
        using var response = await SendAsync(method, options, relativePath, content, authenticate, cancellationToken);
        EnsureSuccess(response);
    }

    /// <summary>Applies one timeout to headers and the entire body, buffers at most 1 MiB, and never logs the member secret.</summary>
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

        if (authenticate && (string.IsNullOrWhiteSpace(options.MemberKey) || options.MemberKey.Length > 1024 || options.MemberKey.Any(char.IsControl)))
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
            var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            try
            {
                if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                {
                    throw new UnionException(StatusCodes.Status502BadGateway, "Union center response is too large.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
                {
                    if (buffer.Length + read > MaximumResponseBytes)
                    {
                        throw new UnionException(StatusCodes.Status502BadGateway, "Union center response is too large.");
                    }

                    buffer.Write(chunk, 0, read);
                }

                var originalContent = response.Content;
                response.Content = new ByteArrayContent(buffer.ToArray());
                originalContent.Dispose();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UnionException(StatusCodes.Status504GatewayTimeout, "Union center request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center is unavailable.", exception);
        }
        catch (IOException exception)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center response could not be read.", exception);
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

