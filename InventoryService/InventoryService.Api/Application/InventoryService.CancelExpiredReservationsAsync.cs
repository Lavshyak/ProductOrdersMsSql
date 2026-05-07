using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Api.Application;

public partial class InventoryService
{
    public async Task CancelExpiredReservationsAsync(CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();
        var cutoffUtc = utcNow - cleanupOptions.Value.ReservationTtl;

        while (true) // rowsAffected != 0
        {
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
                //language=sql
                """
                -- noinspection SqlResolveForFile @ variable/"@batchSize"
                --DECLARE @cutoff AS DATETIMEOFFSET;
                --DECLARE @batchSize INT;
                ;DECLARE @reservationIdsAscMaybe AS dbo.GuidList;
                -- (1) читаю потенциально подходящие Reservations
                ;INSERT INTO @reservationIdsAscMaybe (Id)
                    SELECT TOP (@batchSize) r.Id
                    FROM Reservations r WITH (READPAST)
                    WHERE r.CreatedAtUtc < @cutoff
                    ORDER BY r.Id;

                -- (2) стараюсь заблокировать StockItems по Id ASC. Блокируется точно не меньше StockItems, чем нужно
                ;SELECT 1
                FROM StockItems si WITH (UPDLOCK)
                INNER JOIN (
                    SELECT DISTINCT(ri.ProductId)
                    FROM ReservationItems ri
                    INNER JOIN @reservationIdsAscMaybe rIds ON ri.ReservationId = rIds.Id
                ) pIds ON si.Id = pIds.ProductId
                ORDER BY si.Id;

                -- (3) читаю подходящие Reservations из множества @reservationIdsAscMaybe и стараюсь заблокировать по Id ASC
                ;DECLARE @reservationIdsAscLocked AS dbo.GuidList;
                ;INSERT INTO @reservationIdsAscLocked (Id)
                SELECT r.Id
                FROM Reservations r WITH (UPDLOCK)
                INNER JOIN @reservationIdsAscMaybe rIds ON r.Id = rIds.Id
                ORDER BY r.Id;

                -- (4) блокирую ReservationItems из @reservationIdsAscLocked, обновляю AvailableQuantity у StockItems
                ;UPDATE si
                SET AvailableQuantity = si.AvailableQuantity + ri.Quantity
                FROM StockItems si
                         INNER JOIN (SELECT ri.ProductId, SUM(Quantity) AS "Quantity"
                                     FROM dbo.ReservationItems ri WITH (UPDLOCK) -- ASC блокировку ReservationItems не стараюсь делать
                                     INNER JOIN @reservationIdsAscLocked rIds ON ri.ReservationId = rIds.Id
                                     GROUP BY ri.ProductId) ri ON si.Id = ri.ProductId;
                -- TODO: закинуть {{ProductId, AvailableQuantity}} в OutboxMessages

                -- TODO: переделать на добавление одной строки с много Id
                ;INSERT INTO OutboxMessages (EventType, OccurredAtUtc, PayloadJson)
                SELECT 'reservation.cancelled.expired'          AS EventType,
                       GETDATE()                              AS OccurredAtUtc,
                       (SELECT rIds.Id AS "ReservationId"
                        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS PayloadJson
                FROM @reservationIdsAscLocked rIds;    
                    
                ;DELETE ri FROM dbo.ReservationItems ri
                INNER JOIN @reservationIdsAscLocked rIds ON ri.ReservationId = rIds.Id;

                ;DELETE r FROM dbo.Reservations r
                INNER JOIN @reservationIdsAscLocked rIds ON r.Id = rIds.Id;

                
                """,
                [
                    new SqlParameter("@cutoff", cutoffUtc),
                    new SqlParameter("@batchSize", cleanupOptions.Value.BatchSize)
                ],
                cancellationToken
            );
            
            //Debug.WriteLine($"CancelExpiredReservationsAsync: rowsAffected = {rowsAffected}");

            if (rowsAffected == 0)
                break;

            await transaction.CommitAsync(cancellationToken);
        }
    }
}