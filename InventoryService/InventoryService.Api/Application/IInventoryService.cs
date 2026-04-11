namespace InventoryService.Api.Application;

public interface IInventoryService
{
    Task<OperationResult> SetStockAsync(Guid productId, int quantity, CancellationToken cancellationToken);

    Task<OperationResult> ReserveAsync(Guid reservationId, IReadOnlyCollection<(Guid ProductId, int Quantity)> items, CancellationToken cancellationToken);

    Task<OperationResult> CancelReservationAsync(Guid reservationId, string reason, CancellationToken cancellationToken);

    Task<OperationResult> WriteOffReservationAsync(Guid reservationId, CancellationToken cancellationToken);

    Task<int?> GetAvailableQuantityAsync(Guid productId, CancellationToken cancellationToken);

    Task<int> CancelExpiredReservationsAsync(CancellationToken cancellationToken);
}

