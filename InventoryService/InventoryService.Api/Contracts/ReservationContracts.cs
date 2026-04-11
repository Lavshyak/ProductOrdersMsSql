namespace InventoryService.Api.Contracts;

public sealed record ReserveRequest(Guid ReservationId, IReadOnlyCollection<ReserveItemRequest> Items);

public sealed record ReserveItemRequest(Guid ProductId, int Quantity);

public sealed record ReservationActionRequest(Guid ReservationId);

