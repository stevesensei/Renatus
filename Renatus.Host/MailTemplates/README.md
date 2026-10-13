# 邮件发送

参考：[腾讯云 MailKit 调用示例](https://cloud.tencent.com/document/product/1288/114261)。

`MailService` 已注册为 scoped 服务。SMTP 配置来自 `appsettings.json` 的 `Smtp` 节，字段见 `appsettings.TEMPLATE.json5`。腾讯云的 `Username`、`FromEmail` 填写控制台创建的发信地址，`Password` 填写该地址的 SMTP 密码；建议通过 user-secrets 或环境变量 `Smtp__Password` 提供密码。465 端口使用 `SslOnConnect`，587 使用 `StartTls`。

```csharp
await mailService.SendAsync(
    "recipient@example.com",
    MailType.EmailVerification,
    new Dictionary<string, string>
    {
        ["SiteName"] = "HLJUMC",
        ["VerificationCode"] = "123456",
        ["ExpiresInMinutes"] = "5",
        ["Year"] = DateTimeOffset.UtcNow.Year.ToString(),
    },
    cancellationToken);
```

`EmailVerification` 映射到 `email-verification.html`，主题为 `{{SiteName}} 邮箱验证码`。HTML 使用 `{{VariableName}}` 占位符，支持周围空白；变量名为英文字母开头，可包含数字和下划线。替换只执行一次，HTML 正文中的值先做 HTML 编码，主题使用原文且禁止换行。

模板及主题的全部变量都必须通过字典提供，包括站点名称和年份；缺少、null、空白值会在连接 SMTP 之前抛出 `MailException`，消息列出全部缺失变量。多余变量会被忽略。错误邮箱、未知邮件类型也抛出 `MailException`；配置和文件读取错误同样使用此异常。SMTP 连接、认证和发送错误保留 MailKit 原始异常，取消操作保留 `OperationCanceledException`，由调用方处理。

此服务只发送邮件。验证码的生成、存储、有效期和验证由调用方负责，传入的 `ExpiresInMinutes` 应与实际有效期一致。新增邮件类型时，在 `MailType` 中添加枚举值，在 `MailService` 的映射中指定文件和主题，再将 HTML 放入本目录；模板会复制到构建和发布输出。

## 注册邮箱验证

1. `POST /api/auth/register-code`，请求为 `{"email":"recipient@example.com"}`。发送前复用 `Auth:RegistrationEmailDomains` 的白名单/黑名单规则并检查邮箱格式、邮箱重发间隔及 IP 配额。成功返回 `GlobalResponse<RegistrationCodeResponse>`，其中 `data` 只有 `expiresAt` 和 `resendAfterSeconds`，不返回验证码。
2. `POST /api/auth/register` 在原请求字段之外必须提供 `verificationCode`（6 位数字）。邮箱与验证码不匹配、过期、已消费或错误次数用尽时，账户不会创建。错误格式会在读取密码会话之前拒绝；校验成功后验证码立即消费。
3. 站内注册页已加入发送按钮、重发倒计时、邮件发送结果和验证码输入框；验证码由服务端生成，页面不能指定验证码或限流 IP。

`Auth:RegistrationVerification` 默认配置：

| 配置 | 默认值 | 含义 |
| --- | --- | --- |
| `CodeTtlMinutes` | 5 | 验证码有效期，分钟 |
| `EmailCooldownSeconds` | 60 | 同邮箱重发间隔，秒 |
| `IpWindowSeconds` | 3600 | IP 固定计数窗口，秒 |
| `IpMaxRequests` | 10 | 窗口内同 IP 最大发送尝试数 |
| `MaxVerificationAttempts` | 5 | 每个验证码最多错误校验次数 |

配置值必须为正数。SMTP 发送失败也计入 IP 配额，但会释放该邮箱的发送占用；若此前存在有效验证码，会保留旧验证码，成功重发才替换。发送中的邮箱暂时占用至少 180 秒，完成后改为配置的重发间隔，避免慢速 SMTP 导致同邮箱并发发送。

Redis 使用 `auth:registration:code:{SHA256(Normalize(email))}` 保存验证码摘要与剩余尝试次数并设置 TTL；摘要同时包含规范化邮箱和验证码。`send` 键控制邮箱发送占用和重发间隔，`ip` 键保存 IP 窗口计数。配额获取、验证码写入、错误计数和校验成功后的删除均通过 Lua 原子执行，适用于多实例共享 Redis。

接口使用 `HttpContext.Connection.RemoteIpAddress`，页面使用首次服务器渲染时的连接 IP；IPv4 与 IPv4-mapped IPv6 会归为同一地址。应用不会直接信任客户端提交的 `X-Forwarded-For`。若部署在反向代理后，应在服务器配置可信代理与 Forwarded Headers 中间件，使连接 IP 表示真实客户端地址。

业务拒绝保持 HTTP 200，使用 `code: 10001`；发送过程中内部错误使用 `code: 10010`。验证码不会出现在 API 响应中，也不会写入业务日志。账户保存失败或取消发生在验证码消费之后时，需要重新申请验证码。
