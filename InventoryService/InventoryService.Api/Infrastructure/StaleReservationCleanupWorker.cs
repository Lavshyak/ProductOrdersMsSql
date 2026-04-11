using InventoryService.Api.Application;
using Microsoft.Extensions.Options;

namespace InventoryService.Api.Infrastructure;

public sealed class StaleReservationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationCleanupOptions> options,
    ILogger<StaleReservationCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = options.Value.PollingInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var inventoryService = scope.ServiceProvider.GetRequiredService<IInventoryService>();
                await inventoryService.CancelExpiredReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during stale reservation cleanup loop.");
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}

