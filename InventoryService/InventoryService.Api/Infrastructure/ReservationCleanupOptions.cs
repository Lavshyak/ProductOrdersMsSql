namespace InventoryService.Api.Infrastructure;

public sealed class ReservationCleanupOptions
{
    public const string SectionName = "ReservationCleanup";

    public TimeSpan ReservationTtl { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; set; } = 100;
}

