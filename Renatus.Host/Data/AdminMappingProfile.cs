using AutoMapper;
using Renatus.Host.Entity;

namespace Renatus.Host.Data;

public static class AdminMappingContextKeys
{
    public const string OwnerUserName = nameof(OwnerUserName);
    public const string UploaderId = nameof(UploaderId);
    public const string UploaderName = nameof(UploaderName);
}

public sealed class AdminMappingProfile : Profile
{
    public AdminMappingProfile()
    {
        CreateMap<User, AdminUserListItemDto>();

        CreateMap<User, AdminUserDetailDto>()
            .ForCtorParam(
                nameof(AdminUserDetailDto.Sessions),
                options => options.MapFrom((user, _) => user.Clients));

        CreateMap<Player, AdminPlayerListItemDto>()
            .ForCtorParam(
                nameof(AdminPlayerListItemDto.UserName),
                options => options.MapFrom((player, context) => ResolveOwnerUserName(player, context)));

        CreateMap<Player, AdminPlayerDetailDto>()
            .ForCtorParam(
                nameof(AdminPlayerDetailDto.UserName),
                options => options.MapFrom((player, parentContext) => ResolveOwnerUserName(player, parentContext)));

        CreateMap<Texture, AdminTextureRefDto>();

        CreateMap<Texture, AdminTextureListItemDto>()
            .ForCtorParam(
                nameof(AdminTextureListItemDto.UploaderId),
                options => options.MapFrom((texture, context) => ResolveUploaderId(texture, context)))
            .ForCtorParam(
                nameof(AdminTextureListItemDto.UploaderName),
                options => options.MapFrom((texture, context) => ResolveUploaderName(texture, context)));

        CreateMap<Texture, AdminTextureDetailDto>()
            .ForCtorParam(
                nameof(AdminTextureDetailDto.UploaderId),
                options => options.MapFrom((texture, context) => ResolveUploaderId(texture, context)))
            .ForCtorParam(
                nameof(AdminTextureDetailDto.UploaderName),
                options => options.MapFrom((texture, context) => ResolveUploaderName(texture, context)))
            .ForCtorParam(
                nameof(AdminTextureDetailDto.SizeBytes),
                options => options.MapFrom((texture, _) => Convert.FromBase64String(texture.DataBase64).Length))
            .ForCtorParam(
                nameof(AdminTextureDetailDto.DataUrl),
                options => options.MapFrom(
                    (texture, _) => $"data:{texture.ContentType};base64,{texture.DataBase64}"));

        CreateMap<YggdrasilClient, AdminSessionDto>()
            .ForCtorParam(
                nameof(AdminSessionDto.PlayerName),
                options => options.MapFrom((client, _) => client.Player == null ? null : client.Player.Name));

        CreateMap<YggdrasilClient, AdminRecentActivityDto>()
            .ForCtorParam(
                nameof(AdminRecentActivityDto.ClientId),
                options => options.MapFrom((client, _) => client.Id))
            .ForCtorParam(
                nameof(AdminRecentActivityDto.UserName),
                options => options.MapFrom((client, _) => client.User == null ? string.Empty : client.User.UserName))
            .ForCtorParam(
                nameof(AdminRecentActivityDto.PlayerName),
                options => options.MapFrom((client, _) => client.Player == null ? null : client.Player.Name));
    }

    private static string ResolveOwnerUserName(Player player, ResolutionContext context)
    {
        if (context.Items.TryGetValue(AdminMappingContextKeys.OwnerUserName, out var owner) && owner is string ownerName)
        {
            return ownerName;
        }

        return player.User == null ? string.Empty : player.User.UserName;
    }

    private static Guid? ResolveUploaderId(Texture texture, ResolutionContext context)
    {
        if (context.Items.TryGetValue(AdminMappingContextKeys.UploaderId, out var uploader) && uploader is Guid uploaderId)
        {
            return uploaderId;
        }

        return texture.UserId;
    }

    private static string ResolveUploaderName(Texture texture, ResolutionContext context)
    {
        if (context.Items.TryGetValue(AdminMappingContextKeys.UploaderName, out var name) && name is string uploaderName)
        {
            return uploaderName;
        }

        if (texture.User is null)
        {
            return "Renatus";
        }

        return string.IsNullOrWhiteSpace(texture.User.NickName) ? texture.User.UserName : texture.User.NickName;
    }
}
