using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegionalLocationCreateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Geometry>(
                name: "BoundaryGeometry",
                table: "Locations",
                type: "geography (polygon)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryNote",
                table: "Locations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeType",
                table: "Locations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Locations",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InternalReferenceId",
                table: "Locations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Locations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Locations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Locations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_BoundaryGeometry",
                table: "Locations",
                column: "BoundaryGeometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CreatedByUserId",
                table: "Locations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_InternalReferenceId",
                table: "Locations",
                column: "InternalReferenceId",
                unique: true,
                filter: "\"InternalReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Status",
                table: "Locations",
                column: "Status");

            SeedLocationTagTypes(migrationBuilder);
            SeedLocationTags(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Locations_BoundaryGeometry",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_CreatedByUserId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_InternalReferenceId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_Status",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "BoundaryGeometry",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "BoundaryNote",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeType",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "InternalReferenceId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Locations");
        }

        private static void SeedLocationTagTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "TagTypes"
                    ("Name", "Slug", "Description", "CreatedAt", "ForDestination", "ForLocation", "ForPlace", "ForEvent")
                VALUES
                    ('Location', 'location', 'Tags that can classify locations.', NOW(), FALSE, TRUE, FALSE, FALSE)
                ON CONFLICT ("Slug") DO UPDATE SET
                    "ForLocation" = "TagTypes"."ForLocation" OR EXCLUDED."ForLocation",
                    "UpdatedAt" = NOW();
                """);
        }

        private static void SeedLocationTags(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Tags"
                    ("Name", "Slug", "ForDestination", "ForLocation", "ForPlace", "ForEvent", "Description", "CreatedAt")
                VALUES
                    ('Nature & Outdoors', 'nature-outdoors', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Culture & Heritage', 'culture-heritage', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Urban & City', 'urban-city', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Adventure & Sport', 'adventure-sport', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Family Friendly', 'family-friendly', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Historic', 'historic', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Nature', 'nature', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Scenic Views', 'scenic-views', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Park', 'park', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Museum', 'museum', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Cultural Site', 'cultural-site', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Urban Area', 'urban-area', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Old Town', 'old-town', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Waterfront', 'waterfront', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Walking Area', 'walking-area', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Food & Wine', 'food-wine', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Religious Heritage', 'religious-heritage', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Outdoor', 'outdoor', FALSE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Landmark', 'landmark', FALSE, TRUE, TRUE, FALSE, '', NOW())
                ON CONFLICT ("Slug") DO UPDATE SET
                    "ForDestination" = "Tags"."ForDestination" OR EXCLUDED."ForDestination",
                    "ForLocation" = "Tags"."ForLocation" OR EXCLUDED."ForLocation",
                    "ForPlace" = "Tags"."ForPlace" OR EXCLUDED."ForPlace",
                    "ForEvent" = "Tags"."ForEvent" OR EXCLUDED."ForEvent",
                    "UpdatedAt" = NOW();
                """);
        }
    }
}
