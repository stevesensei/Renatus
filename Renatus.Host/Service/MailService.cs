using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed class MailService(IOptions<SmtpOptions> options, IHostEnvironment environment)
{
    private static readonly Regex Placeholder = new(
        @"\{\{\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task SendAsync(
        string recipientEmail,
        MailType mailType,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variables);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(recipientEmail)
            || !new EmailAddressAttribute().IsValid(recipientEmail)
            || !MailboxAddress.TryParse(recipientEmail, out var recipient))
        {
            throw new MailException(400, "收件邮箱格式不正确。");
        }

        var (fileName, subjectTemplate) = mailType switch
        {
            MailType.EmailVerification => ("email-verification.html", "{{SiteName}} 邮箱验证码"),
            _ => throw new MailException(400, $"不支持的邮件类型：{mailType}。"),
        };

        string htmlTemplate;
        try
        {
            var templatePath = Path.Combine(environment.ContentRootPath, "MailTemplates", fileName);
            htmlTemplate = await File.ReadAllTextAsync(templatePath, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new MailException(500, $"无法读取邮件模板：{fileName}。", exception);
        }

        var missingVariables = Placeholder.Matches(htmlTemplate + subjectTemplate)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !variables.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (missingVariables.Length > 0)
        {
            throw new MailException(400, $"邮件模板缺少变量：{string.Join(", ", missingVariables)}。");
        }

        var subject = Placeholder.Replace(subjectTemplate, match => variables[match.Groups["name"].Value]);
        if (subject.Contains('\r') || subject.Contains('\n'))
        {
            throw new MailException(400, "邮件主题变量不能包含换行符。");
        }

        var html = Placeholder.Replace(htmlTemplate,
            match => WebUtility.HtmlEncode(variables[match.Groups["name"].Value]));
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Host)
            || smtp.Port is < 1 or > 65535
            || !Enum.IsDefined(SecureSocketOptions.Auto)
            || string.IsNullOrWhiteSpace(smtp.Username)
            || string.IsNullOrWhiteSpace(smtp.Password)
            || string.IsNullOrWhiteSpace(smtp.FromEmail)
            || !new EmailAddressAttribute().IsValid(smtp.FromEmail)
            || !MailboxAddress.TryParse(smtp.FromEmail, out var sender))
        {
            throw new MailException(500, "Smtp 配置不完整或无效，请检查 Host、Port、Security、Username、Password 和 FromEmail。");
        }

        sender.Name = smtp.FromName;
        using var message = new MimeMessage();
        message.From.Add(sender);
        message.To.Add(recipient);
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = html };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptions.Auto, cancellationToken);
        await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
