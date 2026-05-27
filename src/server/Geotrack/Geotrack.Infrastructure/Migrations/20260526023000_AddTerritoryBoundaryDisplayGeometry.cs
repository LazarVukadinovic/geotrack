using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260526023000_AddTerritoryBoundaryDisplayGeometry")]
    public partial class AddTerritoryBoundaryDisplayGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Geometry>(
                name: "BoundaryDisplayGeometry",
                table: "Territories",
                type: "geometry (geometry, 4326)",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Territories"
                SET "BoundaryDisplayGeometry" = ST_SetSRID(
                    ST_SimplifyPreserveTopology("BoundaryGeometry"::geometry, 0.05),
                    4326)
                WHERE "BoundaryGeometry" IS NOT NULL
                  AND "BoundaryDisplayGeometry" IS NULL
                  AND NOT ST_IsEmpty("BoundaryGeometry"::geometry)
                  AND ST_IsValid("BoundaryGeometry"::geometry)
                  AND GeometryType("BoundaryGeometry"::geometry) IN ('POLYGON', 'MULTIPOLYGON');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundaryDisplayGeometry",
                table: "Territories");
        }
    }
}
