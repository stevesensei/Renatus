using Microsoft.EntityFrameworkCore;
using Renatus.Host.Data;
using Renatus.Host.Entity;
using Renatus.Host.Utils;

namespace Renatus.Host.Service;

public sealed partial class YggdrasilService
{
    /// <summary>Accepts only a usable local Yggdrasil token bound to the owned Minecraft UUID, reuses PNG validation, and attaches a private texture.</summary>
    public async Task UploadProfileTextureAsync(string accessToken, string uuid, string type, string? model,
        IFormFile file, CancellationToken cancellationToken)
    {
        var (validated, player, textureType) = await AuthorizeTextureAsync(accessToken, uuid, type, cancellationToken);
        if (file.Length is < 1 or > 10485760)
            throw new YggdrasilException(400, "IllegalArgumentException", "Texture file size must be between 1 byte and 10 MiB.");
        var skinModel = model switch
        {
            null or "" or "default" => SkinModel.Default,
            "slim" => SkinModel.Slim,
            _ => throw new YggdrasilException(400, "IllegalArgumentException", "Invalid skin model.")
        };
        using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        try
        {
            var texture = await textureService.UploadTextureAsync(player.UserId,
                new UploadTextureRequest(textureType, Convert.ToBase64String(content.ToArray()), false, skinModel), cancellationToken);
            await playerService.UpdateYggdrasilTextureAsync(player.UserId, player.Id, player.MinecraftUuid,
                validated.Client.Id, validated.Client.Version, validated.IssuedAtMilliseconds, validated.ExpiresAtMilliseconds,
                textureType, texture.Id, skinModel, cancellationToken);
        }
        catch (TextureException exception) { throw TextureError(exception.StatusCode, exception.Message); }
        catch (PlayerException exception) { throw TextureError(exception.StatusCode, exception.Message); }
    }

    /// <summary>Clears only the selected owned profile's requested texture using local Yggdrasil authorization and shared player validation.</summary>
    public async Task ResetProfileTextureAsync(string accessToken, string uuid, string type, CancellationToken cancellationToken)
    {
        var (validated, player, textureType) = await AuthorizeTextureAsync(accessToken, uuid, type, cancellationToken);
        try
        {
            await playerService.UpdateYggdrasilTextureAsync(player.UserId, player.Id, player.MinecraftUuid,
                validated.Client.Id, validated.Client.Version, validated.IssuedAtMilliseconds, validated.ExpiresAtMilliseconds,
                textureType, null, SkinModel.Default, cancellationToken);
        }
        catch (PlayerException exception) { throw TextureError(exception.StatusCode, exception.Message); }
    }

    /// <summary>Separates texture Bearer tokens from frontend HMAC JWTs and requires the bound profile, not merely another profile owned by the same user.</summary>
    private async Task<(ValidatedAccessToken Token, Player Player, TextureType Type)> AuthorizeTextureAsync(
        string accessToken, string uuid, string type, CancellationToken cancellationToken)
    {
        var minecraftUuid = YggdrasilFormat.ParseUnsignedUuid(uuid);
        var textureType = type switch
        {
            "skin" => TextureType.Skin,
            "cape" => TextureType.Cape,
            _ => throw new YggdrasilException(400, "IllegalArgumentException", "Invalid texture type.")
        };
        var validated = await ValidateAccessTokenAsync(accessToken, null, true, false, cancellationToken);
        var player = validated.Client.Player!;
        if (player.MinecraftUuid != minecraftUuid || player.UserId != validated.Client.UserId)
            throw YggdrasilException.InvalidToken();
        return (validated, player, textureType);
    }

    private static YggdrasilException TextureError(int statusCode, string message) =>
        new(statusCode, statusCode == 400 ? "IllegalArgumentException" : "ForbiddenOperationException", message);
}
