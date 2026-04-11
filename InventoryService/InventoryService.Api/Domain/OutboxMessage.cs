namespace InventoryService.Api.Domain;

public sealed class OutboxMessage
{
    public long Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }
}

