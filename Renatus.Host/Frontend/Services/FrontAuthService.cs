using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using HostLoginRequest = Renatus.Host.Data.LoginRequest;
using HostRegisterRequest = Renatus.Host.Data.RegisterRequest;
using Renatus.Host.Service;
using Renatus.Host.Frontend.Models;

namespace Renatus.Host.Frontend.Services;

public sealed class FrontAuthService(IServiceScopeFactory scopeFactory)
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<AuthService>()
                .LoginAsync(new HostLoginRequest(request.SessionKeyId, request.Login, request.EncryptedPassword), cancellationToken));
        return response.ToFrontend();
    }

    public async Task<AuthResponse> LoginAsync(LoginForm form, CancellationToken cancellationToken = default)
    {
        var sessionKey = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<AuthService>().CreateSessionKeyAsync(cancellationToken));
        var encryptedPassword = EncryptPassword(sessionKey.PublicKey, form.Password);
        return await LoginAsync(new LoginRequest(sessionKey.SessionKeyId, form.Login, encryptedPassword), cancellationToken);
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var response = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<AuthService>()
                .RegisterAsync(new HostRegisterRequest(request.SessionKeyId, request.Username, request.Email, request.NickName, request.EncryptedPassword), cancellationToken));
        return response.ToFrontend();
    }

    public async Task<AuthResponse> RegisterAsync(RegisterForm form, CancellationToken cancellationToken = default)
    {
        var sessionKey = await FrontendServiceInvoker.InvokeAsync(
            scopeFactory,
            services => services.GetRequiredService<AuthService>().CreateSessionKeyAsync(cancellationToken));
        var encryptedPassword = EncryptPassword(sessionKey.PublicKey, form.Password);
        return await RegisterAsync(
            new RegisterRequest(sessionKey.SessionKeyId, form.Username, form.Email, form.NickName, encryptedPassword),
            cancellationToken);
    }

    private static string EncryptPassword(string publicKeyPem, string password)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var payload = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.{password}";
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(payload), RSAEncryptionPadding.OaepSHA256);
        return Convert.ToBase64String(encrypted);
    }
}
