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
