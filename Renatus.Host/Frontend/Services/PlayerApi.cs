using Microsoft.Extensions.DependencyInjection;
using HostCreatePlayerRequest = Renatus.Host.Data.CreatePlayerRequest;
using HostUpdatePlayerTexturesRequest = Renatus.Host.Data.UpdatePlayerTexturesRequest;
using Renatus.Host.Service;
using Renatus.Host.Frontend.Models;

namespace Renatus.Host.Frontend.Services;

public sealed class PlayerApi(
    IServiceScopeFactory scopeFactory,
    FrontendUserContext userContext)
{
    public async Task<UserPlayersResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<PlayerService>().GetUserPlayersAsync(userId, cancellationToken));
        return response.ToFrontend();
    }

    public async Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<PlayerService>().CreatePlayerAsync(
                userId,
                new HostCreatePlayerRequest(
                    request.Name,
                    (Renatus.Host.Entity.SkinModel)request.SkinModel,
                    request.SkinTextureId,
                    request.CapeTextureId),
                cancellationToken));
        return response.ToFrontend();
    }

    public async Task<PlayerDto> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<PlayerService>().GetPlayerAsync(userId, playerId, cancellationToken));
        return response.ToFrontend();
    }

    public async Task<PlayerDto> UpdateTexturesAsync(
        Guid playerId,
        UpdatePlayerTexturesRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<PlayerService>().UpdatePlayerTexturesAsync(
                userId,
                playerId,
                new HostUpdatePlayerTexturesRequest(
                    request.SkinTextureId,
                    request.CapeTextureId,
                    request.SkinModel is null ? null : (Renatus.Host.Entity.SkinModel?)request.SkinModel),
                cancellationToken));
        return response.ToFrontend();
    }

    public async Task<DeletePlayerResponse> DeleteAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var userId = await userContext.GetUserIdAsync();
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<PlayerService>().DeletePlayerAsync(userId, playerId, cancellationToken));
        return response.ToFrontend();
    }
}
