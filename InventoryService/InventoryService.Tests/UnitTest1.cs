using InventoryService.Api.Application;
using InventoryService.Api.Domain;
using InventoryService.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InventoryService.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task Reserve_IsIdempotent_ByReservationId()
    {
        await using var db = CreateDbContext();
        var productId = Guid.NewGuid();
        db.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalQuantity = 10,
            AvailableQuantity = 10,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var reservationId = Guid.NewGuid();

        var first = await service.ReserveAsync(reservationId, [(productId, 3)], CancellationToken.None);
        var second = await service.ReserveAsync(reservationId, [(productId, 2)], CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        var stock = await db.StockItems.SingleAsync(x => x.ProductId == productId);
        Assert.Equal(7, stock.AvailableQuantity);

        var reservationCount = await db.Reservations.CountAsync();
        Assert.Equal(1, reservationCount);
    }

    [Fact]
    public async Task SetStock_CancelsLatestReservations_WhenReservedExceedsNewLimit()
    {
        await using var db = CreateDbContext();
        var productId = Guid.NewGuid();
        db.StockItems.Add(new StockItem
        {
            ProductId = productId,
            TotalQuantity = 10,
            AvailableQuantity = 10,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var firstReservation = Guid.NewGuid();
        var secondReservation = Guid.NewGuid();

        await service.ReserveAsync(firstReservation, [(productId, 4)], CancellationToken.None);
        await Task.Delay(10);
        await service.ReserveAsync(secondReservation, [(productId, 3)], CancellationToken.None);

        var setStock = await service.SetStockAsync(productId, 5, CancellationToken.None);
        Assert.True(setStock.IsSuccess);

        var existingReservationIds = await db.Reservations.Select(x => x.ReservationId).ToListAsync();
        Assert.Contains(firstReservation, existingReservationIds);
        Assert.DoesNotContain(secondReservation, existingReservationIds);

        var stock = await db.StockItems.SingleAsync(x => x.ProductId == productId);
        Assert.Equal(1, stock.AvailableQuantity);
        Assert.Equal(5, stock.TotalQuantity);
    }

    private static InventoryDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new InventoryDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static InventoryService.Api.Application.InventoryService CreateService(InventoryDbContext db)
    {
        var options = Options.Create(new ReservationCleanupOptions());
        return new InventoryService.Api.Application.InventoryService(db, options, TimeProvider.System);
    }
}
