using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryService.Api.Infrastructure.InventoryDbMigrations
{
    /// <inheritdoc />
    public partial class AddGuidList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TYPE dbo.GuidList AS TABLE
                (
                    Id UNIQUEIDENTIFIER NOT NULL
                );
                GO
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
