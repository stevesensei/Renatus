using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;

namespace Renatus.Host.Controller;

[ApiController]
[Route("api/texture")]
[Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
public sealed class TextureController(TextureService textureService) : ControllerBase
{
    [HttpPost]
    public async Task<GlobalResponse<TextureDto>> UploadTexture(
        UploadTextureRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<TextureDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await textureService.UploadTextureAsync(userId, request, cancellationToken);
            return GlobalResponse<TextureDto>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<TextureDto>.ResultError(message: exception.Message);
        }
    }

    [HttpGet]
    public async Task<GlobalResponse<UserTexturesResponse>> GetCurrentUserTextures(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<UserTexturesResponse>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await textureService.GetUserTexturesAsync(userId, cancellationToken);
            return GlobalResponse<UserTexturesResponse>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<UserTexturesResponse>.ResultError(message: exception.Message);
        }
    }

    [HttpDelete("{textureId:guid}")]
    public async Task<GlobalResponse<TextureDto>> DeleteTexture(Guid textureId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return GlobalResponse<TextureDto>.ResultError(message: "Authorization token is invalid.");
        }

        try
        {
            var response = await textureService.DeleteTextureAsync(userId, textureId, cancellationToken);
            return GlobalResponse<TextureDto>.Success(response);
        }
        catch (TextureException exception)
        {
            return GlobalResponse<TextureDto>.ResultError(message: exception.Message);
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out userId);
    }
}
