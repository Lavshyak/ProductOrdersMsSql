using InventoryService.Api.Application;
using InventoryService.Api.Contracts;
using InventoryService.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<ReservationCleanupOptions>(
    builder.Configuration.GetSection(ReservationCleanupOptions.SectionName));

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IInventoryService, InventoryService.Api.Application.InventoryService>();
builder.Services.AddHostedService<StaleReservationCleanupWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/stock/set", async (SetStockRequest request, IInventoryService service, CancellationToken ct) =>
{
    var result = await service.SetStockAsync(request.ProductId, request.Quantity, ct);
    return result.IsSuccess
        ? Results.Ok()
        : Results.BadRequest(new { result.ErrorCode, result.ErrorMessage });
});

app.MapPost("/reservations", async (ReserveRequest request, IInventoryService service, CancellationToken ct) =>
{
    var result = await service.ReserveAsync(
        request.ReservationId,
        request.Items.Select(x => (x.ProductId, x.Quantity)).ToArray(),
        ct);

    return result.IsSuccess
        ? Results.Ok()
        : Results.BadRequest(new { result.ErrorCode, result.ErrorMessage });
});

app.MapPost("/reservations/cancel", async (ReservationActionRequest request, IInventoryService service, CancellationToken ct) =>
{
    var result = await service.CancelReservationAsync(request.ReservationId, ct);
    if (result.IsSuccess)
    {
        return Results.Ok();
    }

    return result.ErrorCode == "reservation_not_found"
        ? Results.NotFound(new { result.ErrorCode, result.ErrorMessage })
        : Results.BadRequest(new { result.ErrorCode, result.ErrorMessage });
});

app.MapPost("/reservations/writeoff", async (ReservationActionRequest request, IInventoryService service, CancellationToken ct) =>
{
    var result = await service.WriteOffReservationAsync(request.ReservationId, ct);
    if (result.IsSuccess)
    {
        return Results.Ok();
    }

    return result.ErrorCode == "reservation_not_found"
        ? Results.NotFound(new { result.ErrorCode, result.ErrorMessage })
        : Results.BadRequest(new { result.ErrorCode, result.ErrorMessage });
});

app.MapGet("/stock/{productId:guid}", async (Guid productId, IInventoryService service, CancellationToken ct) =>
{
    var available = await service.GetAvailableQuantityAsync(productId, ct);
    return available is null
        ? Results.NotFound()
        : Results.Ok(new GetStockResponse(productId, available.Value));
});

app.Run();

