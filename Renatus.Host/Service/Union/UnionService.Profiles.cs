using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Utils;

namespace Renatus.Host.Service.Union;

public sealed partial class UnionService
{
    /// <summary>Marks a committed local mutation for retry without contacting the center. If marker persistence fails, periodic snapshot checksums still detect the change.</summary>
    public async Task MarkProfilesDirtyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.MutateUnionStateAsync(state =>
            {
                state.ProfileSyncRevision = checked(state.ProfileSyncRevision + 1);
                state.NeedsProfileSync = true;
                return true;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Unable to persist Union dirty marker; snapshot reconciliation will retry.");
        }
    }

    /// <summary>Sends a complete UUID-to-name snapshot under a single sync lock. Success clears only the captured revision, and outages preserve dirty state for retry.</summary>
    public async Task SynchronizeProfilesAsync(bool force, CancellationToken cancellationToken)
    {
        await ProfileSyncLock.WaitAsync(cancellationToken);
        try
        {
            var options = await configurationService.GetUnionOptionsAsync(cancellationToken);
            if (!options.Enabled)
            {
                return;
            }
            var state = await configurationService.GetUnionStateAsync(cancellationToken);
            var revision = state.ProfileSyncRevision;
            var (profiles, checksum) = await ReadProfilesAsync(cancellationToken);
            if (!force && !state.NeedsProfileSync && state.LastProfileChecksum == checksum)
            {
                return;
            }
            try
            {
                await centerClient.SyncProfilesAsync(options, profiles, cancellationToken);
            }
            catch (UnionException exception)
            {
                await RecordSyncFailureAsync(exception.Message, cancellationToken);
                throw;
            }
            await configurationService.MutateUnionStateAsync(current =>
            {
                current.LastProfileSyncAt = DateTimeOffset.UtcNow;
                current.LastProfileSyncRevision = revision;
                current.LastProfileChecksum = checksum;
                current.LastProfileSyncError = null;
                current.NeedsProfileSync = current.ProfileSyncRevision != revision;
                return true;
            }, cancellationToken);
        }
        finally
        {
            ProfileSyncLock.Release();
        }
    }

    /// <summary>Checks both the persisted dirty flag and the actual profile checksum so failed local markers do not lose committed changes.</summary>
    public Task ReconcileProfilesAsync(CancellationToken cancellationToken)
    {
        return SynchronizeProfilesAsync(false, cancellationToken);
    }

    /// <summary>Resolves a normalized player name to its owner's email for authenticated center callbacks; a missing player produces protocol 204.</summary>
    public async Task<UnionEmailResponse?> QueryEmailAsync(string? username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 16)
        {
            logger.LogInformation("Union 邮箱查询拒绝：角色名为空或长度超过16，名称长度={NameLength}，追踪ID={TraceId}",
                username?.Length, Activity.Current?.Id);
            throw new UnionException(StatusCodes.Status400BadRequest, "Username is invalid.");
        }
        var normalized = YggdrasilFormat.Normalize(username);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var email = await dbContext.Players.AsNoTracking().Where(player => player.NormalizedName == normalized)
            .Select(player => player.User.Email).SingleOrDefaultAsync(cancellationToken);
        logger.LogInformation("Union 邮箱查询完成：角色名={PlayerName}，HTTP={StatusCode}，找到邮箱={Found}，追踪ID={TraceId}",
            JsonSerializer.Serialize(username), email is null ? 204 : 200, email is not null, Activity.Current?.Id);
        return email is null ? null : new UnionEmailResponse(email);
    }

    /// <summary>Uses a deterministic ordered UUID/name checksum and emits all players, including an empty dictionary after the last deletion.</summary>
    private async Task<(Dictionary<string, string> Profiles, string Checksum)> ReadProfilesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.Players.AsNoTracking().OrderBy(player => player.MinecraftUuid)
            .Select(player => new { player.MinecraftUuid, player.Name }).ToArrayAsync(cancellationToken);
        var profiles = rows.ToDictionary(row => row.MinecraftUuid.ToString("N"), row => row.Name, StringComparer.Ordinal);
        var canonical = string.Join('\n', rows.Select(row => $"{row.MinecraftUuid:N}\0{row.Name}"));
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return (profiles, checksum);
    }

    /// <summary>Persists only safe bounded errors and keeps the dirty flag set. Failure to record an outage cannot undo player CRUD.</summary>
    private async Task RecordSyncFailureAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.MutateUnionStateAsync(state =>
            {
                state.NeedsProfileSync = true;
                state.LastProfileSyncError = message.Length <= 256 ? message : message[..256];
                return true;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Unable to persist Union synchronization failure state; reconciliation will retry.");
        }
    }
}
