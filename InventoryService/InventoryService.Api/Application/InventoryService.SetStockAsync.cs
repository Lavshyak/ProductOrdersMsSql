using System.Data;
using InventoryService.Api.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace InventoryService.Api.Application;

public partial class InventoryService
{
    public async Task<OperationResult> SetStockAsync(Guid productId, int newTotalQuantity,
        CancellationToken cancellationToken)
    {
        var productIdMSS = productId.SwapV7ToMSS();

        if (newTotalQuantity < 0)
        {
            return OperationResult.Failure("invalid_quantity", "Quantity must be non-negative.");
        }

        int? totalQuantityVersion = null;
        while (true)
        {
            dbContext.ChangeTracker.Clear(); // на всякий случай

            var existingStockMaybe =
                dbContext.StockItems.AsNoTracking().FirstOrDefault(x => x.Id == productIdMSS);

            totalQuantityVersion ??= existingStockMaybe?.TotalQuantityVersionForSetStock;
            
            if (totalQuantityVersion != existingStockMaybe?.TotalQuantityVersionForSetStock)
            {
                // кто-то уже обновил totalQuantity
                // тоже самое, как если бы этот запрос установил количество, а другой перезаписал чуть позже
                return OperationResult.Success();
            }

            totalQuantityVersion ??= existingStockMaybe?.TotalQuantityVersionForSetStock;

            // попытка добавления
            if (existingStockMaybe is null)
            {
                var stockToAdd = new StockItem
                {
                    Id = productIdMSS,
                    TotalQuantity = newTotalQuantity,
                    AvailableQuantity = newTotalQuantity,
                };
                dbContext.StockItems.Add(stockToAdd);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx &&
                                                   (sqlEx.Number == 2627 || sqlEx.Number == 2601))
                {
                    // запись существует
                    // тоже самое, как если бы этот запрос установил количество, а другой перезаписал чуть позже
                    return OperationResult.Success();
                }

                return OperationResult.Success();
            }

            if (totalQuantityVersion == null)
            {
                throw new InvalidOperationException("Баг");
            }

            var deltaQuantityMayBe = newTotalQuantity - existingStockMaybe.TotalQuantity;
            var newAvailableQuantityMaybe = existingStockMaybe.AvailableQuantity + deltaQuantityMayBe;
            if (newAvailableQuantityMaybe >= 0)
            {
                // обновление
                var totalQuantityVersionTmp = totalQuantityVersion.Value;
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

                var stockItemTracked = await dbContext.StockItems.FromSqlRaw(
                    // language=sql
                    """
                    SELECT * FROM StockItems si WITH (UPDLOCK)
                    WHERE si.Id = @productId AND si.TotalQuantityVersionForSetStock = @totalQuantityVersion
                    """,
                    new SqlParameter("@productId", productIdMSS),
                    new SqlParameter("@totalQuantityVersion", totalQuantityVersionTmp)
                ).FirstOrDefaultAsync(cancellationToken);

                if (stockItemTracked is null) // изменена TotalQuantityVersion или удален
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return OperationResult.Success();
                }

                var deltaQuantity = newTotalQuantity - stockItemTracked.TotalQuantity;
                var newAvailableQuantity = stockItemTracked.AvailableQuantity + deltaQuantity;
                if (newAvailableQuantity >= 0)
                {
                    stockItemTracked.TotalQuantity = newTotalQuantity;
                    stockItemTracked.TotalQuantityVersionForSetStock++;
                    stockItemTracked.AvailableQuantity = newAvailableQuantity;

                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return OperationResult.Success();
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    dbContext.ChangeTracker.Clear();
                    // отмена новых резервирований, чтобы увеличить AvailableQuantity
                    var availableQuantityShortageMaybe = -newAvailableQuantity;
                    await CancelReservationsToIncreaseAvailableStockQuantityInNewTx(productIdMSS,
                        availableQuantityShortageMaybe, cancellationToken);
                }
            }
            else
            {
                // отмена новых резервирований, чтобы увеличить AvailableQuantity
                var availableQuantityShortageMaybe = -newAvailableQuantityMaybe;
                await CancelReservationsToIncreaseAvailableStockQuantityInNewTx(productIdMSS,
                    availableQuantityShortageMaybe, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Батчем отменяет последние резервирования
    /// </summary>
    /// <param name="productIdMSS"></param>
    /// <param name="availableQuantityShortage">сколько не хватает. может не учитываться.</param>
    /// <param name="cancellationToken"></param>
    private async Task CancelReservationsToIncreaseAvailableStockQuantityInNewTx(Guid productIdMSS,
        int availableQuantityShortage, CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // в целом то же, что в CancelExpiredReservationsAsync,
        // только в @reservationIdsAscMaybe селектится другое и 'reservation.notenoughstock.deleted' AS EventType
        var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
            //language=sql
            """
            -- DECLARE @productId AS UNIQUEIDENTIFIER;
            DECLARE @reservationIdsAscMaybe AS dbo.GuidList;
            -- (1) читаю потенциально подходящие Reservations
            -- Вместо TOP 50 можно например min(от чего субд не задохнется, availableQuantityShortage / среднее quantity в заказе + небольшой запас)
            INSERT INTO @reservationIdsAscMaybe (Id)
            SELECT sub.ReservationId
            FROM (SELECT TOP 50 ri.ReservationId
                  FROM ReservationItems ri WITH (NOLOCK)
                  WHERE ri.ProductId = @productId
                  ORDER BY ri.ReservationId DESC -- новые
                 ) sub
            ORDER BY sub.ReservationId;

            -- (2) стараюсь заблокировать StockItems по Id ASC. Блокируется точно не меньше StockItems, чем нужно
            SELECT 1
            FROM StockItems si WITH (UPDLOCK)
            INNER JOIN (
                SELECT DISTINCT(ri.ProductId)
                FROM ReservationItems ri
                INNER JOIN @reservationIdsAscMaybe rIds ON ri.ReservationId = rIds.Id
            ) pIds ON si.Id = pIds.ProductId
            ORDER BY si.Id;

            -- (3) читаю подходящие Reservations из множества @reservationIdsAscMaybe и стараюсь заблокировать по Id ASC
            DECLARE @reservationIdsAscLocked AS dbo.GuidList;
            INSERT INTO @reservationIdsAscLocked (Id)
            SELECT r.Id
            FROM Reservations r WITH (UPDLOCK)
            INNER JOIN @reservationIdsAscMaybe rIds ON r.Id = rIds.Id
            ORDER BY r.Id;

            -- (4) блокирую ReservationItems из @reservationIdsAscLocked, обновляю AvailableQuantity у StockItems
            UPDATE si
            SET AvailableQuantity = si.AvailableQuantity + ri.Quantity
            FROM StockItems si
                     INNER JOIN (SELECT ri.ProductId, SUM(Quantity) AS "Quantity"
                                 FROM dbo.ReservationItems ri WITH (UPDLOCK) -- ASC блокировку ReservationItems не стараюсь делать
                                 INNER JOIN @reservationIdsAscLocked rIds ON ri.ReservationId = rIds.Id
                                 GROUP BY ri.ProductId) ri ON si.Id = ri.ProductId;
            -- TODO: закинуть {{ProductId, AvailableQuantity}} в OutboxMessages для обновления кэша?

            DELETE ri FROM dbo.ReservationItems ri
            INNER JOIN @reservationIdsAscLocked rIds ON ri.ReservationId = rIds.Id;

            DELETE r FROM dbo.Reservations r
            INNER JOIN @reservationIdsAscLocked rIds ON r.Id = rIds.Id;

            -- TODO: переделать на добавление одной строки с много Id?
            INSERT INTO OutboxMessages (EventType, OccurredAtUtc, PayloadJson)
            SELECT 'reservation.cancelled.notenoughstock'   AS EventType,
                   GETDATE()                              AS OccurredAtUtc,
                   (SELECT rIds.Id AS "ReservationId"
                    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS PayloadJson
            FROM @reservationIdsAscLocked rIds;
            """,
            [
                new SqlParameter("@productId", productIdMSS)
            ],
            cancellationToken
        );

        if (rowsAffected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}