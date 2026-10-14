namespace Renatus.Host.Service.Union;

public static class UnionServiceCollectionExtensions
{
    /// <summary>Registers signed callbacks and recoverable sync with redirects disabled; the center client owns the total request timeout and response bound.</summary>
    public static IServiceCollection AddUnionMemberServices(this IServiceCollection services)
    {
        services.AddHttpClient<UnionCenterClient>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3)
            });
        services.AddScoped<UnionSignatureVerifier>();
        services.AddScoped<UnionSignatureFilter>();
        services.AddScoped<UnionService>();
        services.AddHostedService<UnionSynchronizationWorker>();
        return services;
    }
}

