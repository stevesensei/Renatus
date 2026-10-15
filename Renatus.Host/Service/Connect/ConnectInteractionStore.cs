using System.Text.Json;
using Renatus.Host.Data;
using StackExchange.Redis;

namespace Renatus.Host.Service.Connect;

public sealed record ConnectPendingInteraction(
    string InteractionId,
    string ClientId,
    string ClientName,
    string[] Scopes,
    ConnectProfileDto[] Profiles,
    bool DeviceFlow,
    DateTimeOffset ExpiresAt,
    string CompletionUrl);

public sealed record ConnectCompletedInteraction(
    ConnectPendingInteraction Pending,
    Guid UserId,
    Guid? PlayerMinecraftUuid,
    bool Approved);

public sealed class ConnectInteractionStore(IConnectionMultiplexer connectionMultiplexer)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    public async Task<ConnectPendingInteraction> CreateAsync(
        string clientId,
        string clientName,
        IEnumerable<string> scopes,
        IEnumerable<ConnectProfileDto> profiles,
        bool deviceFlow,
        string completionUrl)
    {
        var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var interaction = new ConnectPendingInteraction(id, clientId, clientName, scopes.ToArray(), profiles.ToArray(),
            deviceFlow, expiresAt, completionUrl.Replace("__INTERACTION__", Uri.EscapeDataString(id), StringComparison.Ordinal));
        await _database.StringSetAsync(PendingKey(id), JsonSerializer.Serialize(interaction, JsonOptions),
            expiresAt - DateTimeOffset.UtcNow, When.NotExists);
        return interaction;
    }

    public async Task<ConnectPendingInteraction?> GetAsync(string interactionId)
    {
        var value = await _database.StringGetAsync(PendingKey(interactionId));
        return value.HasValue ? JsonSerializer.Deserialize<ConnectPendingInteraction>(value.ToString(), JsonOptions) : null;
    }

    /// <summary>Atomically consumes the pending interaction before exposing its completion URL, enforcing a single authenticated decision.</summary>
    public async Task<ConnectCompletedInteraction?> DecideAsync(
        string interactionId,
        Guid userId,
        Guid? playerMinecraftUuid,
        bool approved)
    {
        var value = await _database.StringGetDeleteAsync(PendingKey(interactionId));
        if (!value.HasValue) return null;
        var pending = JsonSerializer.Deserialize<ConnectPendingInteraction>(value.ToString(), JsonOptions);
        if (pending is null || pending.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        var completed = new ConnectCompletedInteraction(pending, userId, playerMinecraftUuid, approved);
        await _database.StringSetAsync(CompletedKey(interactionId), JsonSerializer.Serialize(completed, JsonOptions),
            TimeSpan.FromMinutes(2), When.NotExists);
        return completed;
    }

    public async Task<ConnectCompletedInteraction?> ConsumeCompletedAsync(string interactionId)
    {
        var value = await _database.StringGetDeleteAsync(CompletedKey(interactionId));
        return value.HasValue
            ? JsonSerializer.Deserialize<ConnectCompletedInteraction>(value.ToString(), JsonOptions)
            : null;
    }

    private static string PendingKey(string id) => $"connect:interaction:pending:{id}";
    private static string CompletedKey(string id) => $"connect:interaction:completed:{id}";
}
