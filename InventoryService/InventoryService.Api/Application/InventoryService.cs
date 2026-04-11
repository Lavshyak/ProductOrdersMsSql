using System.Data;
using System.Text.Json;
using InventoryService.Api.Domain;
using InventoryService.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared;

namespace InventoryService.Api.Application;

public sealed partial class InventoryService(
    InventoryDbContext dbContext,
    IOptions<ReservationCleanupOptions> cleanupOptions,
    TimeProvider timeProvider) : IInventoryService
{
    public Task<OperationResult> CancelReservationAsync(Guid reservationId,
        CancellationToken cancellationToken)
        => RemoveReservationAsync(reservationId, updateStock: true, cancellationToken);

    public Task<OperationResult> WriteOffReservationAsync(Guid reservationId, CancellationToken cancellationToken)
        => RemoveReservationAsync(reservationId, updateStock: false, cancellationToken);

    public async Task<int?> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken)
    {
        var stock = await dbContext.StockItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == productId, cancellationToken);

        return stock?.AvailableQuantity;
    }
    
    /*private void AddOutboxMessageToContext(string eventType, object payload, DateTimeOffset occurredAtUtc)
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = occurredAtUtc
        });
    }*/
}