using System;
using Geotrack.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513093000_RegionalDestinationCreateFields")]
    public partial class RegionalDestinationCreateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Destinations",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<Geometry>(
                name: "BoundaryGeometry",
                table: "Destinations",
                type: "geography (polygon)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryNote",
                table: "Destinations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BoundaryShapeType",
                table: "Destinations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Destinations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalReferenceId",
                table: "Destinations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Destinations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Destinations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Destinations",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_BoundaryGeometry",
                table: "Destinations",
                column: "BoundaryGeometry")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_CreatedByUserId",
                table: "Destinations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_InternalReferenceId",
                table: "Destinations",
                column: "InternalReferenceId",
                unique: true,
                filter: "\"InternalReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Destinations_Status",
                table: "Destinations",
                column: "Status");

            SeedDestinationTagTypes(migrationBuilder);
            SeedDestinationTags(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Destinations_BoundaryGeometry",
                table: "Destinations");

            migrationBuilder.DropIndex(
                name: "IX_Destinations_CreatedByUserId",
                table: "Destinations");

            migrationBuilder.DropIndex(
                name: "IX_Destinations_InternalReferenceId",
                table: "Destinations");

            migrationBuilder.DropIndex(
                name: "IX_Destinations_Status",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryGeometry",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryNote",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "BoundaryShapeType",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "InternalReferenceId",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Destinations");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Destinations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000);
        }

        private static void SeedDestinationTagTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "TagTypes"
                    ("Name", "Slug", "Description", "CreatedAt", "ForDestination", "ForLocation", "ForPlace", "ForEvent")
                VALUES
                    ('Destination', 'destination', 'Tags that can classify destinations.', NOW(), TRUE, FALSE, FALSE, FALSE),
                    ('Location', 'location', 'Tags that can classify locations.', NOW(), FALSE, TRUE, FALSE, FALSE),
                    ('Place', 'place', 'Tags that can classify places.', NOW(), FALSE, FALSE, TRUE, FALSE),
                    ('Event', 'event', 'Tags that can classify events.', NOW(), FALSE, FALSE, FALSE, TRUE)
                ON CONFLICT ("Slug") DO UPDATE SET
                    "ForDestination" = "TagTypes"."ForDestination" OR EXCLUDED."ForDestination",
                    "ForLocation" = "TagTypes"."ForLocation" OR EXCLUDED."ForLocation",
                    "ForPlace" = "TagTypes"."ForPlace" OR EXCLUDED."ForPlace",
                    "ForEvent" = "TagTypes"."ForEvent" OR EXCLUDED."ForEvent",
                    "UpdatedAt" = NOW();
                """);
        }

        private static void SeedDestinationTags(MigrationBuilder migrationBuilder)
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
                    ('Coastal', 'coastal', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Islands', 'islands', TRUE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Food & Wine', 'food-wine', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Scenic Views', 'scenic-views', TRUE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Beaches', 'beaches', TRUE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Museums', 'museums', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Wellness', 'wellness', TRUE, TRUE, TRUE, FALSE, '', NOW())
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
