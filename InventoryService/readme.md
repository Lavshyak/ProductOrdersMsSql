не актуально.
# InventoryService

Сервис кеширует остатки товара и управляет резервами, чтобы не ходить к продавцу на каждый запрос.

## Что реализовано

- Установка остатков (`POST /stock/set`)
- Создание резерва (`POST /reservations`) с idempotency по `reservationId`
- Отмена резерва (`POST /reservations/cancel`)
- Списание + удаление резерва (`POST /reservations/writeoff`)
- Получение незарезервированного остатка (`GET /stock/{productId}`)
- Автоотмена просроченных резервов фоновым воркером
- Запись событий в таблицу Outbox (`stock.set`, `reservation.added`, `reservation.removed`)

## Конфигурация

`InventoryService.Api/appsettings.json`:

- `ConnectionStrings:InventoryDb` - строка подключения к MS SQL Server
- `ReservationCleanup:ReservationTtl` - TTL резерва
- `ReservationCleanup:PollingInterval` - частота фоновой проверки
- `ReservationCleanup:BatchSize` - размер батча на один проход (вроде бы неактуально)

## Быстрый запуск

```powershell
cd C:\CodeProjects\NET\ProductOrdersMsSql

dotnet restore

dotnet run --project .\InventoryService\InventoryService.Api\InventoryService.Api.csproj
```

По умолчанию схема создается автоматически при старте через `EnsureCreated`.

## Тесты

```powershell
cd C:\CodeProjects\NET\ProductOrdersMsSql

dotnet test
```

