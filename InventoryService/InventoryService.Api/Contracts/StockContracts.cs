namespace InventoryService.Api.Contracts;

public sealed record SetStockRequest(Guid ProductId, int Quantity);

public sealed record GetStockResponse(Guid ProductId, int AvailableQuantity);

