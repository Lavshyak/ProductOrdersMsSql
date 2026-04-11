using InventoryService.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Api.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.HasKey(x => x.ProductId);
            entity.Property(x => x.TotalQuantity).IsRequired();
            entity.Property(x => x.AvailableQuantity).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(x => x.ReservationId);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Reservation)
                .HasForeignKey(x => x.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReservationItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Quantity).IsRequired();
            entity.HasIndex(x => new { x.ReservationId, x.ProductId }).IsUnique();
            entity.HasIndex(x => x.ProductId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.HasIndex(x => x.OccurredAtUtc);
        });
    }
}

