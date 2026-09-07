using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSafeBack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SafeBackAmount",
                table: "PortfolioItems",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SafeBackShares",
                table: "PortfolioItems",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SafeBackAmount",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "SafeBackShares",
                table: "PortfolioItems");
        }
    }
}
