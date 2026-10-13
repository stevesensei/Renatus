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
