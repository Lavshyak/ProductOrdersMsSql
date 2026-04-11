using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shared;

namespace InventoryService.Api.Application;

public partial class InventoryService
{
    private async Task<OperationResult> RemoveReservationAsync(Guid reservationId, bool updateStock,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var reservationIdMSS = reservationId.SwapV7ToMSS();

        if (updateStock)
        {
            // TODO: повтор на дедлоке
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE si
                SET AvailableQuantity = si.AvailableQuantity + ri.Quantity
                FROM StockItems si
                INNER JOIN (
                    SELECT * 
                    FROM ReservationItems ri
                    WHERE ri.ReservationId = @reservationId
                    ORDER BY ri.ProductId
                ) ri ON ri.ProductId = si.Id
                """,
                [
                    new SqlParameter("@reservationId", reservationIdMSS)
                ],
                cancellationToken);
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