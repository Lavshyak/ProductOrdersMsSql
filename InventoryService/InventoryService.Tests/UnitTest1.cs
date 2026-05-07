using InventoryService.Api.Application;
using InventoryService.Api.Domain;
using InventoryService.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Shared;
using Xunit.Abstractions;

namespace InventoryService.Tests;

[Collection("Sequential")]
public sealed class InventoryServiceTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InventoryServiceTests(SqlServerDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /*[Fact]
    public async Task InsertManyNotTest()
    {
        await InsertManyRandom(iterations: 1000, multiplayer: 100, true);
    }*/

    /// <summary>
    /// 
    /// </summary>
    /// <param name="iterations"></param>
    /// <param name="multiplayer">=stock items count, =reservations count, меньше или = multiplayer*(multiplayer/10) reservationItemsCount</param>
    private async Task InsertManyRandom(int iterations = 1, int multiplayer = 20, bool resetDatabase = false)
    {
        if (resetDatabase)
        {
            await _fixture.ResetDatabaseAsync();
        }

        await using var db = CreateDbContext();

        for (int i = 0; i < iterations; i++)
        {
            _output.WriteLine($"i: {i}");
            var tx = await db.Database.BeginTransactionAsync();
            var sis = Enumerable.Range(0, multiplayer).Select(_ =>
            {
                var tq = Random.Shared.Next(1, multiplayer);
                return new StockItem
                {
                    Id = Guid.CreateVersion7().SwapV7ToMSS(),
                    TotalQuantity = tq,
                    AvailableQuantity = tq,
                };
            }).ToList();

            var rs = Enumerable.Range(0, multiplayer).Select(_ =>
            {
                var rId = Guid.CreateVersion7().SwapV7ToMSS();
                var r = new Reservation()
                {
                    Id = rId,
                    CreatedAtUtc = DateTime.UtcNow,
                    Items = Enumerable.Range(0, Random.Shared.Next(1, multiplayer / 10)).Select(_ =>
                    {
                        var si = sis[Random.Shared.Next(0, sis.Count - 1)];
                        var q = Random.Shared.Next(0, si.AvailableQuantity);
                        si.AvailableQuantity -= q;
                        var ri = new ReservationItem()
                        {
                            ProductId = si.Id,
                            Quantity = q,
                            ReservationId = rId
                        };
                        return ri;
                    }).GroupBy(ri => ri.ProductId).Select(riG =>
                    {
                        return new ReservationItem()
                        {
                            ProductId = riG.Key,
                            Quantity = riG.Sum(x => x.Quantity),
                            ReservationId = rId
                        };
                    }).Where(ri => ri.Quantity != 0).ToList(),
                };
                return r;
            });

            db.StockItems.AddRange(sis);
            db.Reservations.AddRange(rs);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task CantReserveAgain()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = CreateDbContext();
        var productId = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId.SwapV7ToMSS(),
            TotalQuantity = 10,
            AvailableQuantity = 10,
        });
        await db.SaveChangesAsync();

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb);
        var reservationId = Guid.CreateVersion7();

        var first = await service.ReserveAsync(reservationId, [(productId, 3)], CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await service.ReserveAsync(reservationId, [(productId, 2)], CancellationToken.None);
        Assert.False(second.IsSuccess);

        db.ChangeTracker.Clear();
        var stock = await db.StockItems.SingleAsync(x => x.Id == productId.SwapV7ToMSS());
        Assert.Equal(7, stock.AvailableQuantity);

        var reservationCount = await db.Reservations
            .Where(r => r.Items.Any(ri => ri.ProductId == productId.SwapV7ToMSS())).CountAsync();
        Assert.Equal(1, reservationCount);
    }

    [Fact]
    public async Task ReserveManyWorks()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = CreateDbContext();
        var productId1 = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId1.SwapV7ToMSS(),
            TotalQuantity = 10,
            AvailableQuantity = 10,
        });
        var productId2 = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId2.SwapV7ToMSS(),
            TotalQuantity = 20,
            AvailableQuantity = 20,
        });
        await db.SaveChangesAsync();

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb);
        var reservationId = Guid.CreateVersion7();

        var result =
            await service.ReserveAsync(reservationId, [(productId1, 10), (productId2, 20)], CancellationToken.None);
        Assert.True(result.IsSuccess);

        db.ChangeTracker.Clear();
        var stock1 = await db.StockItems.SingleAsync(x => x.Id == productId1.SwapV7ToMSS());
        Assert.Equal(0, stock1.AvailableQuantity);
        Assert.Equal(10, stock1.TotalQuantity);

        var stock2 = await db.StockItems.SingleAsync(x => x.Id == productId2.SwapV7ToMSS());
        Assert.Equal(0, stock2.AvailableQuantity);
        Assert.Equal(20, stock2.TotalQuantity);

        var reservationCount = await db.Reservations.Where(r => r.Id == reservationId.SwapV7ToMSS()).CountAsync();
        Assert.Equal(1, reservationCount);
        var reservationItemsCount =
            await db.ReservationItems.Where(ri => ri.ReservationId == reservationId.SwapV7ToMSS()).CountAsync();
        Assert.Equal(2, reservationItemsCount);
    }

    [Fact]
    public async Task CancelExpiredReservationsWorks()
    {
        await _fixture.ResetDatabaseAsync();

        await InsertManyRandom(1, 20);
        await using var dbOldState = CreateDbContext();
        await dbOldState.StockItems.LoadAsync();
        await dbOldState.Reservations.LoadAsync();
        await dbOldState.ReservationItems.LoadAsync();

        await Task.Delay(3000);

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb, Options.Create(new ReservationCleanupOptions()
        {
            ReservationTtl = TimeSpan.FromMilliseconds(3000),
        }));

        await InsertManyRandom(1, 20); // эти не должны быть затронуты, если тест быстро пройдет
        await using var dbNewState = CreateDbContext();
        await dbNewState.StockItems.LoadAsync();
        await dbNewState.Reservations.LoadAsync();
        await dbNewState.ReservationItems.LoadAsync();

        await service.CancelExpiredReservationsAsync(CancellationToken.None);
        await using var dbActualState = CreateDbContext();
        await dbActualState.StockItems.LoadAsync();
        await dbActualState.Reservations.LoadAsync();
        await dbActualState.ReservationItems.LoadAsync();

        var actualReservations = dbActualState.Reservations.Local;
        var newReservations = dbNewState.Reservations.Local;
        var oldReservations = dbOldState.Reservations.Local;

        // Все AvailableQuantity старых товаров = TotalQuantity
        var siOldActual =
            dbActualState.StockItems.Local.Where(si => dbOldState.StockItems.Local.Any(siOld => siOld.Id == si.Id))
                .ToArray();
        Assert.True(siOldActual.All(si => si.AvailableQuantity == si.TotalQuantity));

        // удалены старые Reservations
        var rOldActual = actualReservations
            .Where(r => oldReservations.Any(rOld => rOld.Id == r.Id)).ToArray();
        Assert.Empty(rOldActual);

        // остались все новые Reservations
        // ReSharper disable once ReplaceWithSingleCallToCount
        Assert.Equal(actualReservations.Count,
            actualReservations.Where(rActual =>
                newReservations.Any(rNew =>
                    rActual.Id == rNew.Id && oldReservations.All(rOld => rOld.Id != rNew.Id)
                )
            ).Count());
    }

    [Fact]
    public async Task SetStock_CancelsLatestReservations_WhenReservedExceedsNewLimit()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = CreateDbContext();
        var productId = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId.SwapV7ToMSS(),
            TotalQuantity = 100,
            AvailableQuantity = 100,
        });
        await db.SaveChangesAsync();

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb);
        // старые -> новые
        var reservationIdsAsc = Enumerable.Range(0, 100).Select(_ => Guid.CreateVersion7())
            .Order( /*какое-то слишком случайное время получается*/).ToArray();
        for (int i = 0; i < 100; i++)
        {
            await service.ReserveAsync(reservationIdsAsc[i], [(productId, 1)], CancellationToken.None);
        }

        /*_output.WriteLine("initialExpected:\n" + string.Join("\n", reservationIdsAsc.Select(x => x.SwapV7ToMSS())) +
                          "\n");
        _output.WriteLine("initialActual:\n" +
                          string.Join("\n", await db.Reservations.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync()) +
                          "\n");
                          */

        var setStock = await service.SetStockAsync(productId, 50, CancellationToken.None);
        Assert.True(setStock.IsSuccess);

        db.ChangeTracker.Clear();

        var si = await db.StockItems.SingleAsync(x => x.Id == productId.SwapV7ToMSS());
        Assert.Equal(50, si.TotalQuantity);
        Assert.True(si.AvailableQuantity <= 50);

        var rCount = await db.Reservations.CountAsync();
        Assert.True(rCount <= 50);
        Assert.Equal(50 - si.AvailableQuantity, rCount);

        // rCount старых
        var reservationIdsMSSAscShouldExist = reservationIdsAsc.Take(rCount).Select(x => x.SwapV7ToMSS()).ToArray();
        /*_output.WriteLine("shouldExistExpected:\n" + string.Join("\n", reservationIdsMSSAscShouldExist) + "\n");
        _output.WriteLine("existActual:\n" +
                          string.Join("\n", await db.Reservations.OrderBy(r => r.Id).Select(r => r.Id).ToArrayAsync()) +
                          "\n");*/
        Assert.True(await db.Reservations.AllAsync(r =>
            ((IEnumerable<Guid>)reservationIdsMSSAscShouldExist).Contains(r.Id)));
    }

    [Fact]
    public async Task AvailableAndTotalQuantityIsCorrectWhenWriteOffThenSetStockToIncreasedQuantity()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = CreateDbContext();
        var productId = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId.SwapV7ToMSS(),
            TotalQuantity = 10,
            AvailableQuantity = 10,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb);

        var reservationId = Guid.CreateVersion7();
        Assert.True((await service.ReserveAsync(reservationId, [(productId, 5)], CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 5);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 10);
        Assert.True((await service.WriteOffReservationAsync(reservationId, CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 5);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 5);
        Assert.True((await service.SetStockAsync(productId, 10, CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 10);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 10);
    }
    
    [Fact]
    public async Task AvailableAndTotalQuantityIsCorrectWhenCancelThenSetStockToIncreasedQuantity()
    {
        await _fixture.ResetDatabaseAsync();
        await using var db = CreateDbContext();
        var productId = Guid.CreateVersion7();
        db.StockItems.Add(new StockItem
        {
            Id = productId.SwapV7ToMSS(),
            TotalQuantity = 10,
            AvailableQuantity = 10,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var serviceDb = CreateDbContext();
        var service = CreateService(serviceDb);

        var reservationId = Guid.CreateVersion7();
        Assert.True((await service.ReserveAsync(reservationId, [(productId, 5)], CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 5);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 10);
        Assert.True((await service.CancelReservationAsync(reservationId, CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 10);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 10);
        Assert.True((await service.SetStockAsync(productId, 20, CancellationToken.None)).IsSuccess);
        serviceDb.ChangeTracker.Clear();
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).AvailableQuantity == 20);
        Assert.True((await db.StockItems.AsNoTracking().SingleAsync()).TotalQuantity == 20);
    }

    private InventoryDbContext CreateDbContext()
    {
        return _fixture.CreateDbContext();
        /*var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new InventoryDbContext(options);
        db.Database.EnsureCreated();
        return db;*/
    }

    private InventoryService.Api.Application.InventoryService CreateService(InventoryDbContext db,
        IOptions<ReservationCleanupOptions>? options = null)
    {
        options ??= Options.Create(new ReservationCleanupOptions());
        return new InventoryService.Api.Application.InventoryService(db, options, TimeProvider.System);
    }
}