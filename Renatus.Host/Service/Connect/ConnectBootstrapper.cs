using OpenIddict.Abstractions;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service.Connect;

public sealed class ConnectBootstrapper(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictScopeManager scopeManager)
{
    /// <summary>Idempotently registers the fixed protocol scopes and the configured public PKCE shared client.</summary>
    public async Task InitializeAsync(YggdrasilConnectOptions options, CancellationToken cancellationToken)
    {
        foreach (var name in new[]
        {
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            ConnectConstants.ProfileReadScope,
            ConnectConstants.ProfileSelectScope,
            ConnectConstants.ServerJoinScope
        })
        {
            if (await scopeManager.FindByNameAsync(name, cancellationToken) is null)
                await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name, DisplayName = name, Resources = { "renatus-api" }
                }, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(options.SharedClientId)) return;
        var application = await applicationManager.FindByClientIdAsync(options.SharedClientId, cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = options.SharedClientId,
            DisplayName = "Yggdrasil Connect shared client",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit
        };
        foreach (var uri in options.SharedClientRedirectUris.Select(value => new Uri(value)))
            descriptor.RedirectUris.Add(uri);
        descriptor.Permissions.UnionWith([
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.DeviceAuthorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.DeviceCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Permissions.Prefixes.Scope + ConnectConstants.ProfileReadScope,
            OpenIddictConstants.Permissions.Prefixes.Scope + ConnectConstants.ProfileSelectScope,
            OpenIddictConstants.Permissions.Prefixes.Scope + ConnectConstants.ServerJoinScope
        ]);
        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        if (application is null) await applicationManager.CreateAsync(descriptor, cancellationToken);
        else await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
    }
}
