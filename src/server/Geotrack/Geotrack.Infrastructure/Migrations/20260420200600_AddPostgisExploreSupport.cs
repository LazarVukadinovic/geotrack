using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgisExploreSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTags_UserId",
                table: "UserTags");

            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS postgis;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.AddColumn<Point>(
                name: "Coordinates",
                table: "Places",
                type: "geography (point)",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "Coordinates",
                table: "Locations",
                type: "geography (point)",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "Coordinates",
                table: "Events",
                type: "geography (point)",
                nullable: true);

            migrationBuilder.AddColumn<Point>(
                name: "Coordinates",
                table: "Destinations",
                type: "geography (point)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Destinations""
                SET ""Coordinates"" =
                    ST_SetSRID(ST_MakePoint(""Longitude"", ""Latitude""), 4326)::geography
                WHERE ""Coordinates"" IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Locations""
                SET ""Coordinates"" =
                    ST_SetSRID(ST_MakePoint(""Longitude"", ""Latitude""), 4326)::geography
                WHERE ""Coordinates"" IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Places""
                SET ""Coordinates"" =
                    ST_SetSRID(ST_MakePoint(""Longitude"", ""Latitude""), 4326)::geography
                WHERE ""Coordinates"" IS NULL;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""Events"" AS e
                SET ""Coordinates"" = COALESCE(
                    (
                        SELECT ST_SetSRID(ST_MakePoint(p.""Longitude"", p.""Latitude""), 4326)::geography
                        FROM ""Places"" AS p
                        WHERE p.""Id"" = e.""PlaceId""
                    ),
                    (
                        SELECT ST_SetSRID(ST_MakePoint(l.""Longitude"", l.""Latitude""), 4326)::geography
                        FROM ""Locations"" AS l
                        WHERE l.""Id"" = e.""LocationId""
                    )
                )
                WHERE ""Coordinates"" IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Places_Coordinates",
                table: "Places",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Coordinates",
                table: "Locations",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Coordinates",
                table: "Events",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Coordinates",
                table: "Destinations",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "GIST");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Places_Coordinates",
                table: "Places");

            migrationBuilder.DropIndex(
                name: "IX_Locations_Coordinates",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Events_Coordinates",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Destinations_Coordinates",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "Coordinates",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "Coordinates",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Coordinates",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Coordinates",
                table: "Destinations");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_UserTags_UserId",
                table: "UserTags",
                column: "UserId");
        }
    }
}
