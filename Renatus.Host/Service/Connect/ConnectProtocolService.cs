using System.Security.Claims;
using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Renatus.Host.Data;

namespace Renatus.Host.Service.Connect;

public sealed class ConnectProtocolService(
    AppDbContext dbContext,
    ConnectInteractionStore interactionStore,
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager)
{
    /// <summary>Requires OpenID for Yggdrasil scopes, excludes combined Read/Select, and requires Select for Join.</summary>
    public static void ValidateScopes(IEnumerable<string> values)
    {
        var scopes = values.ToHashSet(StringComparer.Ordinal);
        var hasYggdrasilScope = scopes.Contains(ConnectConstants.ProfileReadScope)
            || scopes.Contains(ConnectConstants.ProfileSelectScope)
            || scopes.Contains(ConnectConstants.ServerJoinScope);
        if ((hasYggdrasilScope && !scopes.Contains(OpenIddictConstants.Scopes.OpenId))
            || (scopes.Contains(ConnectConstants.ProfileReadScope)
                && scopes.Contains(ConnectConstants.ProfileSelectScope))
            || (scopes.Contains(ConnectConstants.ServerJoinScope)
                && !scopes.Contains(ConnectConstants.ProfileSelectScope)))
            throw new ConnectException("The requested scope combination is invalid.");
    }

    /// <summary>Stores a bounded consent request for a registered client without accepting frontend credentials through protocol URLs.</summary>
    public async Task<ConnectPendingInteraction> CreateInteractionAsync(
        string clientId,
        IEnumerable<string> scopes,
        bool deviceFlow,
        string completionUrl,
        CancellationToken cancellationToken)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new ConnectException("The OAuth client is unavailable.");
        return await interactionStore.CreateAsync(clientId,
            await applicationManager.GetDisplayNameAsync(application, cancellationToken) ?? "OAuth client",
            scopes, [], deviceFlow, completionUrl);
    }

    /// <summary>Consumes the authenticated decision once; the controller also binds it to the original protocol request.</summary>
    public Task<ConnectCompletedInteraction?> ConsumeCompletedAsync(string id)
        => interactionStore.ConsumeCompletedAsync(id);

    /// <summary>Reloads the active verified account and owned profile before issuing scoped JSON profile claims, keeping the internal UUID binding out of ID tokens.</summary>
    public async Task<ClaimsPrincipal> CreatePrincipalAsync(
        Guid userId,
        Guid? playerMinecraftUuid,
        ImmutableArray<string> scopes,
        string clientId,
        CancellationToken cancellationToken)
    {
        ValidateScopes(scopes);
        var user = await dbContext.Users.AsNoTracking().Include(entity => entity.Players)
            .SingleOrDefaultAsync(entity => entity.Id == userId && !entity.IsDisabled
                && entity.EmailVerifiedAt != null, cancellationToken)
            ?? throw new ConnectException("The account is inactive or its email is unverified.");
        var player = playerMinecraftUuid is null ? null : user.Players.SingleOrDefault(
            entity => entity.MinecraftUuid == playerMinecraftUuid.Value);
        if (scopes.Contains(ConnectConstants.ProfileSelectScope) && player is null)
            throw new ConnectException("The selected player is unavailable.");

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString("D"));
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.NickName);
        identity.SetClaim(OpenIddictConstants.Claims.PreferredUsername, user.UserName);
        identity.SetClaim(OpenIddictConstants.Claims.Email, user.Email);
        identity.SetClaim(OpenIddictConstants.Claims.EmailVerified, true);
        if (player is not null)
        {
            identity.SetClaim(ConnectConstants.SelectedProfileBindingClaim, player.MinecraftUuid.ToString("N"));
            identity.SetClaim(ConnectConstants.SelectedProfileClaim, JsonSerializer.SerializeToElement(ToProtocolProfile(player)));
        }
        if (scopes.Contains(ConnectConstants.ProfileReadScope))
            identity.SetClaim(ConnectConstants.AvailableProfilesClaim,
                JsonSerializer.SerializeToElement(user.Players.Select(ToProtocolProfile).ToArray()));
        identity.SetScopes(scopes);
        identity.SetDestinations(claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.PreferredUsername
                when scopes.Contains(OpenIddictConstants.Scopes.Profile)
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            OpenIddictConstants.Claims.Email or OpenIddictConstants.Claims.EmailVerified
                when scopes.Contains(OpenIddictConstants.Scopes.Email)
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            ConnectConstants.SelectedProfileClaim or ConnectConstants.AvailableProfilesClaim
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            ConnectConstants.SelectedProfileBindingClaim => [OpenIddictConstants.Destinations.AccessToken],
            OpenIddictConstants.Claims.Subject
                => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            _ => [OpenIddictConstants.Destinations.AccessToken]
        });

        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken)
            ?? throw new ConnectException("The OAuth client is unavailable.");
        var applicationId = await applicationManager.GetIdAsync(application, cancellationToken)
            ?? throw new ConnectException("The OAuth client is unavailable.");
        var authorization = await FindReusableAuthorizationAsync(
            user.Id.ToString("D"), applicationId, scopes, cancellationToken);
        if (authorization is null)
        {
            authorization = await authorizationManager.CreateAsync(
                identity, user.Id.ToString("D"), applicationId,
                OpenIddictConstants.AuthorizationTypes.Permanent, scopes, cancellationToken);
        }
        identity.SetAuthorizationId(await authorizationManager.GetIdAsync(authorization, cancellationToken));
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Rebuilds exchanged principals from current account/profile state and preserves the selected internal UUID binding.</summary>
    public async Task<ClaimsPrincipal> RefreshPrincipalAsync(
        ClaimsPrincipal principal,
        string clientId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.GetClaim(OpenIddictConstants.Claims.Subject), out var userId))
            throw new ConnectException("The token subject is invalid.");
        Guid? playerId = null;
        var value = principal.GetClaim(ConnectConstants.SelectedProfileBindingClaim);
        if (!string.IsNullOrWhiteSpace(value)) playerId = Guid.ParseExact(value, "N");
        return await CreatePrincipalAsync(userId, playerId, principal.GetScopes(), clientId, cancellationToken);
    }

    /// <summary>Returns only granted profile/email fields after rechecking the active verified account and selected-profile ownership.</summary>
    public async Task<object> GetUserInfoAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.GetClaim(OpenIddictConstants.Claims.Subject), out var userId))
            throw new ConnectException("The token subject is invalid.");
        var user = await dbContext.Users.AsNoTracking().Include(entity => entity.Players)
            .SingleOrDefaultAsync(entity => entity.Id == userId && !entity.IsDisabled
                && entity.EmailVerifiedAt != null, cancellationToken)
            ?? throw new ConnectException("The account is unavailable.");
        var scopes = principal.GetScopes();
        var response = new Dictionary<string, object> { ["sub"] = user.Id.ToString("D") };
        if (scopes.Contains(OpenIddictConstants.Scopes.Profile))
        {
            response["name"] = user.NickName;
            response["preferred_username"] = user.UserName;
        }
        if (scopes.Contains(OpenIddictConstants.Scopes.Email))
        {
            response["email"] = user.Email;
            response["email_verified"] = true;
        }
        if (scopes.Contains(ConnectConstants.ProfileReadScope))
            response["availableProfiles"] = user.Players.Select(ToProtocolProfile).ToArray();
        if (scopes.Contains(ConnectConstants.ProfileSelectScope))
        {
            var selected = principal.GetClaim(ConnectConstants.SelectedProfileBindingClaim);
            var player = user.Players.SingleOrDefault(entity => entity.MinecraftUuid.ToString("N") == selected)
                ?? throw new ConnectException("The selected player is unavailable.");
            response["selectedProfile"] = ToProtocolProfile(player);
        }
        return response;
    }

    private async Task<object?> FindReusableAuthorizationAsync(
        string subject,
        string applicationId,
        ImmutableArray<string> scopes,
        CancellationToken cancellationToken)
    {
        await foreach (var authorization in authorizationManager.FindAsync(subject, applicationId,
            OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent,
            scopes, cancellationToken)) return authorization;
        return null;
    }

    private static object ToProtocolProfile(Entity.Player player)
        => new { id = player.MinecraftUuid.ToString("N"), name = player.Name };
}
