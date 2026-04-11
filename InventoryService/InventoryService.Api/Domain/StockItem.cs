namespace InventoryService.Api.Domain;

public sealed class StockItem
{
    public Guid Id { get; set; }

    public int TotalQuantity { get; set; }
    public int TotalQuantityVersion { get; set; }

    public int AvailableQuantity { get; set; }
}

