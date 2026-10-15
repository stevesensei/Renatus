using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Renatus.Host.Data;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed class UnionBlacklistService(
    AppConfigurationService configurationService,
    UnionCenterClient centerClient)
{
    /// <summary>Queries the authoritative center page with bounded search and pagination inputs.</summary>
    public async Task<JsonElement> QueryAsync(string? query, int page, CancellationToken cancellationToken)
    {
        query ??= string.Empty;
        if (query.Length > 256 || query.Any(char.IsControl) || page is < 1 or > 100000)
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Blacklist query is invalid.");
        }
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        return await centerClient.QueryBlacklistAsync(options, query, page, cancellationToken);
    }

    /// <summary>Creates a remote blacklist entry without persisting or logging its email locally.</summary>
    public async Task CreateAsync(UnionBlacklistCreateRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email)
            || reason.Length is < 1 or > 1000 || reason.Any(char.IsControl))
        {
            throw new UnionException(StatusCodes.Status400BadRequest, "Blacklist entry is invalid.");
        }
        var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
        await centerClient.CreateBlacklistEntryAsync(options, new UnionBlacklistCreateRequest(email, reason), cancellationToken);
    }

    /// <summary>Invalidates one positive center-owned blacklist identifier.</summary>
    public async Task InvalidateAsync(long id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        await centerClient.InvalidateBlacklistEntryAsync(
            await configurationService.GetUnionOptionsAsync(cancellationToken), id, cancellationToken);
    }

    /// <summary>Deletes one positive center-owned blacklist identifier.</summary>
    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        await centerClient.DeleteBlacklistEntryAsync(
            await configurationService.GetUnionOptionsAsync(cancellationToken), id, cancellationToken);
    }

    private static void ValidateId(long id)
    {
        if (id <= 0) throw new UnionException(StatusCodes.Status400BadRequest, "Blacklist ID is invalid.");
    }
}
