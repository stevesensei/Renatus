using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed partial class AppConfigurationService
{
    private static readonly SemaphoreSlim UnionConfigurationLock = new(1, 1);

    /// <summary>Reads member settings under the same lock as writes to prevent a stale cache refill after invalidation.</summary>
    public Task<UnionOptions> GetUnionOptionsAsync(CancellationToken cancellationToken)
    {
        return ReadUnionConfigurationAsync<UnionOptions>(AppConfigurationKeys.Union, cancellationToken);
    }

    /// <summary>Replaces member settings through the serialized database writer; secrets stay in persistence, never response DTOs.</summary>
    public Task UpdateUnionOptionsAsync(UnionOptions options, CancellationToken cancellationToken)
    {
        return MutateUnionConfigurationAsync<UnionOptions, bool>(AppConfigurationKeys.Union, _ => options, _ => true, cancellationToken);
    }

    /// <summary>Reads persisted directory, texture key and sync recovery state without racing cache invalidation.</summary>
    public Task<UnionState> GetUnionStateAsync(CancellationToken cancellationToken)
    {
        return ReadUnionConfigurationAsync<UnionState>(AppConfigurationKeys.UnionState, cancellationToken);
    }

    /// <summary>Serializes explicit whole-state replacement with all incremental mutations; prefer MutateUnionStateAsync for partial changes.</summary>
    public Task UpdateUnionStateAsync(UnionState state, CancellationToken cancellationToken)
    {
        return MutateUnionConfigurationAsync<UnionState, bool>(AppConfigurationKeys.UnionState, _ => state, _ => true, cancellationToken);
    }

    /// <summary>Mutates the latest locked JSON row so sync completion, dirty revisions and key updates preserve each other's fields.</summary>
    public Task<TResult> MutateUnionStateAsync<TResult>(Func<UnionState, TResult> mutation, CancellationToken cancellationToken)
    {
        return MutateUnionConfigurationAsync<UnionState, TResult>(AppConfigurationKeys.UnionState, value => value, mutation, cancellationToken);
    }

    /// <summary>Updates only requested settings from the latest locked row, preserving write-only member keys and future option fields.</summary>
    public Task<TResult> MutateUnionOptionsAsync<TResult>(Func<UnionOptions, TResult> mutation, CancellationToken cancellationToken)
    {
        return MutateUnionConfigurationAsync<UnionOptions, TResult>(AppConfigurationKeys.Union, value => value, mutation, cancellationToken);
    }

    /// <summary>Atomically changes member settings and derived state under row locks. Root switches cannot leave the old center's directory or texture key active after restart.</summary>
    public async Task<TResult> MutateUnionSettingsAsync<TResult>(
        Func<UnionOptions, UnionState, TResult> mutation, CancellationToken cancellationToken)
    {
        await UnionConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var keys = new[] { AppConfigurationKeys.Union, AppConfigurationKeys.UnionState };
            var rows = await dbContext.AppConfigurations.FromSqlInterpolated(
                $"SELECT * FROM \"AppConfigurations\" WHERE \"Key\" = ANY ({keys}) ORDER BY \"Key\" FOR UPDATE")
                .ToArrayAsync(cancellationToken);
            var optionsRow = rows.Single(row => row.Key == AppConfigurationKeys.Union);
            var stateRow = rows.Single(row => row.Key == AppConfigurationKeys.UnionState);
            var options = JsonSerializer.Deserialize<UnionOptions>(optionsRow.Value, JsonOptions)
                ?? throw new InvalidOperationException("Union settings contain invalid JSON.");
            var state = JsonSerializer.Deserialize<UnionState>(stateRow.Value, JsonOptions)
                ?? throw new InvalidOperationException("Union state contains invalid JSON.");
            var result = mutation(options, state);
            optionsRow.Value = JsonSerializer.Serialize(options, JsonOptions);
            stateRow.Value = JsonSerializer.Serialize(state, JsonOptions);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            foreach (var key in keys)
            {
                await cacheService.RemoveAsync($"renatus:configuration:{key}", cancellationToken);
            }
            return result;
        }
        finally
        {
            UnionConfigurationLock.Release();
        }
    }

    private async Task<TValue> ReadUnionConfigurationAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        await UnionConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            return await GetAsync<TValue>(key, cancellationToken);
        }
        finally
        {
            UnionConfigurationLock.Release();
        }
    }

    /// <summary>Serializes this process through cache eviction and locks the PostgreSQL row during read-modify-write. Incremental callers never overwrite unrelated state.</summary>
    private async Task<TResult> MutateUnionConfigurationAsync<TValue, TResult>(
        string key, Func<TValue, TValue> replace, Func<TValue, TResult> mutation, CancellationToken cancellationToken)
    {
        await UnionConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var configuration = await dbContext.AppConfigurations.FromSqlInterpolated(
                $"SELECT * FROM \"AppConfigurations\" WHERE \"Key\" = {key} FOR UPDATE").SingleAsync(cancellationToken);
            var current = JsonSerializer.Deserialize<TValue>(configuration.Value, JsonOptions)
                ?? throw new InvalidOperationException($"Configuration '{key}' contains invalid JSON.");
            var value = replace(current);
            var result = mutation(value);
            configuration.Value = JsonSerializer.Serialize(value, JsonOptions);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await cacheService.RemoveAsync($"renatus:configuration:{key}", cancellationToken);
            return result;
        }
        finally
        {
            UnionConfigurationLock.Release();
        }
    }

    public Task<YggdrasilConnectOptions> GetYggdrasilConnectOptionsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<YggdrasilConnectOptions>(AppConfigurationKeys.YggdrasilConnect, cancellationToken);
    }

    public Task UpdateYggdrasilConnectOptionsAsync(YggdrasilConnectOptions options, CancellationToken cancellationToken)
    {
        return UpdateAsync(AppConfigurationKeys.YggdrasilConnect, options, cancellationToken);
    }
}


