namespace Renatus.Host.Service.Union;

internal sealed class UnionSynchronizationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<UnionSynchronizationWorker> logger) : BackgroundService
{
    /// <summary>Reconciles on startup and every 30 seconds, so committed CRUD reaches an unavailable center when it recovers.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        await ReconcileAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(stoppingToken);
        }
    }

    /// <summary>Creates a fresh scope per attempt; a failure preserves service retry state and cannot terminate future reconciliation.</summary>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<UnionService>();
            await service.ReconcileProfilesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning("Union profile reconciliation failed ({ErrorType}) and will be retried.", exception.GetType().Name);
        }
    }
}

