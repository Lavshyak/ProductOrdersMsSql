using System.Data;
using System.Text.Json;
using InventoryService.Api.Domain;
using InventoryService.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InventoryService.Api.Application;

public sealed class InventoryService(
    InventoryDbContext dbContext,
    IOptions<ReservationCleanupOptions> cleanupOptions,
    TimeProvider timeProvider) : IInventoryService
{
    public async Task<OperationResult> SetStockAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        if (quantity < 0)
        {
            return OperationResult.Failure("invalid_quantity", "Quantity must be non-negative.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var stock = await dbContext.StockItems.SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        if (stock is null)
        {
            stock = new StockItem
            {
                ProductId = productId,
                TotalQuantity = 0,
                AvailableQuantity = 0,
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };
            dbContext.StockItems.Add(stock);
        }

        var now = timeProvider.GetUtcNow();
        var totalReserved = await dbContext.ReservationItems
            .Where(x => x.ProductId == productId)
            .SumAsync(x => (int?)x.Quantity, cancellationToken) ?? 0;

        if (totalReserved > quantity)
        {
            var reservationCandidates = await dbContext.Reservations
                .AsNoTracking()
                .Where(r => r.Items.Any(i => i.ProductId == productId))
                .Select(r => new { r.ReservationId, r.CreatedAtUtc })
                .ToListAsync(cancellationToken);

            var reservationIdsToCancel = reservationCandidates
                .OrderByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.ReservationId)
                .Select(r => r.ReservationId)
                .ToList();

            foreach (var reservationId in reservationIdsToCancel)
            {
                if (totalReserved <= quantity)
                {
                    break;
                }

                var reservation = await dbContext.Reservations
                    .Include(r => r.Items)
                    .SingleAsync(r => r.ReservationId == reservationId, cancellationToken);

                foreach (var item in reservation.Items)
                {
                    var itemStock = await dbContext.StockItems.SingleOrDefaultAsync(x => x.ProductId == item.ProductId, cancellationToken);
                    if (itemStock is null)
                    {
                        continue;
                    }

                    itemStock.AvailableQuantity = Math.Min(itemStock.TotalQuantity, itemStock.AvailableQuantity + item.Quantity);
                    itemStock.UpdatedAtUtc = now;
                }

                totalReserved -= reservation.Items.Where(i => i.ProductId == productId).Sum(i => i.Quantity);
                dbContext.Reservations.Remove(reservation);

                AddOutboxMessage("reservation.removed", new
                {
                    ReservationId = reservation.ReservationId,
                    Reason = "auto_cancelled_by_stock_update",
                    RemovedAtUtc = now,
                    Items = reservation.Items.Select(i => new { i.ProductId, i.Quantity })
                }, now);
            }
        }

        stock.TotalQuantity = quantity;
        stock.AvailableQuantity = Math.Max(0, quantity - totalReserved);
        stock.UpdatedAtUtc = now;

        AddOutboxMessage("stock.set", new
        {
            ProductId = productId,
            Quantity = quantity,
            AvailableQuantity = stock.AvailableQuantity,
            OccurredAtUtc = now
        }, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ReserveAsync(Guid reservationId, IReadOnlyCollection<(Guid ProductId, int Quantity)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return OperationResult.Failure("empty_items", "Reservation must contain at least one item.");
        }

        if (items.Any(x => x.Quantity <= 0))
        {
            return OperationResult.Failure("invalid_quantity", "Reservation item quantity must be positive.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var existingReservation = await dbContext.Reservations.AnyAsync(x => x.ReservationId == reservationId, cancellationToken);
        if (existingReservation)
        {
            await transaction.CommitAsync(cancellationToken);
            return OperationResult.Success();
        }

        var normalizedItems = items
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        var productIds = normalizedItems.Select(x => x.ProductId).ToList();
        var stocks = await dbContext.StockItems
            .Where(x => productIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, cancellationToken);

        foreach (var item in normalizedItems)
        {
            if (!stocks.TryGetValue(item.ProductId, out var stock) || stock.AvailableQuantity < item.Quantity)
            {
                return OperationResult.Failure("not_enough_stock", $"Not enough available quantity for product {item.ProductId}.");
            }
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in normalizedItems)
        {
            var stock = stocks[item.ProductId];
            stock.AvailableQuantity -= item.Quantity;
            stock.UpdatedAtUtc = now;
        }

        var reservation = new Reservation
        {
            ReservationId = reservationId,
            CreatedAtUtc = now,
            Items = normalizedItems
                .Select(x => new ReservationItem
                {
                    ProductId = x.ProductId,
                    Quantity = x.Quantity
                })
                .ToList()
        };

        dbContext.Reservations.Add(reservation);

        AddOutboxMessage("reservation.added", new
        {
            ReservationId = reservationId,
            CreatedAtUtc = now,
            Items = normalizedItems
        }, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }

    public Task<OperationResult> CancelReservationAsync(Guid reservationId, string reason, CancellationToken cancellationToken)
        => RemoveReservationAsync(reservationId, updateStock: true, reason, cancellationToken);

    public Task<OperationResult> WriteOffReservationAsync(Guid reservationId, CancellationToken cancellationToken)
        => RemoveReservationAsync(reservationId, updateStock: false, "written_off", cancellationToken);

    public async Task<int?> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken)
    {
        var stock = await dbContext.StockItems
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProductId == productId, cancellationToken);

        return stock?.AvailableQuantity;
    }

    public async Task<int> CancelExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - cleanupOptions.Value.ReservationTtl;

        var reservationCandidates = await dbContext.Reservations
            .AsNoTracking()
            .Select(x => new { x.ReservationId, x.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var staleReservationIds = reservationCandidates
            .Where(x => x.CreatedAtUtc < cutoff)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.ReservationId)
            .Take(cleanupOptions.Value.BatchSize)
            .Select(x => x.ReservationId)
            .ToList();

        var cancelled = 0;
        foreach (var reservationId in staleReservationIds)
        {
            var result = await CancelReservationAsync(reservationId, "expired", cancellationToken);
            if (result.IsSuccess)
            {
                cancelled++;
            }
        }

        return cancelled;
    }

    private async Task<OperationResult> RemoveReservationAsync(Guid reservationId, bool updateStock, string reason, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var reservation = await dbContext.Reservations
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.ReservationId == reservationId, cancellationToken);

        if (reservation is null)
        {
            return OperationResult.Failure("reservation_not_found", "Reservation was not found.");
        }

        var now = timeProvider.GetUtcNow();

        if (updateStock)
        {
            foreach (var item in reservation.Items)
            {
                var stock = await dbContext.StockItems.SingleOrDefaultAsync(x => x.ProductId == item.ProductId, cancellationToken);
                if (stock is null)
                {
                    continue;
                }

                stock.AvailableQuantity = Math.Min(stock.TotalQuantity, stock.AvailableQuantity + item.Quantity);
                stock.UpdatedAtUtc = now;
            }
        }

        dbContext.Reservations.Remove(reservation);

        AddOutboxMessage("reservation.removed", new
        {
            ReservationId = reservation.ReservationId,
            Reason = reason,
            RemovedAtUtc = now,
            Items = reservation.Items.Select(i => new { i.ProductId, i.Quantity })
        }, now);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }

    private void AddOutboxMessage(string eventType, object payload, DateTimeOffset occurredAtUtc)
    {
        dbContext.OutboxMessages.Add(new OutboxMessage
        {
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = occurredAtUtc
        });
    }
}


