using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260526043000_AddDisputedTerritoryBoundaryMetadata")]
    public partial class AddDisputedTerritoryBoundaryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerritoryKind",
                table: "Territories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Country");

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeGroup",
                table: "Territories",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeId",
                table: "Territories",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryExternalKey",
                table: "Territories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_BoundaryExternalKey",
                table: "Territories",
                column: "BoundaryExternalKey",
                unique: true,
                filter: "\"BoundaryExternalKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Territories_BoundaryExternalKey",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryExternalKey",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeGroup",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeId",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "TerritoryKind",
                table: "Territories");
        }
    }
}
