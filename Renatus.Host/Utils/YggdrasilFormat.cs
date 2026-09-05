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

    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex HexRegex();
}
