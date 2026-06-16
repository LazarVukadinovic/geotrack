using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260616190000_AddDestinationBoundaryResolution")]
    public partial class AddDestinationBoundaryResolution : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoundaryDisplayName",
                table: "Destinations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryExternalKey",
                table: "Destinations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BoundaryImportedAt",
                table: "Destinations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundarySource",
                table: "Destinations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Destination boundaries may be Polygon or MultiPolygon. The USING clause
            // preserves every existing polygon while widening only the geography typmod.
            migrationBuilder.Sql("""
                ALTER TABLE "Destinations"
                ALTER COLUMN "BoundaryGeometry" TYPE geography (Geometry, 4326)
                USING "BoundaryGeometry"::geography;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_BoundaryExternalKey",
                table: "Destinations",
                column: "BoundaryExternalKey",
                unique: true,
                filter: "\"BoundaryExternalKey\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Destinations_BoundaryExternalKey",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryDisplayName",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryExternalKey",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryImportedAt",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundarySource",
                table: "Destinations");

            // A downgrade is safe only while all stored boundaries are polygons.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "Destinations"
                        WHERE "BoundaryGeometry" IS NOT NULL
                          AND GeometryType("BoundaryGeometry"::geometry) <> 'POLYGON'
                    ) THEN
                        RAISE EXCEPTION 'Cannot downgrade destination boundaries while MultiPolygon data exists.';
                    END IF;
                END $$;

                ALTER TABLE "Destinations"
                ALTER COLUMN "BoundaryGeometry" TYPE geography (Polygon, 4326)
                USING "BoundaryGeometry"::geography;
                """);
        }
    }
}
