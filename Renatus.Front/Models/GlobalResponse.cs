using System.Text.Json.Serialization;

namespace Renatus.Front.Models;

public enum ResponseCode
{
    Success = 10000,
    ResultError = 10001,
    InternalError = 10010,
}

public sealed class GlobalResponse<T>
{
    [JsonPropertyName("code")]
    public ResponseCode Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public bool IsSuccess => Code == ResponseCode.Success;
}
