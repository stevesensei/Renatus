using System.IdentityModel.Tokens.Jwt;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Entity;
using Renatus.Host.Service.Cache;
using Renatus.Host.Service.Union;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service;

public sealed partial class AuthService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<AuthOptions> options,
    ICacheService cacheService,
    IMapper mapper,
    RegistrationVerificationService registrationVerificationService,
    YggdrasilLifecycleService yggdrasilLifecycle,
    UnionService unionService)
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        var username = request.Username.Trim();
        var email = registrationVerificationService.EnsureEmailAllowed(request.Email);
        var nickName = string.IsNullOrWhiteSpace(request.NickName) ? username : request.NickName.Trim();
        var normalizedUsername = YggdrasilFormat.Normalize(username);
        var normalizedEmail = YggdrasilFormat.Normalize(email);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Username and email are required.");
        }

        RegistrationVerificationService.EnsureCodeFormat(request.VerificationCode);
        var password = await ConsumeEncryptedPasswordAsync(request.SessionKeyId, request.EncryptedPassword, cancellationToken);
        EnsurePasswordAllowed(password);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var exists = await dbContext.Users.AnyAsync(
            entity => entity.NormalizedUserName == normalizedUsername || entity.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (exists)
        {
            throw new AuthException(StatusCodes.Status409Conflict, "Username or email already exists.");
        }

        await registrationVerificationService.VerifyAndConsumeAsync(email, request.VerificationCode, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = username,
            NormalizedUserName = normalizedUsername,
            NickName = nickName,
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        return ToUserDto(user);
    }

    public async Task<AccountProfileDto> GetAccountProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        return ToAccountProfileDto(user);
    }

    /// <summary>Commits verified email changes with hard Yggdrasil revocation; the frontend account response retains its own JWT/envelope boundary.</summary>
    public async Task<AccountProfileDto> UpdateAccountProfileAsync(
        Guid userId,
        UpdateAccountProfileRequest request,
        CancellationToken cancellationToken)
    {
        var nickname = request.NickName.Trim();
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 64)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Nickname must be between 1 and 64 characters.");
        }

        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email) || email.Length > 256)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "A valid email address is required.");
        }

        var normalizedEmail = YggdrasilFormat.Normalize(email);
        var avatarBase64 = NormalizeAvatarBase64(request.AvatarBase64);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            throw new AuthException(StatusCodes.Status401Unauthorized, "User is unavailable.");
        }

        var emailInUse = await dbContext.Users.AnyAsync(
            entity => entity.Id != userId && entity.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (emailInUse)
        {
            throw new AuthException(StatusCodes.Status409Conflict, "Email is already in use.");
        }

        var emailChanged = user.NormalizedEmail != normalizedEmail;
        if (emailChanged || !string.IsNullOrWhiteSpace(request.VerificationCode))
        {
            registrationVerificationService.EnsureEmailAllowed(email);
            await registrationVerificationService.VerifyAndConsumeAsync(email, request.VerificationCode, cancellationToken);
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        user.NickName = nickname;
        user.Email = email;
        user.NormalizedEmail = normalizedEmail;
        user.AvatarBase64 = avatarBase64;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (emailChanged)
            {
                await yggdrasilLifecycle.RevokeUserClientsAsync(dbContext, userId, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await unionService.MarkProfilesDirtyAsync(cancellationToken);
        return ToAccountProfileDto(user);
    }

    /// <summary>Changes the password and revokes usable/refreshable Yggdrasil versions in one database transaction.</summary>
    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        EnsurePasswordAllowed(request.CurrentPassword);
        EnsurePasswordAllowed(request.NewPassword);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .Include(entity => entity.AuthenticationMethods)
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null || user.IsDisabled || !VerifyPassword(user, request.CurrentPassword))
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Current password is invalid.");
        }

        var passwordMethod = user.AuthenticationMethods.FirstOrDefault(method => method.Type == AuthenticationMethodType.Password);
        if (passwordMethod is null)
        {
            passwordMethod = new UserAuthenticationMethod
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Type = AuthenticationMethodType.Password
            };
            dbContext.UserAuthenticationMethods.Add(passwordMethod);
        }

        passwordMethod.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await yggdrasilLifecycle.RevokeUserClientsAsync(dbContext, userId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
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

    private static AccountProfileDto ToAccountProfileDto(User user)
    {
        return new AccountProfileDto(
            user.Id, user.UserName, user.NickName, user.Email, user.AvatarBase64, user.Role, user.EmailVerifiedAt);
    }

    private static string? NormalizeAvatarBase64(string? avatarBase64)
    {
        if (string.IsNullOrWhiteSpace(avatarBase64))
        {
            return null;
        }

        var value = avatarBase64.Trim();
        var validPrefix = value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:image/jpeg;base64,", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:image/webp;base64,", StringComparison.OrdinalIgnoreCase);
        if (!validPrefix)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Avatar must be a PNG, JPEG, or WebP data URL.");
        }

        var separatorIndex = value.IndexOf(",", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Avatar data is invalid.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value[(separatorIndex + 1)..]);
        }
        catch (FormatException)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Avatar data is invalid.");
        }

        if (bytes.Length > 1_500_000)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Avatar must be smaller than 1.5 MB.");
        }

        return value;
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

    #region Admin

    public async Task<AdminPageDto<AdminUserListItemDto>> ListUsersAsync(
        int page,
        int pageSize,
        string? search,
        UserRole? role,
        bool? isDisabled,
        CancellationToken cancellationToken)
    {
        const int maxPageSize = 100;
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = YggdrasilFormat.Normalize(search);
            query = query.Where(user =>
                user.NormalizedUserName.Contains(normalized) || user.NormalizedEmail.Contains(normalized));
        }

        if (role is not null)
        {
            query = query.Where(user => user.Role == role.Value);
        }

        if (isDisabled is not null)
        {
            query = query.Where(user => user.IsDisabled == isDisabled.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;
        if (totalPages > 0)
        {
            page = Math.Min(page, totalPages);
        }

        var users = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var items = mapper.Map<AdminUserListItemDto[]>(users);
        return new AdminPageDto<AdminUserListItemDto>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<AdminUserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .AsNoTracking()
            .Include(entity => entity.Players)
            .Include(entity => entity.Textures)
            .Include(entity => entity.Clients)
            .ThenInclude(client => client.Player)
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        var mapped = mapper.Map<AdminUserDetailDto>(user, options =>
        {
            options.Items[AdminMappingContextKeys.OwnerUserName] = user.UserName;
            options.Items[AdminMappingContextKeys.UploaderId] = user.Id;
            options.Items[AdminMappingContextKeys.UploaderName] = UploaderDisplayName(user.UserName, user.NickName);
        });
        return mapped;
    }

    /// <summary>Commits email or disable changes with hard Yggdrasil revocation, then marks the Union snapshot for recoverable synchronization.</summary>
    public async Task<AdminUserListItemDto> UpdateUserAsync(
        Guid userId,
        AdminUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        var originalEmail = user.NormalizedEmail;
        var originalDisabled = user.IsDisabled;

        if (request.Role == UserRole.Player && user.Role == UserRole.Admin)
        {
            await EnsureAdminNotLastAsync(dbContext, userId, cancellationToken);
        }

        if (request.IsDisabled == true && user.Role == UserRole.Admin)
        {
            await EnsureAdminNotLastAsync(dbContext, userId, cancellationToken);
        }

        if (request.NickName is not null)
        {
            var nickName = request.NickName.Trim();
            if (nickName.Length is < 1 or > 64)
            {
                throw new AuthException(StatusCodes.Status400BadRequest, "Nickname must be 1-64 characters.");
            }

            user.NickName = nickName;
        }

        if (request.Email is not null)
        {
            var email = request.Email.Trim();
            var normalizedEmail = YggdrasilFormat.Normalize(email);
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !EmailRegex().IsMatch(email))
            {
                throw new AuthException(StatusCodes.Status400BadRequest, "Email is invalid.");
            }

            var duplicate = await dbContext.Users.AnyAsync(
                entity => entity.Id != userId && entity.NormalizedEmail == normalizedEmail,
                cancellationToken);
            if (duplicate)
            {
                throw new AuthException(StatusCodes.Status409Conflict, "Email already exists.");
            }

            if (user.NormalizedEmail != normalizedEmail)
            {
                user.EmailVerifiedAt = null;
            }

            user.Email = email;
            user.NormalizedEmail = normalizedEmail;
        }

        if (request.PreferredLanguage is not null)
        {
            var language = request.PreferredLanguage.Trim();
            if (string.IsNullOrWhiteSpace(language) || language.Length > 16)
            {
                throw new AuthException(StatusCodes.Status400BadRequest, "Preferred language is invalid.");
            }

            user.PreferredLanguage = language;
        }

        if (request.Role is not null)
        {
            user.Role = request.Role.Value;
        }

        if (request.MaxPlayerCount is not null)
        {
            if (request.MaxPlayerCount.Value is < 0 or > 100)
            {
                throw new AuthException(StatusCodes.Status400BadRequest, "Max player count must be between 0 and 100.");
            }

            user.MaxPlayerCount = request.MaxPlayerCount.Value;
        }

        if (request.IsDisabled is not null)
        {
            user.IsDisabled = request.IsDisabled.Value;
        }

        var credentialsChanged = user.NormalizedEmail != originalEmail || user.IsDisabled != originalDisabled;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (credentialsChanged)
            {
                await yggdrasilLifecycle.RevokeUserClientsAsync(dbContext, userId, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await unionService.MarkProfilesDirtyAsync(cancellationToken);
        return mapper.Map<AdminUserListItemDto>(user);
    }

    /// <summary>Applies an administrator password reset and hard-revokes all Yggdrasil client versions in the same transaction.</summary>
    public async Task ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken)
    {
        EnsurePasswordAllowed(newPassword);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users
            .Include(entity => entity.AuthenticationMethods)
            .FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        var passwordMethod = user.AuthenticationMethods.FirstOrDefault(method => method.Type == AuthenticationMethodType.Password);
        if (passwordMethod is null)
        {
            passwordMethod = new UserAuthenticationMethod
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Type = AuthenticationMethodType.Password
            };
            dbContext.UserAuthenticationMethods.Add(passwordMethod);
        }

        passwordMethod.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await yggdrasilLifecycle.RevokeUserClientsAsync(dbContext, userId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>Locks the account then all owned or bound-to-owned-player clients in ID order before revocation/cascade deletion, including revoked former-owner rows; cache invalidation and Union reconciliation follow commit.</summary>
    public async Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await dbContext.Users.FirstOrDefaultAsync(entity => entity.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new AuthException(StatusCodes.Status404NotFound, "User was not found.");
        }

        if (user.Role == UserRole.Admin)
        {
            await EnsureAdminNotLastAsync(dbContext, userId, cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await yggdrasilLifecycle.LockUserAsync(dbContext, userId, cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"YggdrasilClients\" WHERE \"UserId\" = {userId} OR \"PlayerId\" IN (SELECT \"Id\" FROM \"Players\" WHERE \"UserId\" = {userId}) ORDER BY \"Id\" FOR UPDATE", cancellationToken);
        await yggdrasilLifecycle.RevokeUserClientsAsync(dbContext, userId, cancellationToken);

        var textureIds = await dbContext.Textures
            .AsNoTracking()
            .Where(texture => texture.UserId == userId)
            .Select(texture => texture.Id)
            .ToArrayAsync(cancellationToken);

        var playerIds = await dbContext.Players
            .AsNoTracking()
            .Where(player => player.UserId == userId)
            .Select(player => player.Id)
            .ToArrayAsync(cancellationToken);

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await cacheService.RemoveAsync(PlayerService.UserPlayersCacheKey(userId), cancellationToken);
        foreach (var playerId in playerIds)
        {
            await cacheService.RemoveAsync(PlayerService.PlayerCacheKey(userId, playerId), cancellationToken);
        }

        await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCountCacheKey(), cancellationToken);
        foreach (var textureId in textureIds)
        {
            await cacheService.RemoveAsync(TextureLibraryService.PublicTextureCacheKey(textureId), cancellationToken);
            await cacheService.RemoveAsync(TextureLibraryService.PublicTexturePreviewCacheKey(textureId), cancellationToken);
        }

        await unionService.MarkProfilesDirtyAsync(cancellationToken);
    }

    public async Task<int> CountUsersAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Users.CountAsync(cancellationToken);
    }

    private static async Task EnsureAdminNotLastAsync(
        AppDbContext dbContext,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        var otherAdmins = await dbContext.Users
            .AsNoTracking()
            .CountAsync(
                entity => entity.Role == UserRole.Admin && entity.Id != adminUserId,
                cancellationToken);
        if (otherAdmins == 0)
        {
            throw new AuthException(StatusCodes.Status400BadRequest, "Cannot demote or disable the last administrator.");
        }
    }

    private static string UploaderDisplayName(string userName, string nickName)
    {
        return string.IsNullOrWhiteSpace(nickName) ? userName : nickName;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")]
    private static partial System.Text.RegularExpressions.Regex EmailRegex();

    #endregion
}
