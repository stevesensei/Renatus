using FrontAuthResponse = Renatus.Host.Frontend.Models.AuthResponse;
using FrontAuthUserDto = Renatus.Host.Frontend.Models.AuthUserDto;
using FrontDeletePlayerResponse = Renatus.Host.Frontend.Models.DeletePlayerResponse;
using FrontPlayerDto = Renatus.Host.Frontend.Models.PlayerDto;
using FrontTextureDto = Renatus.Host.Frontend.Models.TextureDto;
using FrontUserPlayersResponse = Renatus.Host.Frontend.Models.UserPlayersResponse;
using FrontUserTexturesResponse = Renatus.Host.Frontend.Models.UserTexturesResponse;
using HostAuthResponse = Renatus.Host.Data.AuthResponse;
using HostDeletePlayerResponse = Renatus.Host.Data.DeletePlayerResponse;
using HostPlayerDto = Renatus.Host.Data.PlayerDto;
using HostTextureDto = Renatus.Host.Data.TextureDto;
using HostUserPlayersResponse = Renatus.Host.Data.UserPlayersResponse;
using HostUserTexturesResponse = Renatus.Host.Data.UserTexturesResponse;
using FrontendModels = Renatus.Host.Frontend.Models;

namespace Renatus.Host.Frontend.Services;

public static class FrontendDtoMapper
{
    public static FrontAuthResponse ToFrontend(this HostAuthResponse response) => new(
        response.AccessToken,
        response.TokenType,
        response.ExpiresAt,
        new FrontAuthUserDto(
            response.User.Id,
            response.User.Username,
            response.User.NickName,
            response.User.Email,
            (FrontendModels.UserRole)response.User.Role));

    public static FrontUserPlayersResponse ToFrontend(this HostUserPlayersResponse response) => new(
        response.Players.Select(ToFrontend).ToArray(),
        response.MaxPlayerCount);

    public static FrontPlayerDto ToFrontend(this HostPlayerDto response) => new(
        response.Id,
        response.Name,
        (FrontendModels.SkinModel)response.SkinModel,
        response.SkinTextureId,
        response.CapeTextureId,
        response.CreatedAt,
        response.UpdatedAt);

    public static FrontDeletePlayerResponse ToFrontend(this HostDeletePlayerResponse response) => new(response.Id);

    public static FrontUserTexturesResponse ToFrontend(this HostUserTexturesResponse response) => new(
        response.Textures.Select(ToFrontend).ToArray());

    public static FrontTextureDto ToFrontend(this HostTextureDto response) => new(
        response.Id,
        (FrontendModels.TextureType)response.Type,
        response.IsPublic,
        (FrontendModels.SkinModel)response.SkinModel,
        response.Width,
        response.Height,
        response.Sha256,
        response.CreatedAt);
}
