using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed class UnionActivityService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    /// <summary>Reads a bounded activity page for one authenticated user without exposing other accounts' rows.</summary>
    public Task<UnionActivityPageResponse> GetUserPageAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken) =>
        ReadPageAsync(userId, false, page, pageSize, cancellationToken);

    /// <summary>Reads a bounded administrator activity page including actor and source-address fields.</summary>
    public Task<UnionActivityPageResponse> GetAdminPageAsync(
        int page, int pageSize, CancellationToken cancellationToken) =>
        ReadPageAsync(null, true, page, pageSize, cancellationToken);

    private async Task<UnionActivityPageResponse> ReadPageAsync(
        Guid? userId, bool includeAdminFields, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Activity pagination is invalid.");
        }
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<YggdrasilActivity> query = dbContext.YggdrasilActivities.AsNoTracking();
        if (userId is not null) query = query.Where(activity => activity.UserId == userId);
        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(activity => activity.CreatedAt)
            .ThenByDescending(activity => activity.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(activity => new
            {
                activity.Id,
                activity.Action,
                activity.UserId,
                UserName = activity.User == null ? null : activity.User.UserName,
                activity.PlayerId,
                PlayerName = activity.Player == null ? null : activity.Player.Name,
                activity.Parameters,
                activity.IpAddress,
                activity.CreatedAt
            }).ToArrayAsync(cancellationToken);
        var items = rows.Select(row => new UnionActivityDto(
            row.Id,
            row.Action,
            includeAdminFields ? row.UserId : null,
            includeAdminFields ? row.UserName : null,
            row.PlayerId,
            row.PlayerName,
            ParseParameters(row.Parameters),
            includeAdminFields ? row.IpAddress : null,
            row.CreatedAt)).ToArray();
        return new UnionActivityPageResponse(items, page, pageSize, total);
    }

    private static JsonElement ParseParameters(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }
}
