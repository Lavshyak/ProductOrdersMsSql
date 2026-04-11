namespace InventoryService.Api.Domain;

public sealed class Reservation
{
    public Guid ReservationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<ReservationItem> Items { get; set; } = new List<ReservationItem>();
}

