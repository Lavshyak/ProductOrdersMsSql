using InventoryService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shared;

namespace InventoryService.Api.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> StockItems => Set<StockItem>();

    // update не предполагается
    public DbSet<Reservation> Reservations => Set<Reservation>();

    // update не предполагается
    public DbSet<ReservationItem> ReservationItems => Set<ReservationItem>();

    // update не предполагается
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        /*var guidV7SwapperValueConverter = new ValueConverter<Guid, Guid>(
            toDb => toDb.SwapV7ToMsSqlServer(),
            fromDb => fromDb.SwapV7FromMsSqlServer()
        );*/

        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TotalQuantity).IsRequired();
            entity.Property(x => x.AvailableQuantity).IsRequired();

            entity.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_StockItem_TotalQuantity_GE_Zero", "[TotalQuantity] >= 0");
                tb.HasCheckConstraint("CK_StockItem_AvailableQuantity_GE_Zero", "[AvailableQuantity] >= 0");
                tb.HasCheckConstraint(
                    "CK_StockItem_AvailableQuantity_LE_TotalQuantity", "[AvailableQuantity] <= [TotalQuantity]");
            });
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Reservation)
                .HasForeignKey(x => x.ReservationId);
        });

        modelBuilder.Entity<ReservationItem>(entity =>
        {
            entity.HasKey(x => new { x.ReservationId, x.ProductId });
            entity.Property(x => x.Quantity).IsRequired();
            entity.HasIndex(x => x.ProductId);
            
            entity.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_StockItem_Quantity_GE_Zero", "[Quantity] >= 0");
            });
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