using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegionalPlaceCreateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessNotes",
                table: "Places",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccessibilityNotes",
                table: "Places",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdmissionPriceRange",
                table: "Places",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BestTimeToVisit",
                table: "Places",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingExternalLink",
                table: "Places",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Places",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalReferenceId",
                table: "Places",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OperatingHoursEnabled",
                table: "Places",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OperatingHoursJson",
                table: "Places",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Places",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Places",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "VisitorTips",
                table: "Places",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Places",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Places_CreatedByUserId",
                table: "Places",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_InternalReferenceId",
                table: "Places",
                column: "InternalReferenceId",
                unique: true,
                filter: "\"InternalReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Places_Status",
                table: "Places",
                column: "Status");

            SeedPlaceTagTypes(migrationBuilder);
            SeedPlaceTypes(migrationBuilder);
            SeedStayTypes(migrationBuilder);
            SeedPlaceTags(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Places_CreatedByUserId",
                table: "Places");

            migrationBuilder.DropIndex(
                name: "IX_Places_InternalReferenceId",
                table: "Places");

            migrationBuilder.DropIndex(
                name: "IX_Places_Status",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "AccessNotes",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "AccessibilityNotes",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "AdmissionPriceRange",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "BestTimeToVisit",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "BookingExternalLink",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "InternalReferenceId",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "OperatingHoursEnabled",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "OperatingHoursJson",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "VisitorTips",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Places");
        }

        private static void SeedPlaceTagTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "TagTypes"
                    ("Name", "Slug", "Description", "CreatedAt", "ForDestination", "ForLocation", "ForPlace", "ForEvent")
                VALUES
                    ('Place', 'place', 'Tags that can classify places.', NOW(), FALSE, FALSE, TRUE, FALSE)
                ON CONFLICT ("Slug") DO UPDATE SET
                    "ForPlace" = "TagTypes"."ForPlace" OR EXCLUDED."ForPlace",
                    "UpdatedAt" = NOW();
                """);
        }

        private static void SeedPlaceTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "PlaceTypes"
                    ("Name", "Description", "CreatedAt")
                VALUES
                    ('Beach', 'Beaches and coastal areas', NOW()),
                    ('Restaurant', 'Dining establishments', NOW()),
                    ('Cafe', 'Cafes and coffee shops', NOW()),
                    ('Landmark', 'Landmarks and monuments', NOW()),
                    ('Nature', 'Nature spots', NOW()),
                    ('Stay', 'Hotels, apartments, villas and other accommodation', NOW()),
                    ('Museum', 'Museums and galleries', NOW()),
                    ('Park', 'Parks, gardens and green spaces', NOW())
                ON CONFLICT ("Name") DO UPDATE SET
                    "Description" = EXCLUDED."Description",
                    "UpdatedAt" = NOW();
                """);
        }

        private static void SeedStayTypes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "StayTypes"
                    ("Name", "Slug", "Description", "CreatedAt")
                VALUES
                    ('Hotel', 'hotel', 'Hotel accommodation', NOW()),
                    ('Apartment', 'apartment', 'Private apartment accommodation', NOW()),
                    ('Villa', 'villa', 'Private villa accommodation', NOW())
                ON CONFLICT ("Slug") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Description" = EXCLUDED."Description",
                    "UpdatedAt" = NOW();
                """);
        }

        private static void SeedPlaceTags(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Tags"
                    ("Name", "Slug", "ForDestination", "ForLocation", "ForPlace", "ForEvent", "Description", "CreatedAt")
                VALUES
                    ('Landmark', 'landmark', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Museum', 'museum', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Restaurant', 'restaurant', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Cafe', 'cafe', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Beach', 'beach', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Beaches', 'beaches', TRUE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Nature', 'nature', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Historic', 'historic', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Cultural Site', 'cultural-site', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Scenic Views', 'scenic-views', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Family Friendly', 'family-friendly', TRUE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Outdoor', 'outdoor', FALSE, TRUE, TRUE, TRUE, '', NOW()),
                    ('Accessibility Friendly', 'accessibility-friendly', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Coastal', 'coastal', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Urban Center', 'urban-center', FALSE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Mountain & Forest', 'mountain-forest', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('National Park Access', 'national-park-access', TRUE, TRUE, TRUE, FALSE, '', NOW()),
                    ('Accommodation', 'accommodation', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Luxury', 'luxury', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Budget-Friendly', 'budget-friendly', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Romantic', 'romantic', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Pet Friendly', 'pet-friendly', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Food & Drink', 'food-drink', FALSE, FALSE, TRUE, TRUE, '', NOW()),
                    ('Fine Dining', 'fine-dining', FALSE, FALSE, TRUE, FALSE, '', NOW()),
                    ('Sea View', 'sea-view', FALSE, FALSE, TRUE, FALSE, '', NOW())
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
