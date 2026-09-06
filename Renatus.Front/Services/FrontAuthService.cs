using System.Security.Cryptography;
using System.Text;
using Renatus.Front.Models;

namespace Renatus.Front.Services;

public sealed class FrontAuthService(ApiClient apiClient)
{
    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        => apiClient.PostAsync<AuthResponse>("/api/auth/login", request, cancellationToken);

    public async Task<AuthResponse> LoginAsync(LoginForm form, CancellationToken cancellationToken = default)
    {
        var sessionKey = await apiClient.PostAsync<SessionKeyResponse>("/api/auth/session-key", null, cancellationToken);
        var encryptedPassword = EncryptPassword(sessionKey.PublicKey, form.Password);
        return await LoginAsync(new LoginRequest(sessionKey.SessionKeyId, form.Login, encryptedPassword), cancellationToken);
    }

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        => apiClient.PostAsync<AuthResponse>("/api/auth/register", request, cancellationToken);

    public async Task<AuthResponse> RegisterAsync(RegisterForm form, CancellationToken cancellationToken = default)
    {
        var sessionKey = await apiClient.PostAsync<SessionKeyResponse>("/api/auth/session-key", null, cancellationToken);
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
