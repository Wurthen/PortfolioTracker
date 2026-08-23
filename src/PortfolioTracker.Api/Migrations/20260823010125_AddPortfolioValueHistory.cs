using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioValueHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PortfolioHistoryPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValueEur = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioHistoryPoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryPoints_UserId",
                table: "PortfolioHistoryPoints",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioHistoryPoints_UserId_ItemId_Date",
                table: "PortfolioHistoryPoints",
                columns: new[] { "UserId", "ItemId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortfolioHistoryPoints");
        }
    }
}
