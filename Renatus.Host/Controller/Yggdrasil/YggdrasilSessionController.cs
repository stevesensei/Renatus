using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Utils;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;
using Renatus.Host.Service.Connect;
using System.Text.Json;

namespace Renatus.Host.Controller.Yggdrasil;

[Route("api/yggdrasil/sessionserver/session/minecraft")]
public sealed class YggdrasilSessionController(
    YggdrasilService yggdrasilService,
    ConnectSessionService connectSessionService,
    IAuthenticationSchemeProvider schemeProvider,
    ILogger<YggdrasilSessionController> logger)
    : YggdrasilControllerBase
{
    /// <summary>记录入服请求的认证分支及最终状态，不记录访问令牌。</summary>
    [HttpPost("join")]
    public async Task<IActionResult> Join(JoinRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("联盟入服 join 开始：角色UUID={ProfileId}，服务器摘要={ServerHash}，携带认证头={HasAuthorization}，追踪ID={TraceId}",
            JsonSerializer.Serialize(request.SelectedProfile), YggdrasilFormat.Sha256Hex(request.ServerId),
            Request.Headers.Authorization.Count > 0, (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
        try
        {
            if (Request.Headers.Authorization.Count > 0)
            {
                logger.LogInformation("联盟入服 join 使用 Connect 认证头分支，追踪ID={TraceId}", (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
                if (await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is null)
                    throw YggdrasilException.InvalidToken();
                var result = await HttpContext.AuthenticateAsync(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                if (!result.Succeeded || result.Principal is null) throw YggdrasilException.InvalidToken();
                await connectSessionService.JoinAsync(result.Principal, request, cancellationToken);
            }
            else if (await schemeProvider.GetSchemeAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme) is not null
                && !string.IsNullOrWhiteSpace(request.AccessToken))
            {
                Request.Headers.Authorization = $"Bearer {request.AccessToken}";
                var result = await HttpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                if (result.Succeeded && result.Principal is not null)
                {
                    logger.LogInformation("联盟入服 join 使用 Connect 请求体令牌分支，追踪ID={TraceId}", (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
                    await connectSessionService.JoinAsync(result.Principal, request, cancellationToken);
                }
                else
                {
                    logger.LogInformation("联盟入服 join 请求体令牌未通过 Connect 认证，转入传统 Yggdrasil 分支，追踪ID={TraceId}", (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
                    await yggdrasilService.JoinAsync(request, cancellationToken);
                }
            }
            else
            {
                logger.LogInformation("联盟入服 join 使用传统 Yggdrasil 分支，追踪ID={TraceId}", (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
                await yggdrasilService.JoinAsync(request, cancellationToken);
            }
            logger.LogInformation("联盟入服 join 完成：HTTP=204，追踪ID={TraceId}", (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
            return NoContent();
        }
        catch (YggdrasilException exception)
        {
            logger.LogInformation("联盟入服 join 拒绝：HTTP={StatusCode}，错误类型={Error}，原因={Reason}，追踪ID={TraceId}",
                exception.StatusCode, exception.Error, exception.ErrorMessage, (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
            return YggdrasilError(exception);
        }
    }

    /// <summary>记录联盟会话查询和最终返回的身份，用服务器摘要关联重复查询。</summary>
    [HttpGet("hasJoined")]
    public async Task<ActionResult<SessionProfileResponse>> HasJoined(
        [FromQuery] string username,
        [FromQuery] string serverId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("联盟入服 hasJoined 开始：角色名={PlayerName}，服务器摘要={ServerHash}，追踪ID={TraceId}",
            JsonSerializer.Serialize(username), YggdrasilFormat.Sha256Hex(serverId), (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
        var response = await connectSessionService.HasJoinedAsync(username, serverId, cancellationToken)
            ?? await yggdrasilService.HasJoinedAsync(username, serverId, cancellationToken);
        logger.LogInformation("联盟入服 hasJoined 完成：HTTP={StatusCode}，返回UUID={ProfileId}，返回角色名={PlayerName}，追踪ID={TraceId}",
            response is null ? 204 : 200, response?.Id, JsonSerializer.Serialize(response?.Name), (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
        return response is null ? NoContent() : Ok(response);
    }

    /// <summary>记录按 UUID 查询资料的结果，便于区分主角色资料缺失和会话校验失败。</summary>
    [HttpGet("profile/{id}")]
    public async Task<ActionResult<SessionProfileResponse>> Profile(
        string id,
        [FromQuery] string? unsigned,
        CancellationToken cancellationToken)
    {
        try
        {
            var playerId = YggdrasilFormat.ParseUnsignedUuid(id);
            var response = await yggdrasilService.GetSessionProfileAsync(playerId, unsigned != "false", cancellationToken);
            logger.LogInformation("联盟角色资料查询：请求UUID={RequestedId}，请求签名={Signed}，HTTP={StatusCode}，返回UUID={ProfileId}，返回角色名={PlayerName}，追踪ID={TraceId}",
                playerId, unsigned == "false", response is null ? 204 : 200, response?.Id,
                JsonSerializer.Serialize(response?.Name), (Activity.Current?.Id ?? HttpContext.TraceIdentifier));
            return response is null ? NoContent() : Ok(response);
        }
        catch (YggdrasilException exception)
        {
            return YggdrasilError(exception);
        }
    }
}
