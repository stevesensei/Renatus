using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed class UnionUserService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    AppConfigurationService configurationService,
    UnionCenterClient centerClient)
{
    /// <summary>Builds the current account's center binding view after resolving local REST player IDs to protocol UUIDs.</summary>
    public async Task<UnionProfilesResponse> GetProfilesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var options = await GetEnabledOptionsAsync(cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var players = await dbContext.Players.AsNoTracking()
            .Where(player => player.UserId == userId)
            .OrderBy(player => player.Name)
            .Select(player => new { player.Id, player.MinecraftUuid, player.Name })
            .ToArrayAsync(cancellationToken);

        var tasks = players.Select(async player =>
        {
            var detailTask = centerClient.GetProfileDetailAsync(options, player.MinecraftUuid, cancellationToken);
            var conflictsTask = centerClient.GetUnmappedProfilesByNameAsync(options, player.Name, cancellationToken);
            await Task.WhenAll(detailTask, conflictsTask);
            var detail = await detailTask;
            return new UnionProfileDto(player.Id, player.MinecraftUuid, player.Name, detail,
                FilterConflicts(detail, await conflictsTask));
        });
        return new UnionProfilesResponse(await Task.WhenAll(tasks));
    }

    /// <summary>Returns the last validated center directory persisted by the member synchronization path.</summary>
    public async Task<UnionDirectoryResponse> GetDirectoryAsync(CancellationToken cancellationToken)
    {
        await GetEnabledOptionsAsync(cancellationToken);
        var state = await configurationService.GetUnionStateAsync(cancellationToken);
        return new UnionDirectoryResponse(state.Servers);
    }

    /// <summary>Reads the center-computed security level without exposing or returning the member key.</summary>
    public async Task<UnionSecurityLevelResponse> GetSecurityLevelAsync(CancellationToken cancellationToken)
    {
        var level = await centerClient.GetSecurityLevelAsync(
            await GetEnabledOptionsAsync(cancellationToken), cancellationToken);
        return new UnionSecurityLevelResponse(level);
    }

    /// <summary>Creates a binding token only after confirming the local REST player belongs to the caller.</summary>
    public async Task<UnionBindTokenResponse> CreateBindTokenAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var (options, minecraftUuid) = await ResolveOwnedPlayerAsync(userId, playerId, cancellationToken);
        return await centerClient.CreateBindTokenAsync(options, minecraftUuid, cancellationToken);
    }

    /// <summary>Uses a center token to bind an ownership-checked local player.</summary>
    public async Task BindAsync(Guid userId, Guid playerId, UnionBindRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096 || request.Token.Any(char.IsControl))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Binding token is invalid.");
        }
        var (options, minecraftUuid) = await ResolveOwnedPlayerAsync(userId, playerId, cancellationToken);
        await centerClient.BindToAsync(options, minecraftUuid, request.Token.Trim(), cancellationToken);
    }

    /// <summary>Unbinds an ownership-checked local player while leaving the authoritative graph at the center.</summary>
    public async Task UnbindAsync(Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var (options, minecraftUuid) = await ResolveOwnedPlayerAsync(userId, playerId, cancellationToken);
        await centerClient.UnbindAsync(options, minecraftUuid, cancellationToken);
    }

    /// <summary>Requests a remap with a server-derived source UUID so callers cannot operate on another local profile.</summary>
    public async Task RemapAsync(Guid userId, Guid playerId, UnionRemapProfileRequest request, CancellationToken cancellationToken)
    {
        if (request.TargetMinecraftUuid == Guid.Empty)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Target Minecraft UUID is invalid.");
        }
        var (options, minecraftUuid) = await ResolveOwnedPlayerAsync(userId, playerId, cancellationToken);
        await centerClient.RequestRemapAsync(options, minecraftUuid, request.TargetMinecraftUuid, cancellationToken);
    }

    private async Task<(UnionOptions Options, Guid MinecraftUuid)> ResolveOwnedPlayerAsync(
        Guid userId, Guid playerId, CancellationToken cancellationToken)
    {
        var options = await GetEnabledOptionsAsync(cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var minecraftUuid = await dbContext.Players.AsNoTracking()
            .Where(player => player.Id == playerId && player.UserId == userId)
            .Select(player => (Guid?)player.MinecraftUuid)
            .SingleOrDefaultAsync(cancellationToken);
        if (minecraftUuid is null)
        {
            throw new UnionException(StatusCodes.Status404NotFound, "Player was not found.");
        }
        return (options, minecraftUuid.Value);
    }

    private async Task<UnionOptions> GetEnabledOptionsAsync(CancellationToken cancellationToken)
    {
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        if (!options.Enabled)
        {
            throw new UnionException(StatusCodes.Status503ServiceUnavailable, "Union integration is disabled.");
        }
        return options;
    }

    private static IReadOnlyList<JsonElement> FilterConflicts(JsonElement detail, JsonElement candidates)
    {
        if (candidates.ValueKind != JsonValueKind.Array)
        {
            throw new UnionException(StatusCodes.Status502BadGateway, "Union center returned invalid profile conflicts.");
        }

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        AddInternalId(detail, excluded);
        if (detail.ValueKind == JsonValueKind.Object
            && detail.TryGetProperty("bind", out var bindings)
            && bindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var binding in bindings.EnumerateArray()) AddInternalId(binding, excluded);
        }

        return candidates.EnumerateArray()
            .Where(candidate => !TryGetInternalId(candidate, out var id) || !excluded.Contains(id))
            .Select(candidate => candidate.Clone())
            .ToArray();
    }

    private static void AddInternalId(JsonElement value, ISet<string> target)
    {
        if (TryGetInternalId(value, out var id)) target.Add(id);
    }

    private static bool TryGetInternalId(JsonElement value, out string id)
    {
        id = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("internal_id", out var element)) return false;
        id = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            _ => string.Empty
        };
        return id.Length > 0;
    }
}
