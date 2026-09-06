using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class TextureApi(ApiClient apiClient)
{
    public Task<TextureDto> UploadAsync(
        UploadTextureRequest request,
        CancellationToken cancellationToken = default) =>
        apiClient.PostAsync<TextureDto>("/api/texture", request, cancellationToken);
}
