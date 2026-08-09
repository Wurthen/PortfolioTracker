using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioTracker.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceManualPriceWithAlternativeSymbol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualPrice",
                table: "PortfolioItems");

            migrationBuilder.AddColumn<string>(
                name: "AlternativeSymbol",
                table: "PortfolioItems",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAlternativeSymbol",
                table: "PortfolioItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlternativeSymbol",
                table: "PortfolioItems");

            migrationBuilder.DropColumn(
                name: "UseAlternativeSymbol",
                table: "PortfolioItems");

            migrationBuilder.AddColumn<decimal>(
                name: "ManualPrice",
                table: "PortfolioItems",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }
    }
}
