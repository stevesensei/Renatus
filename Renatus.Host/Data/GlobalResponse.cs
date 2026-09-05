using System.Text.Json.Serialization;

namespace Renatus.Host.Data;

public class GlobalResponse<T>
{
    [JsonPropertyName("code")]
    public ResponseCode Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = null!;
    
    [JsonPropertyName("data")]
    public T? Data { get; set; }
    
    public static GlobalResponse<T> Success(T? data = default, string message = "Success")
    {
        return new GlobalResponse<T>
        {
            Code = ResponseCode.Success,
            Message = message,
            Data = data
        };
    }

    public static GlobalResponse<T> Error(T? data = default, string message = "Error")
    {
        return new GlobalResponse<T>
        {
            Code = ResponseCode.InternalError,
            Message = message,
            Data = data
        };
    }
    /// <summary>
    /// 除了系统错误之外的所有错误都是这个代码
    /// </summary>
    /// <param name="data"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public static GlobalResponse<T> ResultError(T? data = default, string message = "Not Found")
    {
        return new GlobalResponse<T>
        {
            Code = ResponseCode.ResultError,
            Message = message,
            Data = data
        };
    }
}