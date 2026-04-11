create type GuidList as table
(
    Id uniqueidentifier not null
)
go

create table OutboxMessages
(
    Id            bigint identity
        constraint PK_OutboxMessages
            primary key,
    EventType     nvarchar(200)  not null,
    PayloadJson   nvarchar(max)  not null,
    OccurredAtUtc datetimeoffset not null
)
go

create index IX_OutboxMessages_OccurredAtUtc
    on OutboxMessages (OccurredAtUtc)
go

create table Reservations
(
    Id           uniqueidentifier not null
        constraint PK_Reservations
            primary key,
    CreatedAtUtc datetimeoffset   not null
)
go

create index IX_Reservations_CreatedAtUtc
    on Reservations (CreatedAtUtc)
go

create table StockItems
(
    Id                   uniqueidentifier not null
        constraint PK_StockItems
            primary key,
    TotalQuantity        int              not null
        constraint CK_StockItem_TotalQuantity_GE_Zero
            check ([TotalQuantity] >= 0)
        constraint CK_StockItem_TotalQuantity_GE_Zero
            check ([TotalQuantity] >= 0),
    TotalQuantityVersion int              not null,
    AvailableQuantity    int              not null
        constraint CK_StockItem_AvailableQuantity_GE_Zero
            check ([AvailableQuantity] >= 0)
        constraint CK_StockItem_AvailableQuantity_GE_Zero
            check ([AvailableQuantity] >= 0),
    constraint CK_StockItem_AvailableQuantity_LE_TotalQuantity
        check ([AvailableQuantity] <= [TotalQuantity]),
    constraint CK_StockItem_AvailableQuantity_LE_TotalQuantity
        check ([AvailableQuantity] <= [TotalQuantity])
)
go

create table __EFMigrationsHistory
(
    MigrationId    nvarchar(150) not null
        constraint PK___EFMigrationsHistory
            primary key,
    ProductVersion nvarchar(32)  not null
)
go

-- Cyclic dependencies found

create table ReservationItems
(
    Id            bigint identity,
    ReservationId uniqueidentifier not null
        constraint FK_ReservationItems_Reservations_ReservationId
            references Reservations (ReservationId)
            on delete cascade,
    ProductId     uniqueidentifier not null,
    Quantity      int              not null
        constraint CK_StockItem_Quantity_GE_Zero
            check ([Quantity] >= 0),
    constraint PK_ReservationItems
        primary key ()
)
go

create table ReservationItems
(
    ReservationId uniqueidentifier not null
        constraint FK_ReservationItems_Reservations_ReservationId
            references Reservations
            on delete cascade,
    ProductId     uniqueidentifier not null,
    Quantity      int              not null
        constraint CK_StockItem_Quantity_GE_Zero
            check ([Quantity] >= 0),
    constraint PK_ReservationItems
        primary key (ReservationId, ProductId)
)
go

create index IX_ReservationItems_ProductId
    on ReservationItems (ProductId)
go

