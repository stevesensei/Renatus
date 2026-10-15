using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service.Connect;

public sealed class ConnectManagementService(
    AppDbContext dbContext,
    ConnectInteractionStore interactionStore,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    AppConfigurationService configurationService)
{
    public async Task<ConnectInteractionDto> GetInteractionAsync(
        Guid userId,
        string interactionId,
        CancellationToken cancellationToken)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);
        var interaction = await interactionStore.GetAsync(interactionId)
            ?? throw new ConnectException("The interaction is invalid or expired.");
        var profiles = await dbContext.Players.AsNoTracking().Where(player => player.UserId == userId)
            .OrderBy(player => player.Name)
            .Select(player => new ConnectProfileDto(player.Id.ToString("D"), player.Name))
            .ToArrayAsync(cancellationToken);
        return ToDto(interaction) with { Profiles = profiles };
    }

    /// <summary>Rechecks account and profile ownership before atomically consuming a pending authorization decision.</summary>
    public async Task<ConnectCompletionDto> DecideAsync(
        Guid userId,
        string interactionId,
        ConnectDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().Include(entity => entity.Players)
            .SingleOrDefaultAsync(entity => entity.Id == userId && !entity.IsDisabled, cancellationToken)
            ?? throw new ConnectException("The account is unavailable.");
        var pending = await interactionStore.GetAsync(interactionId)
            ?? throw new ConnectException("The interaction is invalid or expired.");
        Guid? minecraftUuid = null;
        if (request.Approve && pending.Scopes.Contains(ConnectConstants.ProfileSelectScope, StringComparer.Ordinal))
        {
            if (!Guid.TryParse(request.PlayerId, out var playerId))
                throw new ConnectException("An owned player must be selected.");
            var player = user.Players.SingleOrDefault(entity => entity.Id == playerId)
                ?? throw new ConnectException("The selected player is unavailable.");
            minecraftUuid = player.MinecraftUuid;
        }
        var completed = await interactionStore.DecideAsync(
            interactionId, userId, minecraftUuid, request.Approve)
            ?? throw new ConnectException("The interaction was already decided or expired.");
        dbContext.YggdrasilActivities.Add(new Entity.YggdrasilActivity
        {
            Id = Guid.NewGuid(), UserId = userId,
            PlayerId = minecraftUuid is null ? null : user.Players.Single(player => player.MinecraftUuid == minecraftUuid).Id,
            Action = request.Approve ? "connect.authorize" : "connect.deny"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ConnectCompletionDto(completed.Pending.CompletionUrl);
    }

    public async Task<IReadOnlyList<ConnectGrantDto>> GetGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);
        var result = new List<ConnectGrantDto>();
        await foreach (var authorization in authorizationManager.FindAsync(
            subject: userId.ToString("D"), client: null, status: null, type: null,
            scopes: null, cancellationToken: cancellationToken))
        {
            if (!await authorizationManager.HasStatusAsync(
                    authorization, OpenIddictConstants.Statuses.Valid, cancellationToken)) continue;
            var applicationId = await authorizationManager.GetApplicationIdAsync(authorization, cancellationToken);
            if (applicationId is null) continue;
            var application = await applicationManager.FindByIdAsync(applicationId, cancellationToken);
            if (application is null) continue;
            result.Add(new ConnectGrantDto(
                (await authorizationManager.GetIdAsync(authorization, cancellationToken))!,
                (await applicationManager.GetClientIdAsync(application, cancellationToken))!,
                await applicationManager.GetDisplayNameAsync(application, cancellationToken) ?? "OAuth client",
                (await authorizationManager.GetScopesAsync(authorization, cancellationToken)).ToArray(),
                await authorizationManager.GetCreationDateAsync(authorization, cancellationToken)
                    ?? DateTimeOffset.UnixEpoch));
        }
        return result;
    }

    /// <summary>Revokes only the caller's authorization so existing access and refresh credentials lose their grant.</summary>
    public async Task RevokeGrantAsync(Guid userId, string authorizationId, CancellationToken cancellationToken)
    {
        var authorization = await authorizationManager.FindByIdAsync(authorizationId, cancellationToken)
            ?? throw new ConnectException("The authorization was not found.");
        if (!string.Equals(await authorizationManager.GetSubjectAsync(authorization, cancellationToken),
                userId.ToString("D"), StringComparison.Ordinal))
            throw new ConnectException("The authorization was not found.");
        await authorizationManager.TryRevokeAsync(authorization, cancellationToken);
        dbContext.YggdrasilActivities.Add(new Entity.YggdrasilActivity
        {
            Id = Guid.NewGuid(), UserId = userId, Action = "connect.grant.revoke"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<YggdrasilConnectOptions> GetOptionsAsync(CancellationToken cancellationToken)
        => configurationService.GetConnectOptionsAsync(cancellationToken);

    public async Task<IReadOnlyList<ConnectClientDto>> GetClientsAsync(CancellationToken cancellationToken)
    {
        var result = new List<ConnectClientDto>();
        await foreach (var application in applicationManager.ListAsync(cancellationToken: cancellationToken))
            result.Add(await ToClientDtoAsync(application, cancellationToken));
        return result;
    }

    public async Task<ConnectClientSecretDto> CreateClientAsync(
        CreateConnectClientRequest request,
        CancellationToken cancellationToken)
    {
        await ValidateClientAsync(request.ClientId, request.RedirectUris, request.AllowImplicitFlow, cancellationToken);
        var secret = request.Confidential ? CreateSecret() : null;
        var descriptor = CreateDescriptor(request.ClientId, request.DisplayName, request.RedirectUris,
            request.Confidential, request.AllowImplicitFlow, secret);
        var application = await applicationManager.CreateAsync(descriptor, cancellationToken);
        var dto = await ToClientDtoAsync(application, cancellationToken);
        return new ConnectClientSecretDto(dto.ClientId, dto.DisplayName, dto.RedirectUris,
            dto.Confidential, dto.AllowImplicitFlow, secret);
    }

    public async Task<ConnectClientDto> UpdateClientAsync(
        string clientId,
        UpdateConnectClientRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureMutableClientAsync(clientId, cancellationToken);
        await ValidateClientAsync(clientId, request.RedirectUris, request.AllowImplicitFlow, cancellationToken, true);
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new ConnectException("The client was not found.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.DisplayName = request.DisplayName.Trim();
        descriptor.ClientType = request.Confidential
            ? OpenIddictConstants.ClientTypes.Confidential
            : OpenIddictConstants.ClientTypes.Public;
        descriptor.RedirectUris.Clear();
        foreach (var uri in request.RedirectUris.Select(value => new Uri(value.Trim()))) descriptor.RedirectUris.Add(uri);
        ApplyPermissions(descriptor, request.AllowImplicitFlow);
        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        return await ToClientDtoAsync(application, cancellationToken);
    }

    public async Task<ConnectClientSecretDto> RotateSecretAsync(string clientId, CancellationToken cancellationToken)
    {
        await EnsureMutableClientAsync(clientId, cancellationToken);
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new ConnectException("The client was not found.");
        if (!await applicationManager.HasClientTypeAsync(
                application, OpenIddictConstants.ClientTypes.Confidential, cancellationToken))
            throw new ConnectException("Public clients do not have secrets.");
        var secret = CreateSecret();
        await applicationManager.UpdateAsync(application, secret, cancellationToken);
        var dto = await ToClientDtoAsync(application, cancellationToken);
        return new ConnectClientSecretDto(dto.ClientId, dto.DisplayName, dto.RedirectUris,
            dto.Confidential, dto.AllowImplicitFlow, secret);
    }

    public async Task DeleteClientAsync(string clientId, CancellationToken cancellationToken)
    {
        await EnsureMutableClientAsync(clientId, cancellationToken);
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new ConnectException("The client was not found.");
        await applicationManager.DeleteAsync(application, cancellationToken);
    }

    private async Task EnsureActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AsNoTracking().AnyAsync(
                entity => entity.Id == userId && !entity.IsDisabled, cancellationToken))
            throw new ConnectException("The account is unavailable.");
    }

    private async Task EnsureMutableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        var options = await configurationService.GetConnectOptionsAsync(cancellationToken);
        if (string.Equals(clientId, options.SharedClientId, StringComparison.Ordinal))
            throw new ConnectException("The shared public client is managed by Connect configuration.");
    }

    /// <summary>Requires valid exact-match redirect registrations and server-enabled implicit flow, rejecting duplicate client IDs on creation.</summary>
    private async Task ValidateClientAsync(
        string clientId,
        IReadOnlyList<string> redirectUris,
        bool allowImplicit,
        CancellationToken cancellationToken,
        bool existing = false)
    {
        if (string.IsNullOrWhiteSpace(clientId) || redirectUris.Any(uri => !ConnectUriRules.IsAllowedRedirectUri(uri)))
            throw new ConnectException("The client ID or redirect URI is invalid.");
        var options = await configurationService.GetConnectOptionsAsync(cancellationToken);
        if (allowImplicit && !options.AllowImplicitFlow)
            throw new ConnectException("Implicit and hybrid flows are disabled.");
        var found = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (!existing && found is not null) throw new ConnectException("The client ID is already registered.");
    }

    private static OpenIddictApplicationDescriptor CreateDescriptor(
        string clientId,
        string displayName,
        IReadOnlyList<string> redirectUris,
        bool confidential,
        bool allowImplicit,
        string? secret)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId.Trim(), DisplayName = displayName.Trim(), ClientSecret = secret,
            ClientType = confidential ? OpenIddictConstants.ClientTypes.Confidential : OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit
        };
        foreach (var uri in redirectUris.Select(value => new Uri(value.Trim()))) descriptor.RedirectUris.Add(uri);
        ApplyPermissions(descriptor, allowImplicit);
        return descriptor;
    }

    private static void ApplyPermissions(OpenIddictApplicationDescriptor descriptor, bool allowImplicit)
    {
        descriptor.Permissions.Clear();
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
        if (allowImplicit)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Implicit);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.IdToken);
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);
        }
    }

    private async Task<ConnectClientDto> ToClientDtoAsync(object application, CancellationToken cancellationToken)
    {
        var permissions = await applicationManager.GetPermissionsAsync(application, cancellationToken);
        return new ConnectClientDto(
            (await applicationManager.GetClientIdAsync(application, cancellationToken))!,
            await applicationManager.GetDisplayNameAsync(application, cancellationToken) ?? "OAuth client",
            (await applicationManager.GetRedirectUrisAsync(application, cancellationToken)).ToArray(),
            await applicationManager.HasClientTypeAsync(
                application, OpenIddictConstants.ClientTypes.Confidential, cancellationToken),
            permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.Implicit, StringComparer.Ordinal));
    }

    private static string CreateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static ConnectInteractionDto ToDto(ConnectPendingInteraction value)
        => new(value.InteractionId, value.ClientId, value.ClientName, value.Scopes,
            value.Profiles, value.DeviceFlow, value.ExpiresAt);
}
