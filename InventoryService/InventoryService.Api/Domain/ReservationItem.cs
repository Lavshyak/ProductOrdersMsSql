namespace InventoryService.Api.Domain;

public sealed class ReservationItem
{
    public Guid ReservationId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public Reservation Reservation { get; set; } = null!;
}

