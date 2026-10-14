using System.Text.Json;
using System.Text.Json.Serialization;

namespace Renatus.Host.Data.Config;

public sealed class UnionState
{
    public long ServerListVersion { get; set; }
    public long PrivateKeyVersion { get; set; }
    /// <summary>Persisted center texture key; never replaces frontend or traditional Yggdrasil JWT signing material.</summary>
    public string TextureSigningPrivateKeyPem { get; set; } = string.Empty;
    public List<UnionBackend> Servers { get; set; } = [];
    /// <summary>Retry hint retained across outages; reconciliation also compares the full current profile checksum.</summary>
    public bool NeedsProfileSync { get; set; } = true;
    /// <summary>Incremented by local mutations so an in-flight sync cannot clear newer work.</summary>
    public long ProfileSyncRevision { get; set; }
    public long LastProfileSyncRevision { get; set; } = -1;
    public string LastProfileChecksum { get; set; } = string.Empty;
    public DateTimeOffset? LastProfileSyncAt { get; set; }
    public string? LastProfileSyncError { get; set; }
}

public sealed record UnionBackend(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("bs_root")] string BsRoot)
{
    /// <summary>Preserves center-supplied directory extensions without binding extension data to a JSON constructor parameter.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}


