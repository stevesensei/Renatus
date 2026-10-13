using System.Text.Json.Serialization;

namespace Renatus.Host.Data.Config;

public sealed class UnionState
{
    public long ServerListVersion { get; set; }
    public long PrivateKeyVersion { get; set; }
    public string TextureSigningPrivateKeyPem { get; set; } = string.Empty;
    public List<UnionBackend> Servers { get; set; } = [];
    public bool NeedsProfileSync { get; set; } = true;
    public DateTimeOffset? LastProfileSyncAt { get; set; }
}

public sealed record UnionBackend(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("bs_root")] string BsRoot);
