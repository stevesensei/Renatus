using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(path.TrimStart('/'), cancellationToken);
        return await UnwrapAsync<T>(response, cancellationToken);
    }

    public async Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(path.TrimStart('/'), body, JsonOptions, cancellationToken);
        return await UnwrapAsync<T>(response, cancellationToken);
    }

    public async Task<T> PutAsync<T>(string path, object? body, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(path.TrimStart('/'), body, JsonOptions, cancellationToken);
        return await UnwrapAsync<T>(response, cancellationToken);
    }

    public async Task<T> DeleteAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(path.TrimStart('/'), cancellationToken);
        return await UnwrapAsync<T>(response, cancellationToken);
    }

    private static async Task<T> UnwrapAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(ResponseCode.InternalError, $"HTTP {(int)response.StatusCode}");
        }

        var envelope = await response.Content.ReadFromJsonAsync<GlobalResponse<T>>(JsonOptions, cancellationToken);
        if (envelope is null)
        {
            throw new ApiException(ResponseCode.InternalError, "Empty response body.");
        }

        if (!envelope.IsSuccess)
        {
            throw new ApiException(envelope.Code, envelope.Message);
        }

        return envelope.Data!;
    }
}
