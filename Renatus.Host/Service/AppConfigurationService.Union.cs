using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed partial class AppConfigurationService
{
    public Task<UnionOptions> GetUnionOptionsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<UnionOptions>(AppConfigurationKeys.Union, cancellationToken);
    }

    public Task UpdateUnionOptionsAsync(UnionOptions options, CancellationToken cancellationToken)
    {
        return UpdateAsync(AppConfigurationKeys.Union, options, cancellationToken);
    }

    public Task<UnionState> GetUnionStateAsync(CancellationToken cancellationToken)
    {
        return GetAsync<UnionState>(AppConfigurationKeys.UnionState, cancellationToken);
    }

    public Task UpdateUnionStateAsync(UnionState state, CancellationToken cancellationToken)
    {
        return UpdateAsync(AppConfigurationKeys.UnionState, state, cancellationToken);
    }

    public async Task<TResult> MutateUnionStateAsync<TResult>(
        Func<UnionState, TResult> mutation,
        CancellationToken cancellationToken)
    {
        return await MutateUnionConfigurationAsync(
            AppConfigurationKeys.UnionState, mutation, cancellationToken);
    }

    public async Task<TResult> MutateUnionOptionsAsync<TResult>(
        Func<UnionOptions, TResult> mutation,
        CancellationToken cancellationToken)
    {
        return await MutateUnionConfigurationAsync(
            AppConfigurationKeys.Union, mutation, cancellationToken);
    }

    private async Task<TResult> MutateUnionConfigurationAsync<TValue, TResult>(
        string key,
        Func<TValue, TResult> mutation,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var configuration = await dbContext.AppConfigurations
            .FromSqlInterpolated(
                $"SELECT * FROM \"AppConfigurations\" WHERE \"Key\" = {key} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var value = JsonSerializer.Deserialize<TValue>(configuration.Value, JsonOptions)
            ?? throw new InvalidOperationException($"Configuration '{key}' contains invalid JSON.");
        var result = mutation(value);
        configuration.Value = JsonSerializer.Serialize(value, JsonOptions);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await cacheService.RemoveAsync($"renatus:configuration:{key}", cancellationToken);
        return result;
    }

    public Task<YggdrasilConnectOptions> GetYggdrasilConnectOptionsAsync(CancellationToken cancellationToken)
    {
        return GetAsync<YggdrasilConnectOptions>(AppConfigurationKeys.YggdrasilConnect, cancellationToken);
    }

    public Task UpdateYggdrasilConnectOptionsAsync(
        YggdrasilConnectOptions options, CancellationToken cancellationToken)
    {
        return UpdateAsync(AppConfigurationKeys.YggdrasilConnect, options, cancellationToken);
    }
}
