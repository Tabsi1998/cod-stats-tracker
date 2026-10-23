using CodStats.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api;

/// <summary>Removes nonces whose timestamps could no longer be accepted anyway.</summary>
public sealed class NonceCleanup(IServiceScopeFactory scopes, TimeProvider clock, ILogger<NonceCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                var now = clock.GetUtcNow();
                await db.UsedNonces.Where(n => n.ExpiresAt < now).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Nonce cleanup failed; it runs again in ten minutes");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
