using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RegionalEventCreateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "LocationId",
                table: "Events",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Events",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "AgeRestriction",
                table: "Events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttendanceLimit",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DestinationId",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalTicketLink",
                table: "Events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalReferenceId",
                table: "Events",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFree",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRecurring",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParentEntityType",
                table: "Events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Location");

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Events",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueName",
                table: "Events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Events" AS e
                SET "DestinationId" = l."DestinationId"
                FROM "Locations" AS l
                WHERE e."LocationId" = l."Id"
                  AND e."DestinationId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "DestinationId",
                table: "Events",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "Tags"
                SET "ForEvent" = TRUE
                WHERE "Slug" IN (
                    'history',
                    'music',
                    'festival',
                    'concert',
                    'sports',
                    'exhibition',
                    'nightlife',
                    'workshop',
                    'community',
                    'art',
                    'seasonal',
                    'free-entry',
                    'ticketed'
                );

                INSERT INTO "Tags" ("Name", "Slug", "ForDestination", "ForLocation", "ForPlace", "ForEvent", "Description", "CreatedAt")
                SELECT seed."Name", seed."Slug", FALSE, FALSE, FALSE, TRUE, seed."Description", NOW()
                FROM (VALUES
                    ('Music', 'music', 'Event-compatible tag.'),
                    ('Festival', 'festival', 'Event-compatible tag.'),
                    ('Concert', 'concert', 'Event-compatible tag.'),
                    ('Sports', 'sports', 'Event-compatible tag.'),
                    ('Exhibition', 'exhibition', 'Event-compatible tag.'),
                    ('Nightlife', 'nightlife', 'Event-compatible tag.'),
                    ('Workshop', 'workshop', 'Event-compatible tag.'),
                    ('Community', 'community', 'Event-compatible tag.'),
                    ('Art', 'art', 'Event-compatible tag.'),
                    ('Seasonal', 'seasonal', 'Event-compatible tag.'),
                    ('Free Entry', 'free-entry', 'Event-compatible tag.'),
                    ('Ticketed', 'ticketed', 'Event-compatible tag.')
                ) AS seed("Name", "Slug", "Description")
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "Tags" AS tag
                    WHERE LOWER(tag."Slug") = LOWER(seed."Slug")
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Events_DestinationId",
                table: "Events",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_InternalReferenceId",
                table: "Events",
                column: "InternalReferenceId",
                unique: true,
                filter: "\"InternalReferenceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Status",
                table: "Events",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Destinations_DestinationId",
                table: "Events",
                column: "DestinationId",
                principalTable: "Destinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Destinations_DestinationId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_DestinationId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_InternalReferenceId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AgeRestriction",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AttendanceLimit",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ExternalTicketLink",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "InternalReferenceId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsAllDay",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsFree",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsRecurring",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ParentEntityType",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "VenueName",
                table: "Events");

            migrationBuilder.AlterColumn<int>(
                name: "LocationId",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000);
        }
    }
}
