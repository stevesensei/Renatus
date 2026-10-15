using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;
using StackExchange.Redis;

namespace Renatus.Host.Service.Connect;

public sealed class ConnectSessionService(
    AppDbContext dbContext,
    AppConfigurationService configurationService,
    YggdrasilLifecycleService lifecycleService,
    YggdrasilService yggdrasilService,
    TextureService textureService,
    PlayerService playerService,
    IConnectionMultiplexer connectionMultiplexer)
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    /// <summary>Accepts only a validated Connect access principal with Join+Select, then binds one owned profile to a short-lived single-use session.</summary>
    public async Task JoinAsync(ClaimsPrincipal principal, JoinRequest request, CancellationToken cancellationToken)
    {
        var (user, player, issuedAt) = await ValidateSelectedPlayerAsync(
            principal, request.SelectedProfile, ConnectConstants.ServerJoinScope, cancellationToken);
        var authorizationId = principal.GetAuthorizationId()
            ?? throw YggdrasilException.InvalidToken();
        var renamedAt = await lifecycleService.GetPlayerRenamedAtAsync(dbContext, player.Id, cancellationToken);
        if (renamedAt >= issuedAt) throw YggdrasilException.InvalidToken();
        var options = await configurationService.GetYggdrasilOptionsAsync(cancellationToken);
        var entry = new ConnectJoinEntry(user.Id, player.Id, player.MinecraftUuid, authorizationId,
            issuedAt, request.SelectedProfile, request.ServerId);
        await _database.StringSetAsync(JoinKey(player.Name, request.ServerId),
            JsonSerializer.Serialize(entry), TimeSpan.FromSeconds(options.JoinSessionTtlSeconds));
    }

    /// <summary>Consumes one join snapshot and rechecks its live database grant, current ownership/UUID, and rename boundary before returning a profile.</summary>
    public async Task<SessionProfileResponse?> HasJoinedAsync(
        string username,
        string serverId,
        CancellationToken cancellationToken)
    {
        var cached = await _database.StringGetDeleteAsync(JoinKey(username, serverId));
        if (!cached.HasValue) return null;
        var entry = JsonSerializer.Deserialize<ConnectJoinEntry>(cached.ToString());
        if (entry is null || entry.ServerId != serverId) return null;
        if (!await IsActiveAuthorizationAsync(entry.AuthorizationId, entry.UserId, cancellationToken)) return null;
        var player = await dbContext.Players.AsNoTracking().Include(entity => entity.User)
            .SingleOrDefaultAsync(entity => entity.Id == entry.PlayerId && entity.UserId == entry.UserId
                && entity.MinecraftUuid == entry.MinecraftUuid && !entity.User.IsDisabled
                && entity.User.EmailVerifiedAt != null, cancellationToken);
        if (player is null || !string.Equals(player.Name, username, StringComparison.Ordinal)) return null;
        var renamedAt = await lifecycleService.GetPlayerRenamedAtAsync(dbContext, player.Id, cancellationToken);
        if (renamedAt >= entry.IssuedAtMilliseconds) return null;
        return await yggdrasilService.GetSessionProfileAsync(player.MinecraftUuid, false, cancellationToken);
    }

    public async Task UploadTextureAsync(
        ClaimsPrincipal principal,
        string uuid,
        string type,
        string? model,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var (_, player, _) = await ValidateSelectedPlayerAsync(
            principal, uuid, ConnectConstants.ProfileSelectScope, cancellationToken);
        if (file.Length is < 1 or > 10485760)
            throw new YggdrasilException(400, "IllegalArgumentException", "Texture file size must be between 1 byte and 10 MiB.");
        var textureType = ParseTextureType(type);
        var skinModel = model switch
        {
            null or "" or "default" => SkinModel.Default,
            "slim" => SkinModel.Slim,
            _ => throw new YggdrasilException(400, "IllegalArgumentException", "Invalid skin model.")
        };
        using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        try
        {
            var texture = await textureService.UploadTextureAsync(player.UserId,
                new UploadTextureRequest(textureType, Convert.ToBase64String(content.ToArray()), false, skinModel),
                cancellationToken);
            await playerService.UpdatePlayerTexturesAsync(player.UserId, player.Id,
                textureType == TextureType.Skin
                    ? new UpdatePlayerTexturesRequest(texture.Id, player.CapeTextureId, skinModel)
                    : new UpdatePlayerTexturesRequest(player.SkinTextureId, texture.Id, player.SkinModel),
                cancellationToken);
        }
        catch (Exception exception) when (exception is TextureException or PlayerException)
        {
            throw new YggdrasilException(403, "ForbiddenOperationException", exception.Message);
        }
    }

    public async Task ResetTextureAsync(
        ClaimsPrincipal principal,
        string uuid,
        string type,
        CancellationToken cancellationToken)
    {
        var (_, player, _) = await ValidateSelectedPlayerAsync(
            principal, uuid, ConnectConstants.ProfileSelectScope, cancellationToken);
        var textureType = ParseTextureType(type);
        try
        {
            await playerService.UpdatePlayerTexturesAsync(player.UserId, player.Id,
                textureType == TextureType.Skin
                    ? new UpdatePlayerTexturesRequest(null, player.CapeTextureId, player.SkinModel)
                    : new UpdatePlayerTexturesRequest(player.SkinTextureId, null, player.SkinModel),
                cancellationToken);
        }
        catch (PlayerException exception)
        {
            throw new YggdrasilException(403, "ForbiddenOperationException", exception.Message);
        }
    }

    /// <summary>Checks current persisted grant status as well as account, scope and owned-profile binding for every join or texture use, including JSON-body tokens.</summary>
    private async Task<(Entity.User User, Entity.Player Player, long IssuedAt)> ValidateSelectedPlayerAsync(
        ClaimsPrincipal principal,
        string selectedProfile,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        if (!principal.HasScope(requiredScope)
            || !principal.HasScope(OpenIddictConstants.Scopes.OpenId)
            || !principal.HasScope(ConnectConstants.ProfileSelectScope)
            || !Guid.TryParse(principal.GetClaim(OpenIddictConstants.Claims.Subject), out var userId)
            || !Guid.TryParseExact(principal.GetClaim(ConnectConstants.SelectedProfileBindingClaim), "N", out var tokenProfile)
            || YggdrasilFormat.ParseUnsignedUuid(selectedProfile) != tokenProfile)
            throw YggdrasilException.InvalidToken();
        var authorizationId = principal.GetAuthorizationId();
        if (authorizationId is null || !await IsActiveAuthorizationAsync(authorizationId, userId, cancellationToken))
            throw YggdrasilException.InvalidToken();
        var user = await dbContext.Users.AsNoTracking().Include(entity => entity.Players)
            .SingleOrDefaultAsync(entity => entity.Id == userId && !entity.IsDisabled
                && entity.EmailVerifiedAt != null, cancellationToken)
            ?? throw YggdrasilException.InvalidToken();
        var player = user.Players.SingleOrDefault(entity => entity.MinecraftUuid == tokenProfile)
            ?? throw YggdrasilException.InvalidToken();
        var issued = principal.GetClaim(OpenIddictConstants.Claims.IssuedAt);
        if (!long.TryParse(issued, out var issuedAtSeconds)) throw YggdrasilException.InvalidToken();
        return (user, player, checked(issuedAtSeconds * 1000));
    }

    /// <summary>Reads the database directly so transactional bulk revocation cannot be hidden by a previously cached OpenIddict authorization entity.</summary>
    private Task<bool> IsActiveAuthorizationAsync(string authorizationId, Guid userId, CancellationToken cancellationToken)
        => dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().AsNoTracking().AnyAsync(
            authorization => authorization.Id == authorizationId
                && authorization.Subject == userId.ToString("D")
                && authorization.Status == OpenIddictConstants.Statuses.Valid, cancellationToken);

    private static TextureType ParseTextureType(string type) => type switch
    {
        "skin" => TextureType.Skin,
        "cape" => TextureType.Cape,
        _ => throw new YggdrasilException(400, "IllegalArgumentException", "Invalid texture type.")
    };

    private static string JoinKey(string username, string serverId)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(serverId))).ToLowerInvariant();
        return $"connect:join:{YggdrasilFormat.Normalize(username)}:{hash}";
    }

    private sealed record ConnectJoinEntry(Guid UserId, Guid PlayerId, Guid MinecraftUuid,
        string AuthorizationId, long IssuedAtMilliseconds, string SelectedProfile, string ServerId);
}
