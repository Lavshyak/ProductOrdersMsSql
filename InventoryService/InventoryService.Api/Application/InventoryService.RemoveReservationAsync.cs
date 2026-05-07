using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace InventoryService.Api.Application;

public partial class InventoryService
{
    private enum WriteOffOrCancel
    {
        WriteOff,
        Cancel
    }
    private async Task<OperationResult> RemoveReservationAsync(Guid reservationId, WriteOffOrCancel writeOffOrCancel,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var reservationIdMSS = reservationId.SwapV7ToMSS();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            SELECT 1
            FROM StockItems si WITH (UPDLOCK)
            WHERE si.Id = @reservationId
            ORDER BY si.Id
            """,
            [
                new SqlParameter("@reservationId", reservationIdMSS)
            ],
            cancellationToken);
        
        if (writeOffOrCancel == WriteOffOrCancel.Cancel)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE si
                SET AvailableQuantity = si.AvailableQuantity + ri.Quantity
                FROM StockItems si
                INNER JOIN (
                    SELECT * 
                    FROM ReservationItems ri
                    WHERE ri.ReservationId = @reservationId
                ) ri ON ri.ProductId = si.Id
                """,
                [
                    new SqlParameter("@reservationId", reservationIdMSS)
                ],
                cancellationToken);
        }
        else if(writeOffOrCancel == WriteOffOrCancel.WriteOff)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE si
                SET TotalQuantity = si.TotalQuantity - ri.Quantity
                FROM StockItems si
                INNER JOIN (
                    SELECT * 
                    FROM ReservationItems ri
                    WHERE ri.ReservationId = @reservationId
                ) ri ON ri.ProductId = si.Id
                """,
                [
                    new SqlParameter("@reservationId", reservationIdMSS)
                ],
                cancellationToken);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(writeOffOrCancel), writeOffOrCancel, null);
        }
        
        // далее должно надежно работать, но лень нормально переписывать
        var exists = await dbContext.Database.SqlQueryRaw<int>(
            // language=sql
            """
            SELECT 1 AS "Value"
            FROM ReservationItems ri WITH (UPDLOCK)
            WHERE ri.ReservationId = @reservationId
            """,
            [
                new SqlParameter("@reservationId", reservationIdMSS)
            ]).FirstOrDefaultAsync(cancellationToken);
        if (exists != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Success();
        }
        
        var deletedReservationItemsCount = await dbContext.ReservationItems.Where(ri => ri.ReservationId == reservationIdMSS)
            .OrderBy(ri => ri.ReservationId)
            .ExecuteDeleteAsync(cancellationToken);
        if (deletedReservationItemsCount == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationResult.Success();
        }

        await dbContext.Reservations.Where(r => r.Id == reservationIdMSS)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return OperationResult.Success();
    }
}