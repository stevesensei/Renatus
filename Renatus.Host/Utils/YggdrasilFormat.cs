using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public static partial class YggdrasilFormat
{
    public static string ToUnsignedId(Guid id)
    {
        return id.ToString("N");
    }

    public static Guid ParseUnsignedUuid(string value)
    {
        if (value.Length != 32 || !HexRegex().IsMatch(value))
        {
            throw new YggdrasilException(StatusCodes.Status400BadRequest, "IllegalArgumentException", $"Invalid UUID string: {value}");
        }

        return Guid.ParseExact(value, "N");
    }

    public static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    public static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Matches Java nameUUIDFromBytes for UTF-8 OfflinePlayer:name: MD5 plus RFC version 3/variant bits, parsed as network-order hex to avoid Guid byte-endian reversal.</summary>
    public static Guid CreateOfflinePlayerUuid(string playerName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{playerName}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return Guid.ParseExact(Convert.ToHexString(bytes), "N");
    }

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex HexRegex();
}
