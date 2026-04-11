using System.Data;
using InventoryService.Api.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace InventoryService.Api.Application;

public partial class InventoryService
{
    // TODO: перепроверить, не актуально для текущих названий колонок
    public async Task<OperationResult> ReserveAsync(Guid reservationId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return OperationResult.Failure("empty_items", "Reservation must contain at least one item.");
        }

        if (items.Any(x => x.Quantity <= 0))
        {
            return OperationResult.Failure("invalid_quantity", "Reservation item quantity must be positive.");
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var reservationIdMSS = reservationId.SwapV7ToMSS();

        var isReservationExists =
            await dbContext.Reservations.AnyAsync(x => x.Id == reservationIdMSS,
                cancellationToken);
        if (isReservationExists) // TODO: сравнить ReservationItems, если одинаково то Success()
        {
            return OperationResult.Failure("reservation_exists", "Reservation with the same ID already exists.");
        }

        var normalizedItems = items
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductIdMSS = g.Key.SwapV7ToMSS(), Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        var utcNow = timeProvider.GetUtcNow();

        {
            var valuesSql = string.Join(",",
                normalizedItems.Select((_, i) =>
                    $"(@ProductId{i}, @Quantity{i})"));

            var parameters = new List<object>();

            int idx = 0;
            foreach (var item in normalizedItems)
            {
                parameters.Add(new SqlParameter($"@ProductId{idx}", item.ProductIdMSS));
                parameters.Add(new SqlParameter($"@Quantity{idx}", item.Quantity));
                idx++;
            }

            // может часто лочиться не в ASC порядке?, но не критично
#pragma warning disable EF1002
            var updatedCount = await dbContext.Database.ExecuteSqlRawAsync(
#pragma warning restore EF1002
                $"""
                 UPDATE si
                 SET AvailableQuantity = si.AvailableQuantity - v.Quantity
                 FROM StockItems si
                 INNER JOIN (SELECT *
                             FROM (VALUES {valuesSql}) v(ProductId, Quantity)) v
                            ON si.Id = v.ProductId
                 WHERE si.AvailableQuantity >= v.Quantity
                 """,
                parameters,
                cancellationToken);

            if (updatedCount != normalizedItems.Count)
            {
                return OperationResult.Failure("not_enough_stock",
                    "Not enough available quantity for one or more products.");
            }
        }

        var reservation = new Reservation
        {
            Id = reservationIdMSS,
            CreatedAtUtc = utcNow,
            Items = normalizedItems
                .Select(x => new ReservationItem
                {
                    ProductId = x.ProductIdMSS,
                    Quantity = x.Quantity
                })
                .ToList()
        };

        dbContext.Reservations.Add(reservation);

        // TODO: обработать случай, когда уже добавлена Reservation с этим ReservationId
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }
}