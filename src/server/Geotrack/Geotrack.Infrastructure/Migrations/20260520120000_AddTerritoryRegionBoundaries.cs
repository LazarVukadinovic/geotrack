using Microsoft.EntityFrameworkCore.Migrations;
using Geotrack.Infrastructure;
using NetTopologySuite.Geometries;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260520120000_AddTerritoryRegionBoundaries")]
    public partial class AddTerritoryRegionBoundaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Geometry>(
                name: "BoundaryGeometry",
                table: "Territories",
                type: "geography (geometry)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeType",
                table: "Territories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundarySource",
                table: "Territories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OsmAdminLevel",
                table: "Territories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OsmRelationId",
                table: "Territories",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Geometry>(
                name: "BoundaryGeometry",
                table: "Regions",
                type: "geography (geometry)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeType",
                table: "Regions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundarySource",
                table: "Regions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Regions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OsmAdminLevel",
                table: "Regions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OsmRelationId",
                table: "Regions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_BoundaryGeometry",
                table: "Territories",
                column: "BoundaryGeometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_BoundaryGeometry",
                table: "Regions",
                column: "BoundaryGeometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CreatedByUserId",
                table: "Regions",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Territories_BoundaryGeometry",
                table: "Territories");

            migrationBuilder.DropIndex(
                name: "IX_Regions_BoundaryGeometry",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_CreatedByUserId",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryGeometry",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeType",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundarySource",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "OsmAdminLevel",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "OsmRelationId",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryGeometry",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeType",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "BoundarySource",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "OsmAdminLevel",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "OsmRelationId",
                table: "Regions");
        }
    }
}
