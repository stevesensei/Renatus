namespace Renatus.Host.Data.Config;

public sealed class RequestResponseLoggingOptions
{
    /// <summary>是否记录请求体(默认关闭,开启时敏感字段会被脱敏)。</summary>
    public bool LogRequestBody { get; set; } = false;

    /// <summary>是否记录响应体(默认关闭,仅记录文本类响应)。</summary>
    public bool LogResponseBody { get; set; } = false;

    /// <summary>请求/响应体的最大记录字节数,超出部分截断。</summary>
    public int MaxBodyBytes { get; set; } = 4096;
}
