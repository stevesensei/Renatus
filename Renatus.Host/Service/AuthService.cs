using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service;

public sealed class AuthService(
    AppDbContext dbContext,
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<AuthOptions> options)
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    public async Task<SessionKeyResponse> CreateSessionKeyAsync(CancellationToken cancellationToken)
    {
        using var rsa = RSA.Create(2048);
        var sessionKeyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddSeconds(options.Value.SessionKeyTtlSeconds);
        var sessionKey = new AuthSessionKey(rsa.ExportPkcs8PrivateKeyPem(), now.ToUnixTimeSeconds());

        await _database.StringSetAsync(
            SessionKeyRedisKey(sessionKeyId),
            JsonSerializer.Serialize(sessionKey),
            TimeSpan.FromSeconds(options.Value.SessionKeyTtlSeconds));

        return new SessionKeyResponse(sessionKeyId, rsa.ExportSubjectPublicKeyInfoPem(), expiresAt);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var password = await ConsumeEncryptedPasswordAsync(request.SessionKeyId, request.EncryptedPassword, cancellationToken);
        var normalizedLogin = YggdrasilFormat.Normalize(request.Login);
        var user = await dbContext.Users
            .Include(entity => entity.AuthenticationMethods)
            .FirstOrDefaultAsync(
                entity => entity.NormalizedUserName == normalizedLogin || entity.NormalizedEmail == normalizedLogin,
                cancellationToken);

        if (user is null || user.IsDisabled || !VerifyPassword(user, password))
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "Invalid username, email, or password.");
        }

        return CreateAuthResponse(user);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var password = await ConsumeEncryptedPasswordAsync(request.SessionKeyId, request.EncryptedPassword, cancellationToken);
        EnsurePasswordAllowed(password);

        var username = request.Username.Trim();
        var email = request.Email.Trim();
        var nickName = string.IsNullOrWhiteSpace(request.NickName) ? username : request.NickName.Trim();
        var normalizedUsername = YggdrasilFormat.Normalize(username);
        var normalizedEmail = YggdrasilFormat.Normalize(email);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Username and email are required.");
        }

        var exists = await dbContext.Users.AnyAsync(
            entity => entity.NormalizedUserName == normalizedUsername || entity.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (exists)
        {
            throw new AuthException(StatusCodes.Status409Conflict, "Username or email already exists.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = normalizedUsername,
            NickName = nickName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PreferredLanguage = "zh_CN",
            Role = UserRole.Player
        };

        var authenticationMethod = new UserAuthenticationMethod
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = AuthenticationMethodType.Password,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        dbContext.Users.Add(user);
        dbContext.UserAuthenticationMethods.Add(authenticationMethod);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateAuthResponse(user);
    }

    public async Task<AuthUserDto> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        return ToUserDto(user);
    }

    private async Task<string> ConsumeEncryptedPasswordAsync(
        Guid sessionKeyId,
        string encryptedPassword,
        CancellationToken cancellationToken)
    {
        var sessionKeyJson = await _database.StringGetDeleteAsync(SessionKeyRedisKey(sessionKeyId));
        if (!sessionKeyJson.HasValue)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Session key is invalid or expired.");
        }

        var sessionKey = JsonSerializer.Deserialize<AuthSessionKey>(sessionKeyJson!);
        if (sessionKey is null)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Session key is invalid.");
        }

        string payload;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(sessionKey.PrivateKeyPem);
            var cipherText = Convert.FromBase64String(encryptedPassword);
            var plainText = rsa.Decrypt(cipherText, RSAEncryptionPadding.OaepSHA256);
            payload = Encoding.UTF8.GetString(plainText);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Encrypted password is invalid.");
        }

        return ParseTimestampedPassword(payload);
    }

    private string ParseTimestampedPassword(string payload)
    {
        var separatorIndex = payload.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Encrypted password payload is invalid.");
        }

        var timestampText = payload[..separatorIndex];
        if (!long.TryParse(timestampText, out var unixTimeSeconds))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Encrypted password timestamp is invalid.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - unixTimeSeconds) > options.Value.TimestampSkewSeconds)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Encrypted password timestamp is expired.");
        }

        var password = payload[(separatorIndex + 1)..];
        EnsurePasswordAllowed(password);
        return password;
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(options.Value.JwtExpireDays);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: options.Value.JwtIssuer,
            audience: options.Value.JwtAudience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expiresAt,
            ToUserDto(user));
    }

    private static bool VerifyPassword(User user, string password)
    {
        var passwordMethod = user.AuthenticationMethods.FirstOrDefault(method => method.Type == AuthenticationMethodType.Password);
        if (passwordMethod?.PasswordHash is null)
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordMethod.PasswordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static AuthUserDto ToUserDto(User user)
    {
        return new AuthUserDto(user.Id, user.UserName, user.NickName, user.Email, user.Role);
    }

    private static void EnsurePasswordAllowed(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 128)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Password must be between 1 and 128 characters.");
        }
    }

    private static string SessionKeyRedisKey(Guid sessionKeyId)
    {
        return $"auth:session-key:{sessionKeyId:N}";
    }

    private sealed record AuthSessionKey(string PrivateKeyPem, long CreatedUnixTimeSeconds);
}
