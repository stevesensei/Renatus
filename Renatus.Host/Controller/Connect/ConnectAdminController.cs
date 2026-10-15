using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renatus.Host.Data;
using Renatus.Host.Service;
using Renatus.Host.Service.Connect;
using Renatus.Host.Data.Config;
using System.Text.Json;

namespace Renatus.Host.Controller.Connect;

[ApiController]
[Route("api/admin/connect")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class ConnectAdminController(
    ConnectManagementService service,
    AppConfigurationService configurationService,
    YggdrasilConnectOptions startupOptions,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("config")]
    public async Task<GlobalResponse<ConnectConfigurationDto>> GetConfiguration(CancellationToken cancellationToken)
    {
        var options = await service.GetOptionsAsync(cancellationToken);
        return GlobalResponse<ConnectConfigurationDto>.Success(
            AppConfigurationService.ToConnectConfiguration(options,
                JsonSerializer.Serialize(options) != JsonSerializer.Serialize(startupOptions)));
    }

    [HttpPut("config")]
    public async Task<GlobalResponse<ConnectConfigurationDto>> UpdateConfiguration(
        UpdateConnectConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return GlobalResponse<ConnectConfigurationDto>.Success(
                await configurationService.UpdateConnectOptionsAsync(request, cancellationToken, environment.IsDevelopment()));
        }
        catch (InvalidOperationException exception)
        {
            return GlobalResponse<ConnectConfigurationDto>.ResultError(message: exception.Message);
        }
    }

    [HttpGet("clients")]
    public async Task<GlobalResponse<IReadOnlyList<ConnectClientDto>>> GetClients(CancellationToken cancellationToken)
        => GlobalResponse<IReadOnlyList<ConnectClientDto>>.Success(await service.GetClientsAsync(cancellationToken));

    [HttpPost("clients")]
    public Task<GlobalResponse<ConnectClientSecretDto>> CreateClient(
        CreateConnectClientRequest request, CancellationToken cancellationToken)
        => ResultAsync(() => service.CreateClientAsync(request, cancellationToken));

    [HttpPut("clients/{clientId}")]
    public Task<GlobalResponse<ConnectClientDto>> UpdateClient(
        string clientId, UpdateConnectClientRequest request, CancellationToken cancellationToken)
        => ResultAsync(() => service.UpdateClientAsync(clientId, request, cancellationToken));

    [HttpPost("clients/{clientId}/rotate-secret")]
    public Task<GlobalResponse<ConnectClientSecretDto>> RotateSecret(
        string clientId, CancellationToken cancellationToken)
        => ResultAsync(() => service.RotateSecretAsync(clientId, cancellationToken));

    [HttpDelete("clients/{clientId}")]
    public async Task<GlobalResponse<object>> DeleteClient(string clientId, CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteClientAsync(clientId, cancellationToken);
            return GlobalResponse<object>.Success();
        }
        catch (ConnectException exception)
        {
            return GlobalResponse<object>.ResultError(message: exception.Message);
        }
    }

    private static async Task<GlobalResponse<T>> ResultAsync<T>(Func<Task<T>> operation)
    {
        try { return GlobalResponse<T>.Success(await operation()); }
        catch (ConnectException exception) { return GlobalResponse<T>.ResultError(message: exception.Message); }
    }
}
