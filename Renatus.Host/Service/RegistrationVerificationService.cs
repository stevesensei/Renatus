using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Service.Cache;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service;

public sealed class RegistrationVerificationService(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<AuthOptions> options,
    AppConfigurationService configurationService,
    MailService mailService,
    ILogger<RegistrationVerificationService> logger)
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    public string EnsureEmailAllowed(string? email)
    {
        var value = email?.Trim() ?? string.Empty;
        if (value.Length > 256 || !new EmailAddressAttribute().IsValid(value))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "A valid email address is required.");
        }

        var restriction = options.Value.RegistrationEmailDomains;
        if (restriction.Enabled)
        {
            var domain = value[(value.LastIndexOf('@') + 1)..];
            var listed = restriction.Domains.Any(item =>
                string.Equals(domain, item.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase));
            var allowed = restriction.Mode switch
            {
                EmailDomainListMode.Whitelist => listed,
                EmailDomainListMode.Blacklist => !listed,
                _ => throw new InvalidOperationException("Invalid registration email domain list mode.")
            };
            if (!allowed)
            {
                throw new AuthException(StatusCodes.Status400BadRequest,
                    "This email domain is not allowed for registration.");
            }
        }

        return value;
    }

    public async Task<RegistrationCodeResponse> SendAsync(
        string? email,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recipient = EnsureEmailAllowed(email);
        if (clientIp is null)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Unable to determine the client IP address.");
        }

        var settings = options.Value.RegistrationVerification;
        if (settings.CodeTtlMinutes <= 0 || settings.EmailCooldownSeconds <= 0
            || settings.IpWindowSeconds <= 0 || settings.IpMaxRequests <= 0
            || settings.MaxVerificationAttempts <= 0)
        {
            throw new AuthException(StatusCodes.Status500InternalServerError,
                "Registration verification settings must be positive.");
        }

        var address = clientIp.IsIPv4MappedToIPv6 ? clientIp.MapToIPv4() : clientIp;
        var ipKey = $"auth:registration:ip:{Hash(address.ToString())}";
        var emailHash = Hash(YggdrasilFormat.Normalize(recipient));
        var sendKey = $"auth:registration:send:{emailHash}";
        var codeKey = $"auth:registration:code:{emailHash}";
        var lease = Guid.NewGuid().ToString("N");
        // Reserve the email while SMTP is in progress; MailKit's default timeout is 120 seconds.
        var acquired = await EvaluateAsync(RegistrationVerificationScripts.AcquireSend,
            [ipKey, sendKey],
            [settings.IpMaxRequests, settings.IpWindowSeconds, Math.Max(180, settings.EmailCooldownSeconds), lease]);
        if (acquired != 1)
        {
            throw new AuthException(StatusCodes.Status429TooManyRequests, acquired == -1
                ? "A verification email was recently requested for this address. Please try again later."
                : "Too many verification email requests from this IP address. Please try again later.");
        }

        try
        {
            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            var site = await configurationService.GetSiteOptionsAsync(cancellationToken);
            await mailService.SendAsync(recipient, MailType.EmailVerification, new Dictionary<string, string>
            {
                ["SiteName"] = site.Title,
                ["VerificationCode"] = code,
                ["ExpiresInMinutes"] = settings.CodeTtlMinutes.ToString(CultureInfo.InvariantCulture),
                ["Year"] = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(settings.CodeTtlMinutes);
            var completed = await EvaluateAsync(RegistrationVerificationScripts.CompleteSend,
                [sendKey, codeKey],
                [lease, CodeHash(recipient, code), settings.MaxVerificationAttempts,
                    checked(settings.CodeTtlMinutes * 60), settings.EmailCooldownSeconds]);
            if (completed != 1)
            {
                throw new InvalidOperationException("The verification email send reservation has expired.");
            }

            return new RegistrationCodeResponse(expiresAt, settings.EmailCooldownSeconds);
        }
        catch (Exception exception)
        {
            try
            {
                await _database.ScriptEvaluateAsync(RegistrationVerificationScripts.ReleaseSend, [sendKey], [lease]);
            }
            catch (RedisException)
            {
                logger.LogWarning("Unable to release a registration email reservation; it will expire automatically.");
            }

            if (exception is OperationCanceledException) throw;
            logger.LogError(exception, "Unable to send a registration verification email.");
            throw new AuthException(StatusCodes.Status503ServiceUnavailable,
                "Unable to send the verification email. Please try again later.");
        }
    }

    public static void EnsureCodeFormat(string? code)
    {
        if (code is null || code.Length != 6 || code.Any(character => character is < '0' or > '9'))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "A six-digit email verification code is required.");
        }
    }

    public async Task VerifyAndConsumeAsync(string email, string? code, CancellationToken cancellationToken)
    {
        EnsureCodeFormat(code);
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"auth:registration:code:{Hash(YggdrasilFormat.Normalize(email))}";
        var result = await EvaluateAsync(RegistrationVerificationScripts.ConsumeCode,
            [key], [CodeHash(email, code!)]);
        if (result != 1)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, result == -1
                ? "The email verification code has expired or is unavailable. Please request a new code."
                : "The email verification code is incorrect.");
        }
    }

    private static string CodeHash(string email, string code) => Hash($"{YggdrasilFormat.Normalize(email)}:{code}");

    private async Task<long> EvaluateAsync(string script, RedisKey[] keys, RedisValue[] values)
    {
        try
        {
            return (long)await _database.ScriptEvaluateAsync(script, keys, values);
        }
        catch (RedisException exception)
        {
            logger.LogError(exception, "Registration verification cache is unavailable.");
            throw new AuthException(StatusCodes.Status503ServiceUnavailable,
                "Email verification is temporarily unavailable. Please try again later.");
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
