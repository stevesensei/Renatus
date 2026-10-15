using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Data.Config;

namespace Renatus.Host.Service;

public sealed partial class AppConfigurationService
{
    private static readonly SemaphoreSlim YggdrasilConfigurationLock = new(1, 1);

    /// <summary>Reads only administrator-editable Yggdrasil fields; signing keys, metadata and derived URLs never enter the response DTO.</summary>
    public async Task<YggdrasilAdminConfigDto> GetYggdrasilAdminConfigAsync(CancellationToken cancellationToken)
    {
        await YggdrasilConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var raw = await dbContext.AppConfigurations.AsNoTracking()
                .Where(value => value.Key == AppConfigurationKeys.Yggdrasil)
                .Select(value => value.Value)
                .SingleAsync(cancellationToken);
            var options = JsonSerializer.Deserialize<YggdrasilOptions>(raw, JsonOptions)
                ?? throw new InvalidOperationException("Yggdrasil configuration contains invalid JSON.");
            return ToAdminDto(options);
        }
        finally
        {
            YggdrasilConfigurationLock.Release();
        }
    }

    /// <summary>Updates ten safe fields under a row lock while preserving every secret, metadata field and unknown JSON property value.</summary>
    public async Task<YggdrasilAdminConfigDto> UpdateYggdrasilAdminConfigAsync(
        UpdateYggdrasilAdminConfigRequest request,
        CancellationToken cancellationToken)
    {
        var uuidAlgorithm = request.UuidAlgorithm?.Trim().ToLowerInvariant() ?? string.Empty;
        var requested = new YggdrasilOptions
        {
            AuthServerEnabled = request.AuthServerEnabled,
            UuidAlgorithm = uuidAlgorithm,
            AccessTokenExpireHours = request.AccessTokenExpireHours,
            RefreshTokenExpireHours = request.RefreshTokenExpireHours,
            JoinSessionTtlSeconds = request.JoinSessionTtlSeconds,
            MaxProfileLookupCount = request.MaxProfileLookupCount,
            MaxActiveSessions = request.MaxActiveSessions,
            AuthenticationRateLimitMilliseconds = request.AuthenticationRateLimitMilliseconds,
            NonEmailLogin = request.NonEmailLogin,
            UsernameCheck = request.UsernameCheck
        };
        try
        {
            ValidateYggdrasilOptions(requested);
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException("Yggdrasil configuration values are outside the supported bounds.", nameof(request), exception);
        }

        await YggdrasilConfigurationLock.WaitAsync(cancellationToken);
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var row = await dbContext.AppConfigurations.FromSqlInterpolated(
                $"SELECT * FROM \"AppConfigurations\" WHERE \"Key\" = {AppConfigurationKeys.Yggdrasil} FOR UPDATE")
                .SingleAsync(cancellationToken);
            JsonObject json;
            try
            {
                json = JsonNode.Parse(row.Value)?.AsObject()
                    ?? throw new JsonException("Yggdrasil configuration is not an object.");
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                throw new InvalidOperationException("Yggdrasil configuration contains invalid JSON.", exception);
            }

            json["authServerEnabled"] = requested.AuthServerEnabled;
            json["uuidAlgorithm"] = requested.UuidAlgorithm;
            json["accessTokenExpireHours"] = requested.AccessTokenExpireHours;
            json["refreshTokenExpireHours"] = requested.RefreshTokenExpireHours;
            json["joinSessionTtlSeconds"] = requested.JoinSessionTtlSeconds;
            json["maxProfileLookupCount"] = requested.MaxProfileLookupCount;
            json["maxActiveSessions"] = requested.MaxActiveSessions;
            json["authenticationRateLimitMilliseconds"] = requested.AuthenticationRateLimitMilliseconds;
            json["nonEmailLogin"] = requested.NonEmailLogin;
            json["usernameCheck"] = requested.UsernameCheck;

            row.Value = json.ToJsonString(JsonOptions);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await cacheService.RemoveAsync(
                $"renatus:configuration:{AppConfigurationKeys.Yggdrasil}", cancellationToken);
            return ToAdminDto(requested);
        }
        finally
        {
            YggdrasilConfigurationLock.Release();
        }
    }

    private static YggdrasilAdminConfigDto ToAdminDto(YggdrasilOptions options) => new(
        options.AuthServerEnabled,
        options.UuidAlgorithm,
        options.AccessTokenExpireHours,
        options.RefreshTokenExpireHours,
        options.JoinSessionTtlSeconds,
        options.MaxProfileLookupCount,
        options.MaxActiveSessions,
        options.AuthenticationRateLimitMilliseconds,
        options.NonEmailLogin,
        options.UsernameCheck);
}
