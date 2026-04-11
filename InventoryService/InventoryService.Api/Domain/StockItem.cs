namespace InventoryService.Api.Domain;

public sealed class StockItem
{
    public Guid ProductId { get; set; }

    public int TotalQuantity { get; set; }

    public int AvailableQuantity { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

