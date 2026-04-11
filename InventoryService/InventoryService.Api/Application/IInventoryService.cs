namespace InventoryService.Api.Application;

public interface IInventoryService
{
    /// <summary>
    /// если зарезервировано больше товара, чем newTotalQuantity, то могут быть отменены резервирования, на которые товара хватает
    /// </summary>
    Task<OperationResult> SetStockAsync(Guid productId, int newTotalQuantity, CancellationToken cancellationToken);

    Task<OperationResult> ReserveAsync(Guid reservationId, IReadOnlyCollection<(Guid ProductId, int Quantity)> items, CancellationToken cancellationToken);

    Task<OperationResult> CancelReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<OperationResult> WriteOffReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<int?> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken);

    Task CancelExpiredReservationsAsync(CancellationToken cancellationToken);
}

