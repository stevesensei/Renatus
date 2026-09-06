using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class PlayerApi(ApiClient apiClient)
{
    public Task<UserPlayersResponse> ListAsync(CancellationToken cancellationToken = default) =>
        apiClient.GetAsync<UserPlayersResponse>("/api/player", cancellationToken);

    public Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken cancellationToken = default) =>
        apiClient.PostAsync<PlayerDto>("/api/player", request, cancellationToken);

    public Task<PlayerDto> GetAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        apiClient.GetAsync<PlayerDto>($"/api/player/{playerId:D}", cancellationToken);

    public Task<PlayerDto> UpdateTexturesAsync(
        Guid playerId,
        UpdatePlayerTexturesRequest request,
        CancellationToken cancellationToken = default) =>
        apiClient.PutAsync<PlayerDto>($"/api/player/{playerId:D}/textures", request, cancellationToken);

    public Task<DeletePlayerResponse> DeleteAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        apiClient.DeleteAsync<DeletePlayerResponse>($"/api/player/{playerId:D}", cancellationToken);
}
