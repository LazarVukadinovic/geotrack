using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260525193000_AddGeoBoundariesTerritoryMetadata")]
    public partial class AddGeoBoundariesTerritoryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryIso3",
                table: "Territories",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryAdmLevel",
                table: "Territories",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoundaryImportedAt",
                table: "Territories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryLicense",
                table: "Territories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryReleaseType",
                table: "Territories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundarySourceUrl",
                table: "Territories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_CountryIso3",
                table: "Territories",
                column: "CountryIso3",
                unique: true,
                filter: "\"CountryIso3\" IS NOT NULL");

            migrationBuilder.Sql("""
                ALTER TABLE "Territories"
                ALTER COLUMN "BoundaryGeometry" TYPE geometry (Geometry, 4326)
                USING "BoundaryGeometry"::geometry;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Regions"
                ALTER COLUMN "BoundaryGeometry" TYPE geometry (Geometry, 4326)
                USING "BoundaryGeometry"::geometry;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Regions"
                ALTER COLUMN "BoundaryGeometry" TYPE geography (Geometry)
                USING "BoundaryGeometry"::geography;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "Territories"
                ALTER COLUMN "BoundaryGeometry" TYPE geography (Geometry)
                USING "BoundaryGeometry"::geography;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Territories_CountryIso3",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "CountryIso3",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryAdmLevel",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryImportedAt",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryLicense",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundaryReleaseType",
                table: "Territories");

            migrationBuilder.DropColumn(
                name: "BoundarySourceUrl",
                table: "Territories");
        }
    }
}
