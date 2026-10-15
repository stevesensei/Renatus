using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed partial class PlayerService
{
    /// <summary>Serializes a protocol texture change with revocation and player mutation, rechecks UUID/ownership/rename, and preserves the other texture slot.</summary>
    public async Task UpdateYggdrasilTextureAsync(Guid userId, Guid playerId, Guid minecraftUuid,
        Guid clientId, int clientVersion, long issuedAtMilliseconds, long expiresAtMilliseconds, TextureType type, Guid? textureId,
        SkinModel skinModel, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await yggdrasilLifecycle.LockUserAsync(dbContext, userId, cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"YggdrasilClients\" WHERE \"Id\" = {clientId} FOR UPDATE", cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Players\" WHERE \"Id\" = {playerId} FOR UPDATE", cancellationToken);
        var available = await dbContext.YggdrasilClients.AnyAsync(client => client.Id == clientId
            && client.Version == clientVersion && client.UserId == userId && client.PlayerId == playerId
            && !client.User.IsDisabled, cancellationToken);
        var player = await dbContext.Players.SingleOrDefaultAsync(entity => entity.Id == playerId
            && entity.UserId == userId && entity.MinecraftUuid == minecraftUuid, cancellationToken);
        var renamedAt = await yggdrasilLifecycle.GetPlayerRenamedAtAsync(dbContext, playerId, cancellationToken);
        var unionOptions = await configurationService.GetUnionOptionsAsync(cancellationToken);
        var verified = !unionOptions.Enabled || await dbContext.Users.AnyAsync(
            user => user.Id == userId && user.EmailVerifiedAt != null, cancellationToken);
        if (!available || !verified || player is null || renamedAt >= issuedAtMilliseconds
            || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiresAtMilliseconds)
            throw new PlayerException(403, "The selected profile is unavailable.");
        await EnsureTextureTypeAsync(dbContext, userId, textureId, type, cancellationToken);
        if (type == TextureType.Skin)
        {
            player.SkinTextureId = textureId;
            player.SkinModel = skinModel;
        }
        else
        {
            player.CapeTextureId = textureId;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await RemovePlayerCachesAsync(userId, playerId, cancellationToken);
        await unionService.MarkProfilesDirtyAsync(cancellationToken);
    }
}
