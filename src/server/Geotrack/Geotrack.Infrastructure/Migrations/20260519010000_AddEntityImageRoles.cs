using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geotrack.Infrastructure.Migrations
{
    public partial class AddEntityImageRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "EntityImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");

            migrationBuilder.AddColumn<string>(
                name: "ImageRole",
                table: "PlaceContributionImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "gallery");

            migrationBuilder.Sql("""
                UPDATE "EntityImages"
                SET "ImageRole" = CASE WHEN "IsMain" THEN 'cover' ELSE 'gallery' END;

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "PlaceId", "EventId", "RegionId", "DestinationId", "TerritoryId", "LocationId"
                        ORDER BY "IsMain" DESC, "SortOrder", "CreatedAt", "Id"
                    ) AS rn
                    FROM "EntityImages"
                )
                UPDATE "EntityImages" e
                SET "ImageRole" = 'card'
                FROM ranked r
                WHERE e."Id" = r."Id" AND r.rn = 2;

                UPDATE "PlaceContributionImages"
                SET "ImageRole" = CASE WHEN "IsMain" THEN 'cover' ELSE 'gallery' END;

                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "PlaceContributionId"
                        ORDER BY "IsMain" DESC, "SortOrder", "CreatedAt", "Id"
                    ) AS rn
                    FROM "PlaceContributionImages"
                )
                UPDATE "PlaceContributionImages" p
                SET "ImageRole" = 'card'
                FROM ranked r
                WHERE p."Id" = r."Id" AND r.rn = 2;
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "EntityImages");

            migrationBuilder.DropColumn(
                name: "ImageRole",
                table: "PlaceContributionImages");
        }
    }
}
