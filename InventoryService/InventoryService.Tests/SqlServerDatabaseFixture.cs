using System.Diagnostics;
using DotNet.Testcontainers.Configurations;
using InventoryService.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace InventoryService.Tests;

public sealed class SqlServerDatabaseFixture : IAsyncLifetime, IDisposable
{
    public TestLogCollector Logs { get; } = new();

    private readonly ILoggerFactory _loggerFactory;
    private MsSqlContainer? _dbContainer;
    private string? _connectionString;

    public SqlServerDatabaseFixture()
    {
        _loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddProvider(new TestLogCollectorProvider(Logs));
        });
    }

    private readonly bool _skipStartContainer = true;

    public async Task InitializeAsync()
    {
        if (!_skipStartContainer)
        {
            _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithName("InventoryDbTest")
                .WithAutoRemove(false)
                .WithCleanUp(false)
                .WithPortBinding(5984, 1433)
                .WithPassword("YourStrong!Passw0rd")
                .Build();

            await _dbContainer.StartAsync();
        }
        
        //_connectionString = _dbContainer.GetConnectionString();
        _connectionString =
            "Server=localhost,5984;Database=InventoryServiceDb;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True;";

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContainer != null)
        {
            await _dbContainer.StopAsync();
            await _dbContainer.DisposeAsync();
        }
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
    }

    public InventoryDbContext CreateDbContext()
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .EnableDetailedErrors(true)
            .EnableSensitiveDataLogging(true)
            .LogTo(str => Debug.WriteLine(str), LogLevel.Debug)
            .UseSqlServer(_connectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    public ILogger<T> CreateLogger<T>() => _loggerFactory.CreateLogger<T>();

    public async Task ResetDatabaseAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.OutboxMessages.ExecuteDeleteAsync();
        await dbContext.ReservationItems.ExecuteDeleteAsync();
        await dbContext.Reservations.ExecuteDeleteAsync();
        await dbContext.StockItems.ExecuteDeleteAsync();
    }
}