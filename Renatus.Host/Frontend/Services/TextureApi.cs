using Microsoft.Extensions.DependencyInjection;
using HostUploadTextureRequest = Renatus.Host.Data.UploadTextureRequest;
using Renatus.Host.Service;
using Renatus.Host.Frontend.Models;

namespace Renatus.Host.Frontend.Services;

public sealed class TextureApi(
    IServiceScopeFactory scopeFactory,
    FrontendUserContext userContext)
{
    public async Task<UserTexturesResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<TextureService>().GetUserTexturesAsync(userId, cancellationToken));
        return response.ToFrontend();
    }

    public async Task<TextureDto> UploadAsync(
        UploadTextureRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<TextureService>().UploadTextureAsync(
                userId,
                new HostUploadTextureRequest(
                    (Renatus.Host.Entity.TextureType?)request.Type,
                    request.DataBase64,
                    request.IsPublic,
                    (Renatus.Host.Entity.SkinModel)request.SkinModel),
                cancellationToken));
        return response.ToFrontend();
    }

    public async Task<TextureDto> DeleteAsync(Guid textureId, CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<TextureService>().DeleteTextureAsync(userId, textureId, cancellationToken));
        return response.ToFrontend();
    }
}
