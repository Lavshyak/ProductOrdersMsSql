using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Api.Infrastructure.InventoryDbMigrations
{
    /// <inheritdoc />
    public partial class Rename_StockItem_TotalQuantityVersion_To_TotalQuantityVersionForSetStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TotalQuantityVersion",
                table: "StockItems",
                newName: "TotalQuantityVersionForSetStock");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TotalQuantityVersionForSetStock",
                table: "StockItems",
                newName: "TotalQuantityVersion");
        }
    }
}
